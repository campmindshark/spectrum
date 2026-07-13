using System;

namespace Spectrum.LEDs {

  // Immutable physical-address projection. Logical frames never contain OPC
  // addresses; cable calibration swaps this object atomically.
  public sealed class DomeOutputMapping {
    private readonly int[] controlBoxes;
    private readonly int[] pixelsWithinBox;

    public int Count => this.controlBoxes.Length;

    public DomeOutputMapping(int[] controlBoxes, int[] pixelsWithinBox) {
      if (controlBoxes == null || pixelsWithinBox == null ||
          controlBoxes.Length != pixelsWithinBox.Length) {
        throw new ArgumentException("Mapping arrays must have equal lengths.");
      }
      this.controlBoxes = (int[])controlBoxes.Clone();
      this.pixelsWithinBox = (int[])pixelsWithinBox.Clone();
    }

    public int ControlBoxAt(int logicalPixel) =>
      this.controlBoxes[logicalPixel];
    public int PixelWithinBoxAt(int logicalPixel) =>
      this.pixelsWithinBox[logicalPixel];
  }
}
