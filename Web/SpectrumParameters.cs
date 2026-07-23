using Spectrum.Base;

namespace Spectrum.Web {

  /**
   * Compatibility facade for callers that consume the web parameter registry.
   * The concrete catalog now lives in SpectrumConfigurationSchema alongside
   * defaults, persistence participation, and restart policy.
   */
  public static class SpectrumParameters {

    public static string NormalizeOpcAddress(string raw) =>
      SpectrumConfigurationSchema.NormalizeOpcAddress(raw);

    public static ParameterRegistry BuildRegistry(
      bool nativeWindowControlsAvailable = true
    ) => SpectrumConfigurationSchema.BuildParameterRegistry(
      nativeWindowControlsAvailable);
  }
}
