using Spectrum.Base;
using System.Collections.Generic;

namespace Spectrum.LEDs {

  /**
   * This is a simple wrapper around SimpleTeensyOutput that exposes a
   * Cartestian coordinate system for a LED grid that is arrayed in a particular
   * back-and-forth grid structure. Here's a drawing:
   *   >>>>>
   *   <<<<<
   *   >>>>>
   *   >>>>>
   *   <<<<<
   *   >>>>>
   * In this set up, we have two total strips composed of three rows each.
   * The rowLength is 5 and rowsPerStrip is 3 (configuration params).
   */
  public class CartesianTeensyOutput : Output {

    private SimpleTeensyOutput output;
    private Configuration config;
    private List<Visualizer> visualizers;

    public CartesianTeensyOutput(Configuration config) {
      this.config = config;
      this.output = new SimpleTeensyOutput(
        this.config.teensyUSBPort,
        this.config.ledsOutputInSeparateThread
      );
      this.visualizers = new List<Visualizer>();
    }

    public bool Enabled {
      get { return this.output.Enabled; }
      set { this.output.Enabled = value; }
    }

    public void OperatorUpdate() {
      this.output.OperatorUpdate();
    }

    public void RegisterVisualizer(Visualizer visualizer) {
      this.visualizers.Add(visualizer);
    }

    public Visualizer[] GetVisualizers() {
      return this.visualizers.ToArray();
    }

    public void Flush() {
      this.output.Flush();
    }

    public void SetPixel(int x, int y, int color) {
      // We need to figure out if this row is connected
      // in the forward or negative direction
      bool reverse = (y % this.config.teensyRowsPerStrip) % 2 == 1;
      int pixelIndex = y * this.config.teensyRowLength;
      if (reverse) {
        pixelIndex += this.config.teensyRowLength - x - 1;
      } else {
        pixelIndex += x;
      }
      this.output.SetPixel(pixelIndex, color);
    }

  }

}
