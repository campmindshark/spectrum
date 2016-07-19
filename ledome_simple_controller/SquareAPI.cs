namespace LEDome {

  public class SquareAPI {

    private SimpleAPI api;
    private int rowLength;
    private int rowsPerStrip;

    ///
    /// >>>>>
    /// <<<<<
    /// >>>>>
    /// >>>>>
    /// <<<<<
    /// >>>>>
    /// 
    /// In this set up, we have two total strips composed of three rows each.
    /// The rowLength is 5 and rowsPerStrip is 3.
    ///
    public SquareAPI(string portName, int rowLength, int rowsPerStrip) {
      this.api = new SimpleAPI(portName);
      this.rowLength = rowLength;
      this.rowsPerStrip = rowsPerStrip;
    }

    public void Open() {
      this.api.Open();
    }

    public void Close() {
      this.api.Close();
    }

    public void Flush() {
      this.api.Flush();
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
      this.api.SetPixel(pixelIndex, color);
    }

  }

}
