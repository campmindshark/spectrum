using Spectrum.Base;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace Spectrum.LEDs {

  public class LEDBarOutput : Output {

    private TeensyAPI teensyAPI;
    private OPCAPI opcAPI;
    private Configuration config;
    private LEDDomeOutput dome;
    private List<Visualizer> visualizers;

    public LEDBarOutput(Configuration config) {
      this.config = config;
      this.visualizers = new List<Visualizer>();
      this.config.PropertyChanged += this.ConfigUpdated;
    }

    public LEDBarOutput(Configuration config, LEDDomeOutput dome) {
      this.config = config;
      this.dome = dome;
      this.visualizers = new List<Visualizer>();
      this.config.PropertyChanged += this.ConfigUpdated;
    }

    private void ConfigUpdated(object sender, PropertyChangedEventArgs e) {
      if (!this.active || this.dome != null) {
        return;
      }
      if (e.PropertyName == "barHardwareSetup") {
        if (this.config.barHardwareSetup == 0) {
          if (this.opcAPI != null) {
            this.opcAPI.Active = false;
          }
          this.initializeTeensyAPI();
        } else if (this.config.barHardwareSetup == 1) {
          if (this.teensyAPI != null) {
            this.teensyAPI.Active = false;
          }
          this.initializeOPCAPI();
        }
      } else if (e.PropertyName == "barTeensyUSBPort") {
        if (this.config.barHardwareSetup == 0) {
          this.teensyAPI.Active = false;
          this.initializeTeensyAPI();
        }
      } else if (e.PropertyName == "barBeagleboneOPCAddress") {
        if (this.config.barHardwareSetup == 1) {
          this.opcAPI.Active = false;
          this.initializeOPCAPI();
        }
      } else if (e.PropertyName == "barOutputInSeparateThread") {
        if (this.config.barHardwareSetup == 0) {
          if (this.teensyAPI != null) {
            this.teensyAPI.Active = false;
          }
          this.initializeTeensyAPI();
        } else if (this.config.barHardwareSetup == 1) {
          if (this.opcAPI != null) {
            this.opcAPI.Active = false;
          }
          this.initializeOPCAPI();
        }
      }
    }

    private void initializeTeensyAPI() {
      if (this.dome != null) {
        return;
      }
      this.teensyAPI = new TeensyAPI(
        this.config.barTeensyUSBPort,
        this.config.barOutputInSeparateThread,
        newFPS => this.config.barTeensyFPS = newFPS
      );
      this.teensyAPI.Active = this.active &&
        this.config.barHardwareSetup == 0;
    }

    private void initializeOPCAPI() {
      if (this.dome != null) {
        return;
      }
      var opcAddress = this.config.barBeagleboneOPCAddress;
      string[] parts = opcAddress.Split(':');
      if (parts.Length < 3) {
        opcAddress += ":0"; // default to channel 0
      }
      this.opcAPI = new OPCAPI(
        opcAddress,
        this.config.barOutputInSeparateThread,
        newFPS => this.config.barBeagleboneOPCFPS = newFPS
      );
      this.opcAPI.Active = this.active &&
        this.config.barHardwareSetup == 1;
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
        if (this.dome != null) {
          this.dome.Active = value;
        }
        if (value) {
          if (this.config.barHardwareSetup == 0) {
            this.initializeTeensyAPI();
          } else if (this.config.barHardwareSetup == 1) {
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
        return this.config.barEnabled || this.config.barSimulationEnabled;
      }
    }

    public void OperatorUpdate() {
      if (this.config.barHardwareSetup == 0 && this.teensyAPI != null) {
        this.teensyAPI.OperatorUpdate();
      }
      if (this.config.barHardwareSetup == 1 && this.opcAPI != null) {
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
      if (this.dome != null) {
        this.dome.Flush();
      }
      if (this.config.barHardwareSetup == 0 && this.teensyAPI != null) {
        this.teensyAPI.Flush();
      }
      if (this.config.barHardwareSetup == 1 && this.opcAPI != null) {
        this.opcAPI.Flush();
      }
      if (this.config.barSimulationEnabled) {
        this.config.barCommandQueue.Enqueue(
          new BarLEDCommand() { isFlush = true }
        );
      }
    }

    /**
     * isRunner: is this a pixel on the runner strip lighting the front of the
     *   bar, or a pixel on the infinity strips on the bar surface
     * pixelIndex: for the runner strip, pixelIndex goes 0-n from left to right
     *   if you're facing the bar. for the infinity strips, it starts from the
     *   top left (facing the bar) and goes clockwise
     * color: duh the color???
     */
    public void SetPixel(bool isRunner, int ledIndex, int color) {
      var infinityStripLength = this.config.barInfinityLength +
        this.config.barInfinityWidth;
      var totalInfinityLength = infinityStripLength * 2;
      var pixelIndex = ledIndex;
      if (isRunner) {
        Debug.Assert(
          pixelIndex < this.config.barRunnerLength,
          "pixelIndex too large"
        );
        pixelIndex += totalInfinityLength;
      } else {
        Debug.Assert(
          pixelIndex < totalInfinityLength,
          "pixelIndex too large"
        );
        if (ledIndex >= infinityStripLength) {
          // The second infinity strip is reversed
          pixelIndex = totalInfinityLength - ledIndex + infinityStripLength - 1;
        }
      }
      if (this.dome != null) {
        this.dome.SetBarPixel(pixelIndex, color);
      }
      if (this.config.barHardwareSetup == 0 && this.teensyAPI != null) {
        this.teensyAPI.SetPixel(pixelIndex, color);
      }
      if (this.config.barHardwareSetup == 1 && this.opcAPI != null) {
        this.opcAPI.SetPixel(pixelIndex, color);
      }
      if (this.config.barSimulationEnabled) {
        this.config.barCommandQueue.Enqueue(new BarLEDCommand() {
          isRunner = isRunner,
          ledIndex = ledIndex,
          color = color,
        });
      }
    }

  }

}