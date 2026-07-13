namespace Spectrum.Base {

  // Narrow live-state boundary shared by layer renderers. Persisted
  // Configuration stays outside the renderer pipeline; the host adapts the
  // handful of cross-layer values whose changes intentionally take effect on
  // the next frame.
  public interface DomeLayerEnvironment {
    double GlobalFadeSpeed { get; }
    int OutputBrightnessByte { get; }
    int SpotlightDeviceId { get; }

    int FireGeneration(LayerInstanceId instanceId);
    int ClearGeneration(LayerInstanceId instanceId);
  }
}
