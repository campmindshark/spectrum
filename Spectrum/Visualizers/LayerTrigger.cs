using System.Collections.Generic;
using System.Diagnostics;
using Spectrum.Audio;
using Spectrum.Base;

namespace Spectrum.Visualizers {

  // Edge-detects a triggerable layer's fire condition, OR-ing up to four live
  // sources:
  //   - Manual (always on): a monotonic per-layer fire counter bumped by the
  //     native/web Fire button.
  //   - Button (always on when bound): the layer's "button" param names a wand
  //     button (1/2/3; 0 = Unbound), fired on that button's 0->nonzero
  //     transition.
  //   - Beat: a wrap of BeatBroadcaster.ProgressThroughMeasure. Selected by the
  //     `source` arg (the layer's "trigger" param).
  //   - Audio: audio.Volume crossing a threshold, re-armed on a wall-clock
  //     interval. Also selected by `source`.
  // Manual and Button are always evaluated regardless of `source`; Beat and
  // Audio fire only when `source` names them (a layer has exactly one
  // autonomous source, plus the always-on Manual/Button). One instance per
  // triggerable layer, constructed alongside it — unlike OrientationCenter/
  // BeatBroadcaster this is *not* shared across layers, since each instance
  // tracks its own prior-frame baseline (per-device actionFlag, last-seen
  // manual counter, last beat progress, audio re-arm clock) and reads its own
  // layer's counter.
  //
  // actionFlag is a level, not an edge: OrientationInput.ProcessDatagram sets
  // it while a button is held and clears it to 0 on release (already
  // debounced there). A "press" is the 0->nonzero transition, so Fired()
  // must be called once per frame even while the layer is mid-playthrough,
  // or an edge occurring during that window is missed.
  class LayerTrigger {

    private readonly Configuration config;
    private readonly OrientationInput orientationInput;
    private readonly BeatBroadcaster beat;
    private readonly AudioInput audio;
    private readonly string instanceId;

    // Previous frame's actionFlag per device id, so a held button (flag stays
    // nonzero across frames) only fires once.
    private readonly Dictionary<int, int> lastActionFlag =
      new Dictionary<int, int>();
    // -1 = not yet baselined. The first frame only records the current
    // counter rather than firing, so a manual counter already bumped in a
    // saved config doesn't fire the instant the layer is (re)constructed.
    private int lastManualCounter = -1;
    // Prior frame's ProgressThroughMeasure, so a beat boundary (the value
    // wrapping back down) is detectable. -1 = not yet baselined, same first-
    // frame no-fire idiom as lastManualCounter.
    private double lastProgress = -1;
    // Wall-clock since the last Audio fire, for the re-arm interval. Not
    // started until the first Audio evaluation.
    private readonly Stopwatch audioRearm = new Stopwatch();

    // beat/audio are optional: the OneShot layers (Wave/Metaball) that only use
    // Manual + Button pass null and never select the Beat/Audio sources.
    public LayerTrigger(
      Configuration config, OrientationInput orientationInput, string instanceId,
      BeatBroadcaster beat = null, AudioInput audio = null
    ) {
      this.config = config;
      this.orientationInput = orientationInput;
      this.instanceId = instanceId;
      this.beat = beat;
      this.audio = audio;
    }

    // Manual + Button only (Wave/Metaball): no autonomous source.
    public bool Fired(int button) {
      return this.Fired(button, 0, 0, 0);
    }

    // True exactly on the frame the layer newly fires from any live source.
    // `button` is the wand actionFlag value to watch (1/2/3), or 0 (Unbound).
    // `source` selects the autonomous source, matching the "trigger" param's
    // Options order: 0 = Manual (none), 1 = Beat, 2 = Audio. audioThreshold /
    // audioIntervalMs tune the Audio source (ignored otherwise). Each source is
    // evaluated before OR-ing so none is skipped by short-circuiting — the
    // button poll and beat baseline must run every frame regardless of the
    // outcome.
    public bool Fired(
      int button, int source, double audioThreshold, double audioIntervalMs
    ) {
      bool manual = this.ManualFired();
      bool buttonFired = button > 0 && this.ButtonFired(button);
      bool beatFired = this.BeatWrapped() && source == 1;
      bool audioFired = source == 2
        && this.AudioTransient(audioThreshold, audioIntervalMs);
      return manual || buttonFired || beatFired || audioFired;
    }

    // A device's actionFlag transitions 0 -> `button` (1/2/3). Respects
    // orientationDeviceSpotlight the same way OrientationCenter does: a
    // non-negative spotlight restricts firing to that one wand; -1 (none) /
    // -2 (all ignored for drawing, but a button press is still a button
    // press) let every connected wand fire it.
    private bool ButtonFired(int button) {
      int spotlight = this.config.orientationDeviceSpotlight;
      IReadOnlyDictionary<int, OrientationDevice> devices =
        this.orientationInput.OperatorFrameDevices;
      bool fired = false;
      foreach (var kvp in devices) {
        int deviceId = kvp.Key;
        int flag = kvp.Value.actionFlag;
        this.lastActionFlag.TryGetValue(deviceId, out int prev);
        this.lastActionFlag[deviceId] = flag;
        if (prev == 0 && flag == button
            && (spotlight < 0 || spotlight == deviceId)) {
          fired = true;
        }
      }
      return fired;
    }

    // Whether config.domeLayerFireCounters[instanceId] changed since the last
    // frame. A counter rather than a bool: the native/web Fire buttons just
    // increment it, so two clients firing around the same time never race
    // over who resets a shared flag.
    private bool ManualFired() {
      int counter = 0;
      this.config.domeLayerFireCounters?.TryGetValue(
        this.instanceId, out counter);
      if (this.lastManualCounter == -1) {
        this.lastManualCounter = counter;
        return false;
      }
      bool fired = counter != this.lastManualCounter;
      this.lastManualCounter = counter;
      return fired;
    }

    // Whether a beat boundary passed since the last frame: ProgressThroughMeasure
    // runs 0->1 across a beat and wraps back down at the boundary, so a fire is
    // the frame where progress decreases. lastProgress is advanced every frame
    // (not just when Beat is the selected source) so switching to Beat mid-run
    // starts from a fresh baseline rather than firing on a stale delta. With no
    // tempo established ProgressThroughMeasure is a constant 0, which never
    // wraps — so a Beat-triggered layer simply waits (Manual still fires it).
    private bool BeatWrapped() {
      if (this.beat == null) {
        return false;
      }
      double progress = this.beat.ProgressThroughMeasure;
      bool wrapped = this.lastProgress != -1 && progress < this.lastProgress;
      this.lastProgress = progress;
      return wrapped;
    }

    // Whether the audio level is above threshold and the re-arm interval has
    // elapsed since the last audio fire. The interval (wall-clock ms) debounces
    // a sustained loud passage into periodic fires rather than one per frame.
    // The stopwatch arms on the first evaluation and then fires on the first
    // loud frame once at least intervalMs has elapsed (matching Stamp's old
    // "wait an interval, then fire when loud" cadence).
    private bool AudioTransient(double threshold, double intervalMs) {
      if (this.audio == null) {
        return false;
      }
      if (!this.audioRearm.IsRunning) {
        this.audioRearm.Restart();
        return false;
      }
      if (this.audio.Volume > threshold
          && this.audioRearm.Elapsed.TotalMilliseconds >= intervalMs) {
        this.audioRearm.Restart();
        return true;
      }
      return false;
    }
  }
}
