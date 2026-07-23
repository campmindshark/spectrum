using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

    private readonly IRuntimeSettingsConfiguration settings;
    // Guards all of the mutable beat/tap/driver state below, which is touched
    // from the Madmom output thread, the MIDI thread, the tap-tempo Timer
    // thread, the operator thread, and the UI thread. PropertyChanged is always
    // raised outside this lock to avoid reentrancy into the locked getters.
    private readonly object beatLock = new object();
    private List<long> currentTaps = new List<long>();
    private long startingTime = -1;
    // The most recent Madmom-reported beat timestamps (ms, in Madmom's
    // audio-stream base), used to derive tempo. These are sample-derived and so
    // immune to the tracker's bursty per-frame latency, unlike wall-clock arrival
    // times. See ReportMadmomBeat.
    private const int madmomBeatWindow = 8;
    // A gap longer than this (in Madmom's timeline) means detection dropped out
    // (silence / song change); we start a fresh tempo window rather than
    // averaging across it.
    private const long madmomBeatTimeout = 2500;
    private readonly List<long> madmomBeatTimes = new List<long>();
    private int measureLength = -1;
    private TimeRelativeTo timeRelativeTo = TimeRelativeTo.Timestamp;
    private readonly Timer tapTempoConclusionTimer = new Timer(tapTempoConclusionTime);
    private readonly MidiLevelDriverInstance?[] driversByChannel = new MidiLevelDriverInstance?[] {
      null, null, null, null, null, null, null, null,
    };
    private long lastChannelInteractionTime = 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    public BeatBroadcaster(Configuration config) {
      this.settings = config as IRuntimeSettingsConfiguration ??
        throw new ArgumentException(
          "BeatBroadcaster requires immutable runtime settings.",
          nameof(config));
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
        // Monotonic milliseconds since system boot (same base as the value
        // Madmom reports). 64-bit, so unlike Environment.TickCount it does not
        // wrap to negative after ~24.9 days.
        return Environment.TickCount64;
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
        if (beatLength <= 0) {
          // A large factor (or a tiny measureLength) can floor the beat length
          // to zero; guard the modulo/division below against DivideByZeroException.
          return 0.0;
        }
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

    // Sets the tempo directly from a pre-computed BPM, as if it had been tapped.
    // The web "Tap" button measures tap timing in the browser (so request
    // latency doesn't distort the intervals) and posts the resulting BPM here,
    // rather than replaying individual taps through AddTap. Phase is anchored to
    // now against the real-time clock, exactly as UpdateBeatFromTaps does.
    // Ignores non-positive/non-finite input.
    public void SetManualBPM(double bpm) {
      if (double.IsNaN(bpm) || double.IsInfinity(bpm) || bpm <= 0.0) {
        return;
      }
      int length = (int)Math.Round(60000.0 / bpm);
      if (length < 1) {
        // Absurdly high BPM would floor the measure length to zero, which
        // BPMString and ProgressThroughBeat both divide by; ignore it.
        return;
      }
      long timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      lock (this.beatLock) {
        this.currentTaps = new List<long>();
        this.measureLength = length;
        this.startingTime = timestamp;
        this.madmomBeatTimes.Clear();
        this.timeRelativeTo = TimeRelativeTo.Timestamp;
      }
      this.PropertyChanged?.Invoke(
        this,
        new PropertyChangedEventArgs("BPMString")
      );
    }

    private void TapTempoConcluded(object? sender, ElapsedEventArgs? e) {
      this.PropertyChanged?.Invoke(
        this,
        new PropertyChangedEventArgs("TapTempoActive")
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
        this.madmomBeatTimes.Clear();
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
      this.madmomBeatTimes.Clear();
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

    // True while a tap-tempo sequence is in progress (a tap has landed and the
    // conclusion timeout has not yet elapsed). The HUD maps this to the tap
    // button's colour via TapCounterBrushConverter; the Brush itself is kept out
    // of Base so the project needs no WPF reference.
    public bool TapTempoActive {
      get {
        return !this.IsTapTempoConcluded();
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
        BeatSettingsSnapshot snapshot = this.settings.BeatSettingsSnapshot;
        return snapshot.FlashSpeed != 0.0 &&
          this.ProgressThroughBeat(snapshot.FlashSpeed) >= 0.5;
      }
    }

    private bool TryGetPresetForChannelIndex(
      int channelIndex,
      out MidiLevelDriverSettingsSnapshot preset
    ) => this.settings.BeatSettingsSnapshot.TryGetMidiPreset(
      channelIndex, out preset);

    public void MidiReleaseOnChannel(int channelIndex) {
      lock (this.beatLock) {
        MidiLevelDriverInstance? driver = this.driversByChannel[channelIndex];
        if (driver != null) {
          long now = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
          driver.ReleaseTimestamp = now;
          this.lastChannelInteractionTime = now;
        }
      }
    }

    public void MidiPress(MidiLevelDriverInstance newDriver) {
      if (this.TryGetPresetForChannelIndex(
          newDriver.ChannelIndex, out _)) {
        lock (this.beatLock) {
          this.driversByChannel[newDriver.ChannelIndex] = newDriver;
          this.lastChannelInteractionTime = newDriver.PressTimestamp;
        }
      }
    }

    private double CurrentMidiLevelDriverValueWithoutReleaseForChannel(
      MidiLevelDriverInstance driver,
      MidiLevelDriverSettingsSnapshot preset,
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
      bool hasPreset = this.TryGetPresetForChannelIndex(
        channelIndex, out MidiLevelDriverSettingsSnapshot preset);
      lock (this.beatLock) {
        var driver = this.driversByChannel[channelIndex];
        if (driver == null || !hasPreset) {
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

    // The 1-4 beat-within-bar of the most recent Pro DJ Link beat (1 = downbeat),
    // or 0 if no Pro DJ Link beat has been seen. Carried from the gear but not yet
    // acted on for phase; it's here so the HUD can surface it and so Phase 3 can
    // expose true downbeat-aligned 4-beat measures. Guarded by beatLock.
    private int latestBeatWithinBar = 0;
    public int LatestBeatWithinBar {
      get {
        lock (this.beatLock) {
          return this.latestBeatWithinBar;
        }
      }
    }

    // Called once per beat received from a Pioneer Pro DJ Link device (a CDJ or
    // DJM), with the exact effective BPM the gear reports (track BPM scaled by the
    // deck's current pitch fader). Unlike Madmom, no estimation is needed: the
    // tempo is authoritative and the packet's arrival is itself a beat boundary,
    // so phase is anchored to the real-time clock exactly as taps are. Malformed
    // or stopped-deck values (non-positive/non-finite BPM) are dropped.
    public void ReportProDjLinkBeat(double effectiveBpm, int beatWithinBar) {
      if (
        double.IsNaN(effectiveBpm) ||
        double.IsInfinity(effectiveBpm) ||
        effectiveBpm <= 0.0
      ) {
        return;
      }
      int length = (int)Math.Round(60000.0 / effectiveBpm);
      if (length < 1) {
        // Absurdly high BPM would floor the measure length to zero, which
        // BPMString and ProgressThroughBeat both divide by; ignore it.
        return;
      }
      lock (this.beatLock) {
        this.timeRelativeTo = TimeRelativeTo.Timestamp;
        this.latestBeatWithinBar = beatWithinBar;
        long now = this.currentTime;
        // Anchor startingTime an integer number of beats behind the beat we just
        // received, so the phase stays locked to the gear while multi-beat cycles
        // (ProgressThroughBeat with factor < 1) remain continuous across beats,
        // mirroring ReportMadmomBeat. The beat's arrival instant is a beat
        // boundary, so ProgressThroughBeat(1.0) reads ~0 right now.
        double totalMeasures = 0.0;
        if (this.measureLength > 0 && this.startingTime >= 0) {
          totalMeasures = (double)(now - this.startingTime) / this.measureLength;
          if (totalMeasures > 8.0) {
            totalMeasures -= 8.0;
          }
          totalMeasures = Math.Floor(totalMeasures);
        }
        this.measureLength = length;
        this.startingTime = now - (long)(totalMeasures * this.measureLength);
        this.madmomBeatTimes.Clear();
      }
      this.PropertyChanged?.Invoke(
        this,
        new PropertyChangedEventArgs("BPMString")
      );
    }

    // Called once per beat Madmom detects, with Madmom's own (audio-stream)
    // timestamp for that beat in milliseconds. Tempo is derived from the median
    // spacing of a short window of these timestamps: a single Madmom interval is
    // noisy (a missed beat doubles it, a double-report zeroes it), and the
    // sample-derived timestamps are far more reliable for *spacing* than the
    // bursty wall-clock arrival of the BEAT: lines. The phase, however, is
    // anchored to our own real-time clock, since that is the base callers
    // measure progress against.
    public void ReportMadmomBeat(long beatTimeMs) {
      lock (this.beatLock) {
        this.timeRelativeTo = TimeRelativeTo.SystemBoot;

        // Drop the window on a discontinuity in Madmom's timeline: a long gap
        // (silence / song change) or a backwards jump (Madmom process restart,
        // which resets its clock toward zero). measureLength is left intact to
        // free-run until the window refills.
        if (this.madmomBeatTimes.Count > 0) {
          long sinceLast =
            beatTimeMs - this.madmomBeatTimes[this.madmomBeatTimes.Count - 1];
          if (sinceLast <= 0 || sinceLast > madmomBeatTimeout) {
            this.madmomBeatTimes.Clear();
          }
        }
        this.madmomBeatTimes.Add(beatTimeMs);
        if (this.madmomBeatTimes.Count > madmomBeatWindow) {
          this.madmomBeatTimes.RemoveAt(0);
        }

        long now = Environment.TickCount64;
        int estimatedLength = this.EstimateMadmomBeatLength();
        if (estimatedLength <= 0) {
          // First beat of a fresh window: no interval to measure yet. Anchor the
          // phase to now and wait for the next beat to establish a tempo.
          this.startingTime = now;
        } else {
          // Keep startingTime an integer number of beats behind the beat we just
          // saw, so the phase stays locked to real beats while multi-beat cycles
          // (ProgressThroughBeat with factor < 1) remain continuous across
          // beats. measureLength is still the previous value here, used only to
          // pick how many whole beats back to anchor.
          double totalMeasures = 0.0;
          if (this.measureLength > 0 && this.startingTime >= 0) {
            totalMeasures = (double)(now - this.startingTime)
              / this.measureLength;
            if (totalMeasures > 8.0) {
              totalMeasures -= 8.0;
            }
            totalMeasures = Math.Floor(totalMeasures);
          }
          this.measureLength = estimatedLength;
          this.startingTime = now - (long)(totalMeasures * this.measureLength);
        }
      }

      this.PropertyChanged?.Invoke(
        this,
        new PropertyChangedEventArgs("BPMString")
      );
    }

    // Caller must hold beatLock. Returns the median interval (ms) between the
    // beats currently in the window, or -1 if fewer than two beats are known.
    // The median is intentionally robust to the occasional missed/double beat,
    // which would skew a mean.
    private int EstimateMadmomBeatLength() {
      if (this.madmomBeatTimes.Count < 2) {
        return -1;
      }
      var intervals = new List<long>(this.madmomBeatTimes.Count - 1);
      for (int i = 1; i < this.madmomBeatTimes.Count; i++) {
        intervals.Add(this.madmomBeatTimes[i] - this.madmomBeatTimes[i - 1]);
      }
      intervals.Sort();
      int mid = intervals.Count / 2;
      long median = intervals.Count % 2 == 1
        ? intervals[mid]
        : (intervals[mid - 1] + intervals[mid]) / 2;
      return median > 0 ? (int)median : -1;
    }

  }

}
