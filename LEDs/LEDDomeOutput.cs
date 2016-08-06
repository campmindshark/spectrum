using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;
using System.ComponentModel;

namespace Spectrum.LEDs {

  public class LEDDomeOutput : Output {

    private SimpleTeensyAPI[] teensies;
    private Configuration config;
    private List<Visualizer> visualizers;

    public LEDDomeOutput(Configuration config) {
      this.config = config;
      this.visualizers = new List<Visualizer>();
      this.config.PropertyChanged += ConfigUpdated;
    }

    public void RegisterVisualizer(Visualizer visualizer) {
      this.visualizers.Add(visualizer);
    }

    public Visualizer[] GetVisualizers() {
      return this.visualizers.ToArray();
    }

    private void ConfigUpdated(object sender, PropertyChangedEventArgs e) {
      if (e.PropertyName != "domeEnabled") {
        return;
      }
      bool domeEnabled = this.config.domeEnabled;
      lock (this.visualizers) {
        if (domeEnabled) {
          this.teensies = this.getNewTeensies();
        }
        if (this.Active) {
          foreach (var teensy in this.teensies) {
            teensy.Active = domeEnabled;
          }
        }
        if (!domeEnabled) {
          this.teensies = null;
        }
      }
    }

    private SimpleTeensyAPI[] getNewTeensies() {
      return new SimpleTeensyAPI[] {
        new SimpleTeensyAPI(
          this.config.domeTeensyUSBPort1,
          this.config.domeOutputInSeparateThread
        ),
        new SimpleTeensyAPI(
          this.config.domeTeensyUSBPort2,
          this.config.domeOutputInSeparateThread
        ),
        new SimpleTeensyAPI(
          this.config.domeTeensyUSBPort3,
          this.config.domeOutputInSeparateThread
        ),
        new SimpleTeensyAPI(
          this.config.domeTeensyUSBPort4,
          this.config.domeOutputInSeparateThread
        ),
        new SimpleTeensyAPI(
          this.config.domeTeensyUSBPort5,
          this.config.domeOutputInSeparateThread
        ),
      };
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
        lock (this.visualizers) {
          if (this.teensies != null) {
            foreach (var teensy in this.teensies) {
              teensy.Active = value;
            }
          }
          this.active = value;
        }
      }
    }

    public bool Enabled {
      get {
        return this.config.domeEnabled || this.config.domeSimulationEnabled;
      }
    }

    public void OperatorUpdate() {
      lock (this.visualizers) {
        if (this.teensies == null) {
          return;
        }
        foreach (var teensy in this.teensies) {
          teensy.OperatorUpdate();
        }
      }
    }

    public void Flush() {
      lock (this.visualizers) {
        if (this.teensies != null) {
          foreach (var teensy in this.teensies) {
            teensy.Flush();
          }
        }
      }
      this.config.domeCommandQueue.Enqueue(new LEDCommand() { isFlush = true });
    }

    private void SetTeensyPixel(int teensyIndex, int pixelIndex, int color) {
      lock (this.visualizers) {
        if (this.teensies != null) {
          this.teensies[teensyIndex].SetPixel(pixelIndex, color);
        }
      }
    }

    public void SetPixel(int strutIndex, int ledIndex, int color) {
      // TODO: logic to convert from (strutIndex, ledIndex) to
      // (teensyIndex, pixelIndex) and then call SetTeensyPixel
      this.config.domeCommandQueue.Enqueue(new LEDCommand() {
        strutIndex = strutIndex,
        ledIndex = ledIndex,
        color = color,
      });
    }

  }

}