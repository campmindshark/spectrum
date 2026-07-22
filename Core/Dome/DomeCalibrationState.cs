using System.Threading;

namespace Spectrum {

  // Immutable diagnostic selection published by either calibration UI and
  // polled by the operator thread. Hardware and simulator coordinates are
  // deliberately separate: raw controller output identifies what is lit on
  // the wire, while the logical endpoint/path identifies the operator's
  // current simulator candidate.
  public sealed class DomeCalibrationSelection {
    public bool Active { get; }
    public int RawControllerCable { get; }
    public int RawControllerBox { get; }
    public int RawControllerPort { get; }
    public int SimulatorEndpoint { get; }
    public int SimulatorBox { get; }
    public int SimulatorPath { get; }

    public DomeCalibrationSelection(
      bool active,
      int rawControllerCable = -1,
      int rawControllerBox = -1,
      int rawControllerPort = -1,
      int simulatorEndpoint = -1,
      int simulatorBox = -1,
      int simulatorPath = -1
    ) {
      this.Active = active;
      this.RawControllerCable = rawControllerCable;
      this.RawControllerBox = rawControllerBox;
      this.RawControllerPort = rawControllerPort;
      this.SimulatorEndpoint = simulatorEndpoint;
      this.SimulatorBox = simulatorBox;
      this.SimulatorPath = simulatorPath;
    }
  }

  // Transient rendering state for dome mapping calibration. The authoritative
  // draft lives in DomeCalibrationController; this class is the small lock-free
  // handoff to LEDDomeMappingCalibrationVisualizer. Replacing one immutable
  // selection makes the raw and logical coordinates coherent to the polling
  // operator thread without persisting either one.
  public class DomeCalibrationState {
    private DomeCalibrationSelection selection;
    private DomeCalibrationSelection renderedRelease;

    public DomeCalibrationState() {
      this.selection = new DomeCalibrationSelection(false);
      this.renderedRelease = this.selection;
    }

    public bool Active => this.Snapshot().Active;

    // Keep the diagnostic visualizer at the winning priority for one final
    // operator tick after deactivation so it can flush an explicit black raw
    // frame before normal rendering resumes (including when no normal layer is
    // currently producing frames).
    public bool ShouldOverride {
      get {
        DomeCalibrationSelection current = this.Snapshot();
        return current.Active || !ReferenceEquals(
          current, Volatile.Read(ref this.renderedRelease));
      }
    }

    public DomeCalibrationSelection Snapshot() =>
      Volatile.Read(ref this.selection);

    public void ShowCable(int rawControllerCable, int simulatorEndpoint) {
      this.Publish(new DomeCalibrationSelection(
        true,
        rawControllerCable: rawControllerCable,
        simulatorEndpoint: simulatorEndpoint));
    }

    public void ShowPort(
      int rawControllerCable,
      int rawControllerBox,
      int rawControllerPort,
      int simulatorBox,
      int simulatorPath
    ) {
      this.Publish(new DomeCalibrationSelection(
        true,
        rawControllerCable: rawControllerCable,
        rawControllerBox: rawControllerBox,
        rawControllerPort: rawControllerPort,
        simulatorBox: simulatorBox,
        simulatorPath: simulatorPath));
    }

    // Keep the priority-10000 modal override while blanking both diagnostic
    // frames for a review screen or a completed box.
    public void ShowBlank() =>
      this.Publish(new DomeCalibrationSelection(true));

    public void Deactivate() {
      if (!this.Snapshot().Active) {
        return;
      }
      this.Publish(new DomeCalibrationSelection(false));
    }

    public void AcknowledgeRelease(DomeCalibrationSelection rendered) {
      if (!rendered.Active && ReferenceEquals(rendered, this.Snapshot())) {
        Volatile.Write(ref this.renderedRelease, rendered);
      }
    }

    private void Publish(DomeCalibrationSelection next) =>
      Volatile.Write(ref this.selection, next);
  }
}
