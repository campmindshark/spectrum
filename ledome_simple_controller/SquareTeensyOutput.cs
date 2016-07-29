namespace Spectrum.LEDs {

  /**
   * This is a simple wrapper around SimpleTeensyOutput that exposes a
   * Cartestian coordinate system for a LED grid that is arrayed in a particular
   * back-and-forth grid structure (see the drawing in the constructor).
   */
  public class SquareTeensyOutput {

    private SimpleTeensyOutput output;
    private int rowLength;
    private int rowsPerStrip;

    /**
     * Here's a drawing of a square grid of LEDs:
     *   >>>>>
     *   <<<<<
     *   >>>>>
     *   >>>>>
     *   <<<<<
     *   >>>>>
     *
     * In this set up, we have two total strips composed of three rows each.
     * The rowLength is 5 and rowsPerStrip is 3.
     */
    public SquareTeensyOutput(string portName, int rowLength, int rowsPerStrip) {
      this.output = new SimpleTeensyOutput(portName);
      this.rowLength = rowLength;
      this.rowsPerStrip = rowsPerStrip;
    }

    public bool Enabled {
      get { return this.output.Enabled; }
      set { this.output.Enabled = value; }
    }

    public void Flush() {
      this.output.Flush();
    }

    public void SetPixel(int x, int y, int color) {
      // We need to figure out if this row is connected
      // in the forward or negative direction
      bool reverse = (y % rowsPerStrip) % 2 == 1;
      int pixelIndex = y * this.rowLength;
      if (reverse) {
        pixelIndex += this.rowLength - x - 1;
      } else {
        pixelIndex += x;
      }
      this.output.SetPixel(pixelIndex, color);
    }

  }

}
