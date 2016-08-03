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

    private SimpleTeensyAPI api;
    private Configuration config;
    private List<Visualizer> visualizers;

    public CartesianTeensyOutput(Configuration config) {
      this.config = config;
      this.visualizers = new List<Visualizer>();
    }

    private bool active = false;
    public bool Active {
      get {
        return this.active;
      }
      set {
        if (value == this.active) {
          return;
        }
        if (value) {
          this.api = new SimpleTeensyAPI(
            this.config.teensyUSBPort,
            this.config.ledBoardOutputInSeparateThread
          );
          this.api.Active = true;
        } else {
          this.api.Active = false;
        }
        this.active = value;
      }
    }

    public bool Enabled {
      get {
        return this.config.ledBoardEnabled;
      }
   }

    public void OperatorUpdate() {
      this.api.OperatorUpdate();
    }

    public void RegisterVisualizer(Visualizer visualizer) {
      this.visualizers.Add(visualizer);
    }

    public Visualizer[] GetVisualizers() {
      return this.visualizers.ToArray();
    }

    public void Flush() {
      this.api.Flush();
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
      this.api.SetPixel(pixelIndex, color);
    }

  }

}
