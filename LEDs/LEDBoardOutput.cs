using Spectrum.Base;
using System.Collections.Generic;
using System.ComponentModel;

namespace Spectrum.LEDs {

  /**
   * This is a simple wrapper around OPCAPI that exposes a Cartesian coordinate
   * system for a LED grid that is arrayed in a particular back-and-forth grid
   * structure. Here's a drawing:
   *   >>>>>
   *   <<<<<
   *   >>>>>
   *   >>>>>
   *   <<<<<
   *   >>>>>
   * In this set up, we have two total strips composed of three rows each.
   * The rowLength is 5 and rowsPerStrip is 3 (configuration params).
   */
  public class LEDBoardOutput : Output {

    private OPCAPI opcAPI;
    private readonly Configuration config;
    private readonly List<Visualizer> visualizers;

    public LEDBoardOutput(Configuration config) {
      this.config = config;
      this.visualizers = new List<Visualizer>();
      this.config.PropertyChanged += this.ConfigUpdated;
    }

    private void ConfigUpdated(object sender, PropertyChangedEventArgs e) {
      if (!this.active || !this.config.ledBoardEnabled) {
        return;
      }
      if (
        e.PropertyName != "boardBeagleboneOPCAddress" &&
        e.PropertyName != "ledBoardOutputInSeparateThread"
      ) {
        return;
      }
      if (this.opcAPI != null) {
        this.opcAPI.Active = false;
      }
      this.initializeOPCAPI();
    }

    private void initializeOPCAPI() {
      var opcAddress = this.config.boardBeagleboneOPCAddress;
      string[] parts = opcAddress.Split(':');
      if (parts.Length < 3) {
        opcAddress += ":0"; // default to channel 0
      }
      this.opcAPI = new OPCAPI(
        opcAddress,
        this.config.ledBoardOutputInSeparateThread,
        newFPS => this.config.boardBeagleboneOPCFPS = newFPS
      );
      this.opcAPI.Active = this.active;
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
        this.active = value;
        if (value) {
          this.initializeOPCAPI();
        } else if (this.opcAPI != null) {
          this.opcAPI.Active = false;
        }
      }
    }

    public bool Enabled {
      get {
        return this.config.ledBoardEnabled;
      }
    }

    public void OperatorUpdate() {
      if (this.opcAPI != null) {
        this.opcAPI.OperatorUpdate();
      }
    }

    public void RegisterVisualizer(Visualizer visualizer) {
      this.visualizers.Add(visualizer);
    }

    public Visualizer[] GetVisualizers() {
      return this.visualizers.ToArray();
    }

    public void Flush() {
      if (this.opcAPI != null) {
        this.opcAPI.Flush();
      }
    }

    public void SetPixel(int x, int y, int color) {
      int pixelIndex = y * this.config.boardRowLength;
      // We need to figure out if this row is connected
      // in the forward or negative direction
      bool reverse = (y % this.config.boardRowsPerStrip) % 2 == 1;
      if (reverse) {
        pixelIndex += this.config.boardRowLength - x - 1;
      } else {
        pixelIndex += x;
      }
      if (this.opcAPI != null) {
        this.opcAPI.SetPixel(pixelIndex, color);
      }
    }

  }

}
