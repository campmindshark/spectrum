namespace Spectrum {

  // Transient control state for the "Set Dome Mapping" calibration — which
  // cable the dome is currently lighting, and whether the flow is running at
  // all. Split out of Configuration (arch_issues item 5): it is never
  // persisted, and nothing subscribes to changes. Writers are the two
  // calibration UIs (the native DomeMappingWindow on the UI thread; the web
  // DomeCalibrationController); the reader is
  // LEDDomeMappingCalibrationVisualizer, polling from the operator thread each
  // tick — volatile fields are the whole synchronization contract that
  // polling needs. Created and exposed by the Operator, like RuntimeTelemetry.
  public class DomeCalibrationState {

    // While true the calibration visualizer holds the dome at priority 10000,
    // overriding every normal visualizer.
    public volatile bool Active;

    // Which controller cable to light, encoded as box*2 + half
    // (half 0 = ethernet A, 1 = B), or -1 for all-off.
    public volatile int CableIndex = -1;
  }
}
