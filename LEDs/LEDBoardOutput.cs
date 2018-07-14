using Spectrum.Base;
using System.Collections.Generic;
using System.ComponentModel;

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
  public class LEDBoardOutput : Output {

    private TeensyAPI teensyAPI;
    private OPCAPI opcAPI;
    private Configuration config;
    private List<Visualizer> visualizers;

    public LEDBoardOutput(Configuration config) {
      this.config = config;
      this.visualizers = new List<Visualizer>();
      this.config.PropertyChanged += this.ConfigUpdated;
    }

    private void ConfigUpdated(object sender, PropertyChangedEventArgs e) {
      if (!this.active) {
        return;
      }
      if (e.PropertyName == "boardHardwareSetup") {
        if (this.config.boardHardwareSetup == 0) {
          if (this.opcAPI != null) {
            this.opcAPI.Active = false;
          }
          this.initializeTeensyAPI();
        } else if (this.config.boardHardwareSetup == 1) {
          if (this.teensyAPI != null) {
            this.teensyAPI.Active = false;
          }
          this.initializeOPCAPI();
        }
      } else if (e.PropertyName == "boardTeensyUSBPort") {
        if (this.config.boardHardwareSetup == 0) {
          this.teensyAPI.Active = false;
          this.initializeTeensyAPI();
        }
      } else if (e.PropertyName == "boardBeagleboneOPCAddress") {
        if (this.config.boardHardwareSetup == 1) {
          this.opcAPI.Active = false;
          this.initializeOPCAPI();
        }
      } else if (e.PropertyName == "ledBoardOutputInSeparateThread") {
        if (this.config.boardHardwareSetup == 0) {
          if (this.teensyAPI != null) {
            this.teensyAPI.Active = false;
          }
          this.initializeTeensyAPI();
        } else if (this.config.boardHardwareSetup == 1) {
          if (this.opcAPI != null) {
            this.opcAPI.Active = false;
          }
          this.initializeOPCAPI();
        }
      }
    }

    private void initializeTeensyAPI() {
      this.teensyAPI = new TeensyAPI(
        this.config.boardTeensyUSBPort,
        this.config.ledBoardOutputInSeparateThread,
        newFPS => this.config.boardTeensyFPS = newFPS
      );
      this.teensyAPI.Active = this.active &&
        this.config.boardHardwareSetup == 0;
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
      this.opcAPI.Active = this.active &&
        this.config.boardHardwareSetup == 1;
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
          if (this.config.boardHardwareSetup == 0) {
            this.initializeTeensyAPI();
          } else if (this.config.boardHardwareSetup == 1) {
            this.initializeOPCAPI();
          }
        } else {
          if (this.teensyAPI != null) {
            this.teensyAPI.Active = false;
          }
          if (this.opcAPI != null) {
            this.opcAPI.Active = false;
          }
        }
      }
    }

    public bool Enabled {
      get {
        return this.config.ledBoardEnabled;
      }
    }

    public void OperatorUpdate() {
      if (this.config.boardHardwareSetup == 0 && this.teensyAPI != null) {
        this.teensyAPI.OperatorUpdate();
      }
      if (this.config.boardHardwareSetup == 1 && this.opcAPI != null) {
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
      if (this.config.boardHardwareSetup == 0 && this.teensyAPI != null) {
        this.teensyAPI.Flush();
      }
      if (this.config.boardHardwareSetup == 1 && this.opcAPI != null) {
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
      if (this.config.boardHardwareSetup == 0 && this.teensyAPI != null) {
        this.teensyAPI.SetPixel(pixelIndex, color);
      }
      if (this.config.boardHardwareSetup == 1 && this.opcAPI != null) {
        this.opcAPI.SetPixel(pixelIndex, color);
      }
    }

  }

}
