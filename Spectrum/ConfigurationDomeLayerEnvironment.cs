using Spectrum.Base;

namespace Spectrum {

  // Adapts persisted application configuration to the live values layer
  // renderers are allowed to observe. Dictionary publishers use copy-and-swap,
  // so each generation read sees either the old or the new command snapshot.
  internal sealed class ConfigurationDomeLayerEnvironment
      : DomeLayerEnvironment {
    private readonly Configuration config;

    public ConfigurationDomeLayerEnvironment(Configuration config) {
      this.config = config;
    }

    public double GlobalFadeSpeed => this.config.domeGlobalFadeSpeed;

    // Preserve the historical multiplication and truncation order used by the
    // two random-color renderers.
    public int OutputBrightnessByte => (int)(
      0xFF * this.config.domeMaxBrightness * this.config.domeBrightness);

    public int SpotlightDeviceId => this.config.orientationDeviceSpotlight;

    public int FireGeneration(LayerInstanceId instanceId) {
      int generation = 0;
      this.config.domeLayerFireCounters?.TryGetValue(
        instanceId.Value, out generation);
      return generation;
    }

    public int ClearGeneration(LayerInstanceId instanceId) {
      int generation = 0;
      this.config.domeLayerClearCounters?.TryGetValue(
        instanceId.Value, out generation);
      return generation;
    }
  }
}
