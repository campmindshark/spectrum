namespace Spectrum {

  internal static class WandTypeNames {
    public static string Of(int deviceType) {
      switch (deviceType) {
        case 1: return "Wand";
        case 2: return "Poi";
        case 3: return "Wand v2";
        case 4: return "Wristband";
        case 6: return "Wand v3";
        default: return "Unknown (" + deviceType + ")";
      }
    }
  }
}
