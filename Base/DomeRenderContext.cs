namespace Spectrum.Base {

  // Hardware-independent services available to a layer renderer. Lifecycle,
  // output transport, calibration commands, and simulator queues deliberately
  // stay on the host's concrete Output implementation.
  public interface DomeRenderContext {
    DomeFrame MakeDomeFrame();
    int StrutCount { get; }

    int GetSingleColor(int index, int paletteIndex = 0);
    int GetGradientColor(
      int index,
      double pixelPos,
      double focusPos,
      bool wrap,
      int paletteIndex = 0);
    int GetGradientBetweenColors(
      int minIndex,
      int maxIndex,
      double pixelPos,
      double focusPos,
      bool wrap,
      int paletteIndex = 0);
  }
}
