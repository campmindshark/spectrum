using System;
using System.Linq;
using Spectrum.Base;
using Spectrum.LEDs;

namespace Spectrum {

  // Drives the two independent calibration diagnostics. Raw controller cable
  // or port selections are written only to unpermuted OPC addresses. Logical
  // endpoint/path candidates are written only to the native/web simulator
  // queues. Comparing those halves separately guarantees Previous/Next can
  // repaint the candidate without disturbing the physical output.
  class LEDDomeMappingCalibrationVisualizer : Visualizer {
    private readonly Configuration config;
    private readonly DomeCalibrationState calibration;
    private readonly LEDDomeOutput dome;
    private DomeCalibrationSelection lastSelection;
    private bool lastNativeSimulatorConsumer;
    private bool lastWebSimulatorConsumer;

    public LEDDomeMappingCalibrationVisualizer(
      Configuration config,
      DomeCalibrationState calibration,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.calibration = calibration;
      this.dome = dome;
    }

    public int Priority => this.calibration.ShouldOverride ? 10000 : 0;

    private bool enabled;
    public bool Enabled {
      get => this.enabled;
      set => this.enabled = value;
    }

    public Input[] GetInputs() => Array.Empty<Input>();

    public void Visualize() {
      DomeCalibrationSelection next = this.calibration.Snapshot();
      bool simulatorConsumerChanged =
        this.dome.SimulatorHasConsumer != this.lastNativeSimulatorConsumer ||
        this.dome.WebSimulatorHasConsumer != this.lastWebSimulatorConsumer;
      if (ReferenceEquals(next, this.lastSelection) &&
          !simulatorConsumerChanged) {
        return;
      }

      bool hardwareChanged = this.lastSelection == null ||
        next.Active != this.lastSelection.Active ||
        next.RawControllerCable != this.lastSelection.RawControllerCable ||
        next.RawControllerBox != this.lastSelection.RawControllerBox ||
        next.RawControllerPort != this.lastSelection.RawControllerPort;
      bool simulatorChanged = this.lastSelection == null ||
        next.Active != this.lastSelection.Active ||
        next.SimulatorEndpoint != this.lastSelection.SimulatorEndpoint ||
        next.SimulatorBox != this.lastSelection.SimulatorBox ||
        next.SimulatorPath != this.lastSelection.SimulatorPath ||
        simulatorConsumerChanged;

      if (hardwareChanged) {
        this.RenderHardware(next);
      }
      if (simulatorChanged) {
        this.RenderSimulator(next);
      }
      this.lastSelection = next;
      this.lastNativeSimulatorConsumer = this.dome.SimulatorHasConsumer;
      this.lastWebSimulatorConsumer = this.dome.WebSimulatorHasConsumer;
      this.calibration.AcknowledgeRelease(next);
    }

    private void RenderHardware(DomeCalibrationSelection selection) {
      this.ClearHardware();
      if (selection.Active) {
        if (selection.RawControllerBox >= 0 &&
            selection.RawControllerPort >= 0) {
          this.PaintHardware(LEDDomeOutput.GetStripPathStruts(
            selection.RawControllerBox, selection.RawControllerPort));
        } else if (selection.RawControllerCable >= 0 &&
            selection.RawControllerCable < LEDDomeOutput.NumCables) {
          this.PaintHardware(LEDDomeOutput.GetControllerCableStruts(
            selection.RawControllerCable / 2,
            selection.RawControllerCable % 2));
        }
      }
      this.dome.FlushHardware();
    }

    private void RenderSimulator(DomeCalibrationSelection selection) {
      this.ClearSimulator();
      if (selection.Active) {
        if (selection.SimulatorBox >= 0 &&
            selection.SimulatorPath >= 0) {
          this.PaintSimulator(LEDDomeOutput.GetStripPathStruts(
            selection.SimulatorBox, selection.SimulatorPath));
        } else if (selection.SimulatorEndpoint >= 0 &&
            selection.SimulatorEndpoint < LEDDomeOutput.NumCables) {
          int endpoint = selection.SimulatorEndpoint;
          this.PaintSimulator(LEDDomeOutput.GetPhysicalCableStruts(
            endpoint / 2,
            endpoint % 2,
            this.EffectivePortMapping(endpoint / 2)));
        }
      }
      this.dome.FlushSimulator();
    }

    private int DiagnosticColor() {
      double brightness = Math.Clamp(this.config.domeMaxBrightness, 0.0, 1.0);
      byte value = (byte)(0xFF * brightness);
      return value << 16 | value << 8 | value;
    }

    private void ClearHardware() {
      for (int strut = 0; strut < LEDDomeOutput.GetNumStruts(); strut++) {
        for (int led = 0; led < LEDDomeOutput.GetNumLEDs(strut); led++) {
          this.dome.SetPixelRawHardware(strut, led, 0x000000);
        }
      }
    }

    private void ClearSimulator() {
      for (int strut = 0; strut < LEDDomeOutput.GetNumStruts(); strut++) {
        for (int led = 0; led < LEDDomeOutput.GetNumLEDs(strut); led++) {
          this.dome.SetPixelSimulator(strut, led, 0x000000);
        }
      }
    }

    private void PaintHardware(System.Collections.Generic.IEnumerable<int> struts) {
      int color = this.DiagnosticColor();
      foreach (int strut in struts) {
        for (int led = 0; led < LEDDomeOutput.GetNumLEDs(strut); led++) {
          this.dome.SetPixelRawHardware(strut, led, color);
        }
      }
    }

    private void PaintSimulator(System.Collections.Generic.IEnumerable<int> struts) {
      int color = this.DiagnosticColor();
      foreach (int strut in struts) {
        for (int led = 0; led < LEDDomeOutput.GetNumLEDs(strut); led++) {
          this.dome.SetPixelSimulator(strut, led, color);
        }
      }
    }

    private int[] EffectivePortMapping(int box) {
      DomePortMapping[] perBox = this.config.domePortMappings;
      if (perBox?.Length == LEDDomeOutput.NumDomeBoxes &&
          box >= 0 && box < perBox.Length) {
        int[] configured = perBox[box]?.ports?.ToArray();
        if (LEDDomeOutput.IsValidPortMapping(configured)) {
          return configured;
        }
      }
      return Enumerable.Range(0, LEDDomeOutput.NumPortsPerBox).ToArray();
    }
  }
}
