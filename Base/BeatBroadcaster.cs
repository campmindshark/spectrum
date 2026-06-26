using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.ComponentModel;
using System.Timers;

namespace Spectrum.Base {

  public class MidiLevelDriverInstance {
    public int ChannelIndex { get; set; }
    public long PressTimestamp { get; set; }
    public double PressVelocity { get; set; }
    public long? ReleaseTimestamp { get; set; }
  }

  public class BeatBroadcaster : INotifyPropertyChanged {

    private enum TimeRelativeTo { Timestamp, SystemBoot };

    private static readonly int tapTempoConclusionTime = 2000;

    private readonly Configuration config;
    // Guards all of the mutable beat/tap/driver state below, which is touched
    // from the Madmom output thread, the MIDI thread, the tap-tempo Timer
    // thread, the operator thread, and the UI thread. PropertyChanged is always
    // raised outside this lock to avoid reentrancy into the locked getters.
    private readonly object beatLock = new object();
    private List<long> currentTaps = new List<long>();
    private long startingTime = -1;
    private long lastMadmomReport = -1;
    private int measureLength = -1;
    private TimeRelativeTo timeRelativeTo = TimeRelativeTo.Timestamp;
    private readonly Timer tapTempoConclusionTimer = new Timer(tapTempoConclusionTime);
    private readonly MidiLevelDriverInstance[] driversByChannel = new MidiLevelDriverInstance[] {
      null, null, null, null, null, null, null, null,
    };
    private long lastChannelInteractionTime = 0;

    // Monotonic milliseconds since system boot. Unlike Environment.TickCount
    // (32-bit, wraps to negative after ~24.9 days) this is 64-bit, and unlike
    // Environment.TickCount64 it is available on .NET Framework 4.8 (not just
    // 4.8.1). Same "ms since boot" base as the value Madmom reports.
    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();

    public event PropertyChangedEventHandler PropertyChanged;

    public BeatBroadcaster(Configuration config) {
      this.config = config;
      this.tapTempoConclusionTimer.Elapsed += TapTempoConcluded;
    }

    public double ProgressThroughMeasure {
      get {
        return this.ProgressThroughBeat(1.0);
      }
    }

    // Reads timeRelativeTo, so callers must hold beatLock.
    private long currentTime {
      get {
        if (this.timeRelativeTo == TimeRelativeTo.Timestamp) {
          return DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        }
        return (long)GetTickCount64();
      }
    }

    public double ProgressThroughBeat(double factor) {
      if (factor == 0.0) {
        return 0.0;
      }
      lock (this.beatLock) {
        if (this.startingTime == -1 || this.measureLength == -1) {
          return 0.0;
        }
        int distance = (int)(this.currentTime - this.startingTime);
        int beatLength = (int)(this.measureLength / factor);
        int progressThroughMeasure = distance % beatLength;
        return (double)progressThroughMeasure / beatLength;
      }
    }

    public int MeasureLength {
      get {
        lock (this.beatLock) {
          return this.measureLength;
        }
      }
    }

    public void AddTap() {
      this.tapTempoConclusionTimer.Stop();
      long timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      bool beatUpdated = false;
      lock (this.beatLock) {
        if (
          this.currentTaps.Count > 0 &&
          timestamp - this.currentTaps.Last() > tapTempoConclusionTime
        ) {
          this.currentTaps = new List<long>();
        }
        this.currentTaps.Add(timestamp);
        if (this.currentTaps.Count >= 3) {
          this.UpdateBeatFromTaps();
          beatUpdated = true;
        }
      }
      this.TapTempoConcluded(null, null);
      this.tapTempoConclusionTimer.Start();
      if (beatUpdated) {
        this.PropertyChanged?.Invoke(
          this,
          new PropertyChangedEventArgs("BPMString")
        );
      }
    }

    private void TapTempoConcluded(object sender, ElapsedEventArgs e) {
      this.PropertyChanged?.Invoke(
        this,
        new PropertyChangedEventArgs("TapCounterBrush")
      );
      this.PropertyChanged?.Invoke(
        this,
        new PropertyChangedEventArgs("TapCounterText")
      );
    }

    public void Reset() {
      lock (this.beatLock) {
        this.currentTaps = new List<long>();
        this.startingTime = -1;
        this.measureLength = -1;
        this.lastMadmomReport = -1;
        this.timeRelativeTo = TimeRelativeTo.Timestamp;
      }
      this.TapTempoConcluded(null, null);
      this.PropertyChanged?.Invoke(
        this,
        new PropertyChangedEventArgs("BPMString")
      );
    }

    // Caller must hold beatLock. PropertyChanged("BPMString") is raised by the
    // caller after the lock is released.
    private void UpdateBeatFromTaps() {
      int[] measureLengths = new int[this.currentTaps.Count - 1];
      for (int i = 0; i < this.currentTaps.Count - 1; i++) {
        measureLengths[i] =
          (int)(this.currentTaps[i + 1] - this.currentTaps[i]);
      }
      this.measureLength = (int)(measureLengths.Average());
      this.startingTime = this.currentTaps.Last();
      this.lastMadmomReport = -1;
      this.timeRelativeTo = TimeRelativeTo.Timestamp;
    }

    // beatLock is reentrant (Monitor), so this is safe to call from other
    // locked getters below.
    private bool IsTapTempoConcluded() {
      lock (this.beatLock) {
        if (this.currentTaps.Count == 0) {
          return true;
        }
        long timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        return timestamp - this.currentTaps.Last() > tapTempoConclusionTime;
      }
    }

    public Brush TapCounterBrush {
      get {
        if (this.IsTapTempoConcluded()) {
          return new SolidColorBrush(Colors.Black);
        }
        return new SolidColorBrush(Colors.ForestGreen);
      }
    }

    public string TapCounterText {
      get {
        lock (this.beatLock) {
          if (this.IsTapTempoConcluded()) {
            return "Tap";
          }
          return this.currentTaps.Count.ToString();
        }
      }
    }

    public string BPMString {
      get {
        lock (this.beatLock) {
          if (this.measureLength == -1) {
            return "[none]";
          }
          return (60000 / this.measureLength).ToString();
        }
      }
    }

    public bool CurrentlyFlashedOff {
      get {
        return this.config.flashSpeed != 0.0 &&
          this.ProgressThroughBeat(this.config.flashSpeed) >= 0.5;
      }
    }

    private MidiLevelDriverPreset GetPresetForChannelIndex(int channelIndex) {
      if (!this.config.channelToMidiLevelDriverPreset.ContainsKey(channelIndex)) {
        return null;
      }
      string levelDriverPreset =
        this.config.channelToMidiLevelDriverPreset[channelIndex];
      if (
        levelDriverPreset == null ||
        !this.config.levelDriverPresets.ContainsKey(levelDriverPreset)
      ) {
        return null;
      }
      ILevelDriverPreset preset =
        this.config.levelDriverPresets[levelDriverPreset];
      return preset is MidiLevelDriverPreset
        ? (MidiLevelDriverPreset)preset
        : null;
    }

    public void MidiReleaseOnChannel(int channelIndex) {
      lock (this.beatLock) {
        if (this.driversByChannel[channelIndex] != null) {
          long now = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
          this.driversByChannel[channelIndex].ReleaseTimestamp = now;
          this.lastChannelInteractionTime = now;
        }
      }
    }

    public void MidiPress(MidiLevelDriverInstance newDriver) {
      if (this.GetPresetForChannelIndex(newDriver.ChannelIndex) != null) {
        lock (this.beatLock) {
          this.driversByChannel[newDriver.ChannelIndex] = newDriver;
          this.lastChannelInteractionTime = newDriver.PressTimestamp;
        }
      }
    }

    private double CurrentMidiLevelDriverValueWithoutReleaseForChannel(
      MidiLevelDriverInstance driver,
      MidiLevelDriverPreset preset,
      long currentTime
    ) {
      long timeSincePress = currentTime - driver.PressTimestamp;
      double realPeak = driver.PressVelocity * preset.PeakLevel;
      if (timeSincePress < preset.AttackTime) {
        return ((double)timeSincePress / preset.AttackTime) * realPeak;
      }
      long timeSinceDecayBegan = timeSincePress - preset.AttackTime;
      double realSustain = driver.PressVelocity * preset.SustainLevel;
      if (timeSinceDecayBegan > preset.DecayTime) {
        return realSustain;
      }
      return realPeak -
        (double)timeSinceDecayBegan / preset.DecayTime * (realPeak - realSustain);
    }

    public double? CurrentMidiLevelDriverValueForChannel(int channelIndex) {
      var preset = this.GetPresetForChannelIndex(channelIndex);
      lock (this.beatLock) {
        var driver = this.driversByChannel[channelIndex];
        if (driver == null || preset == null) {
          return null;
        }
        long currentTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        long timeSincePress = currentTime - driver.PressTimestamp;
        if (timeSincePress < 0.0) {
          return 0.0;
        }
        if (
          !driver.ReleaseTimestamp.HasValue ||
          currentTime < driver.ReleaseTimestamp.Value
        ) {
          return this.CurrentMidiLevelDriverValueWithoutReleaseForChannel(
            driver,
            preset,
            currentTime
          );
        }
        long timeSinceRelease = currentTime - driver.ReleaseTimestamp.Value;
        if (timeSinceRelease > preset.ReleaseTime) {
          if (currentTime > this.lastChannelInteractionTime + 5000) {
            // Pass control back to the audio stream
            return null;
          }
          return 0.0;
        }
        double levelAtRelease =
          this.CurrentMidiLevelDriverValueWithoutReleaseForChannel(
            driver,
            preset,
            driver.ReleaseTimestamp.Value
          );
        return levelAtRelease * (preset.ReleaseTime - timeSinceRelease)
          / preset.ReleaseTime;
      }
    }

    public void ReportMadmomBeat(long msSinceBoot) {
      lock (this.beatLock) {
        this.timeRelativeTo = TimeRelativeTo.SystemBoot;

        if (this.startingTime < 0 || this.lastMadmomReport < 0) {
          this.startingTime = msSinceBoot;
          this.lastMadmomReport = msSinceBoot;
          this.measureLength = -1;
          return;
        }

        long beatInterval = msSinceBoot - this.lastMadmomReport;
        if (beatInterval <= 0) {
          // Two beats reported at the same (or out-of-order) timestamp: a zero
          // interval would divide by zero in the modulo below and yields no
          // usable measure length. Just record this report and wait for the
          // next one.
          this.lastMadmomReport = msSinceBoot;
          return;
        }

        long currentMsSinceBoot = (long)GetTickCount64();
        var progressThroughMeasure = (currentMsSinceBoot - msSinceBoot)
          % beatInterval;

        double totalMeasures = (double)(currentMsSinceBoot - this.startingTime)
          / this.measureLength;
        if (totalMeasures > 8.0) {
          totalMeasures -= 8.0;
        }
        totalMeasures = Math.Floor(totalMeasures);

        this.measureLength = (int)beatInterval;
        this.lastMadmomReport = msSinceBoot;

        this.startingTime = currentMsSinceBoot
          - (long)(totalMeasures * this.measureLength)
          - progressThroughMeasure;
      }

      this.PropertyChanged?.Invoke(
        this,
        new PropertyChangedEventArgs("BPMString")
      );
    }

  }

}
