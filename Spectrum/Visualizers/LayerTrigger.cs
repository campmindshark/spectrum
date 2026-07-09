using System.Collections.Generic;
using Spectrum.Base;

namespace Spectrum.Visualizers {

  // Edge-detects a triggerable layer's fire condition. A OneShot layer is
  // always fireable by two live sources at once: the Manual source (a monotonic
  // per-layer fire counter bumped by the native/web Fire button), and — when
  // the layer's "button" param names a wand button (1/2/3; 0 = Unbound) — a
  // wand button's 0->nonzero transition. Fired() OR's them together. One
  // instance per triggerable layer, constructed alongside it — unlike
  // OrientationCenter/
  // BeatBroadcaster this is *not* shared across layers, since each instance
  // tracks its own prior-frame baseline (per-device actionFlag, last-seen
  // manual counter) and reads its own layer's counter.
  //
  // actionFlag is a level, not an edge: OrientationInput.ProcessDatagram sets
  // it while a button is held and clears it to 0 on release (already
  // debounced there). A "press" is the 0->nonzero transition, so Fired()
  // must be called once per frame even while the layer is mid-playthrough,
  // or an edge occurring during that window is missed.
  class LayerTrigger {

    private readonly Configuration config;
    private readonly OrientationInput orientationInput;
    private readonly string layerKey;

    // Previous frame's actionFlag per device id, so a held button (flag stays
    // nonzero across frames) only fires once.
    private readonly Dictionary<int, int> lastActionFlag =
      new Dictionary<int, int>();
    // -1 = not yet baselined. The first frame only records the current
    // counter rather than firing, so a manual counter already bumped in a
    // saved config doesn't fire the instant the layer is (re)constructed.
    private int lastManualCounter = -1;

    public LayerTrigger(
      Configuration config, OrientationInput orientationInput, string layerKey
    ) {
      this.config = config;
      this.orientationInput = orientationInput;
      this.layerKey = layerKey;
    }

    // True exactly on the frame the layer newly fires from any live source.
    // `button` is the wand actionFlag value to watch (1/2/3), or 0 (Unbound)
    // for Manual-only. Both sources are polled every frame — evaluate each
    // before OR-ing so neither is skipped by short-circuiting (the button poll
    // must run to keep advancing its per-device baseline).
    public bool Fired(int button) {
      bool manual = this.ManualFired();
      bool buttonFired = button > 0 && this.ButtonFired(button);
      return manual || buttonFired;
    }

    // A device's actionFlag transitions 0 -> `button` (1/2/3). Respects
    // orientationDeviceSpotlight the same way OrientationCenter does: a
    // non-negative spotlight restricts firing to that one wand; -1 (none) /
    // -2 (all ignored for drawing, but a button press is still a button
    // press) let every connected wand fire it.
    private bool ButtonFired(int button) {
      int spotlight = this.config.orientationDeviceSpotlight;
      Dictionary<int, OrientationDevice> devices =
        this.orientationInput.DevicesSnapshot();
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

    // Whether config.domeLayerFireCounters[layerKey] changed since the last
    // frame. A counter rather than a bool: the native/web Fire buttons just
    // increment it, so two clients firing around the same time never race
    // over who resets a shared flag.
    private bool ManualFired() {
      int counter = 0;
      this.config.domeLayerFireCounters?.TryGetValue(this.layerKey, out counter);
      if (this.lastManualCounter == -1) {
        this.lastManualCounter = counter;
        return false;
      }
      bool fired = counter != this.lastManualCounter;
      this.lastManualCounter = counter;
      return fired;
    }
  }
}
