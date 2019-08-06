using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

    private static readonly int tapTempoConclusionTime = 2000;

    private readonly Configuration config;
    private List<long> currentTaps = new List<long>();
    private long startingTime = -1;
    private int measureLength = -1;
    private readonly Timer tapTempoConclusionTimer = new Timer(tapTempoConclusionTime);
    private readonly MidiLevelDriverInstance[] driversByChannel = new MidiLevelDriverInstance[] {
      null, null, null, null, null, null, null, null,
    };
    private long lastChannelInteractionTime = 0;

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

    public double ProgressThroughBeat(double factor) {
      if (
        this.startingTime == -1 ||
        this.measureLength == -1 ||
        factor == 0.0
      ) {
        return 0.0;
      }
      long timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      int distance = (int)(timestamp - this.startingTime);
      int beatLength = (int)(this.measureLength / factor);
      int progressThroughMeasure = distance % beatLength;
      return (double)progressThroughMeasure / beatLength;
    }

    public int MeasureLength {
      get {
        return this.measureLength;
      }
    }

    public void AddTap() {
      this.tapTempoConclusionTimer.Stop();
      long timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      if (
        this.currentTaps.Count > 0 &&
        timestamp - this.currentTaps.Last() > tapTempoConclusionTime
      ) {
        this.currentTaps = new List<long>();
      }
      this.currentTaps.Add(timestamp);
      this.TapTempoConcluded(null, null);
      this.tapTempoConclusionTimer.Start();
      if (this.currentTaps.Count >= 3) {
        this.UpdateBeatFromTaps();
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
      this.currentTaps = new List<long>();
      this.startingTime = -1;
      this.measureLength = -1;
      this.TapTempoConcluded(null, null);
      this.PropertyChanged?.Invoke(
        this,
        new PropertyChangedEventArgs("BPMString")
      );
    }

    private void UpdateBeatFromTaps() {
      int[] measureLengths = new int[this.currentTaps.Count - 1];
      for (int i = 0; i < this.currentTaps.Count - 1; i++) {
        measureLengths[i] =
          (int)(this.currentTaps[i + 1] - this.currentTaps[i]);
      }
      this.measureLength = (int)(measureLengths.Average());
      this.startingTime = this.currentTaps.Last();
      this.PropertyChanged?.Invoke(
        this,
        new PropertyChangedEventArgs("BPMString")
      );
    }

    private bool IsTapTempoConcluded() {
      if (this.currentTaps.Count == 0) {
        return true;
      }
      long timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      return timestamp - this.currentTaps.Last() > tapTempoConclusionTime;
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
        if (this.IsTapTempoConcluded()) {
          return "Tap";
        }
        return this.currentTaps.Count.ToString();
      }
    }

    public string BPMString {
      get {
        if (this.measureLength == -1) {
          return "[none]";
        }
        return (60000 / this.measureLength).ToString();
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
      if (this.driversByChannel[channelIndex] != null) {
        long now = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        this.driversByChannel[channelIndex].ReleaseTimestamp = now;
        this.lastChannelInteractionTime = now;
      }
    }

    public void MidiPress(MidiLevelDriverInstance newDriver) {
      if (this.GetPresetForChannelIndex(newDriver.ChannelIndex) != null) {
        this.driversByChannel[newDriver.ChannelIndex] = newDriver;
        this.lastChannelInteractionTime = newDriver.PressTimestamp;
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
      var driver = this.driversByChannel[channelIndex];
      var preset = this.GetPresetForChannelIndex(channelIndex);
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

    public void ReportMadmomBeat(int millisecondsSinceLast) {
      long timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;

      if (this.startingTime < 0) {
        this.measureLength = millisecondsSinceLast;
        this.startingTime = timestamp;
      } else {
        double totalMeasures = (double)(timestamp - this.startingTime)
          / this.measureLength;
        if (totalMeasures > 8.0) {
          totalMeasures -= 8.0;
        }
        totalMeasures = Math.Round(totalMeasures);
        this.measureLength = millisecondsSinceLast;
        this.startingTime = timestamp
          - (long)(totalMeasures * this.measureLength);
      }

      this.PropertyChanged?.Invoke(
        this,
        new PropertyChangedEventArgs("BPMString")
      );
    }

  }

}
