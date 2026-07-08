using Spectrum.Base;
using Spectrum.LEDs;

namespace Spectrum {

  /**
   * Drives the dome during "Set Dome Mapping" calibration. It lights exactly one
   * physical controller cable at a time (raw, ignoring the saved permutation) so
   * the user can see which sector/cable that controller output actually reaches
   * and record it. It is controlled through the shared DomeCalibrationState
   * (written by both calibration UIs):
   *   - Active gates it; while true it returns a very high priority so it
   *     overrides every other dome visualizer (exclusive control).
   *   - CableIndex selects which controller cable to light, encoded as
   *     box*2 + half (half 0 = ethernet A, 1 = B), or -1 for all-off.
   * It only repaints when that selection changes, so the lit cable simply holds
   * between user clicks.
   */
  class LEDDomeMappingCalibrationVisualizer : Visualizer {

    private readonly Configuration config;
    private readonly DomeCalibrationState calibration;
    private readonly LEDDomeOutput dome;
    // Last rendered selection; -2 is a sentinel that never equals a real cable
    // index or -1, so the first Visualize() always paints.
    private int lastCableIndex = -2;
    private bool lastActive = false;

    public LEDDomeMappingCalibrationVisualizer(
      Configuration config,
      DomeCalibrationState calibration,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.calibration = calibration;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
    }

    public int Priority {
      get {
        return this.calibration.Active ? 10000 : 0;
      }
    }

    private bool enabled = false;
    public bool Enabled {
      get {
        return this.enabled;
      }
      set {
        if (value == this.enabled) {
          return;
        }
        this.enabled = value;
      }
    }

    public Input[] GetInputs() {
      return System.Array.Empty<Input>();
    }

    public void Visualize() {
      int cableIndex = this.calibration.CableIndex;
      bool active = this.calibration.Active;
      if (active == this.lastActive && cableIndex == this.lastCableIndex) {
        return;
      }
      this.lastActive = active;
      this.lastCableIndex = cableIndex;

      // Blank the whole dome so only the selected cable remains lit.
      for (int i = 0; i < LEDDomeOutput.GetNumStruts(); i++) {
        int leds = LEDDomeOutput.GetNumLEDs(i);
        for (int j = 0; j < leds; j++) {
          this.dome.SetPixelRaw(i, j, 0x000000);
        }
      }

      if (cableIndex >= 0 && cableIndex < LEDDomeOutput.NumCables) {
        int box = cableIndex / 2;
        int half = cableIndex % 2;
        // Bright white, capped only by the max-brightness safety limit so the
        // lit cable is easy to spot on the physical dome during setup.
        byte brightnessByte = (byte)(0xFF * this.config.domeMaxBrightness);
        int color = brightnessByte << 16
          | brightnessByte << 8
          | brightnessByte;
        foreach (int strutIndex in
            LEDDomeOutput.GetControllerCableStruts(box, half)) {
          int leds = LEDDomeOutput.GetNumLEDs(strutIndex);
          for (int j = 0; j < leds; j++) {
            this.dome.SetPixelRaw(strutIndex, j, color);
          }
        }
      }

      this.dome.Flush();
    }

  }

}
