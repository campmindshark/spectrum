using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.Hues;

namespace Spectrum {

  class HueSolidColorVisualizer : Visualizer {

    private Configuration config;
    private HueOutput hue;
    private bool shouldRun = false;

    // We cache previous values so we can detect when there was a change
    private bool lastControlLights;
    private bool lastLightsOff;
    private bool lastRedAlert;
    private int lastBrighten;
    private int lastSat;
    private int lastColorslide;

    public HueSolidColorVisualizer(
      Configuration config,
      HueOutput hue
    ) {
      this.config = config;
      this.hue = hue;
      this.hue.RegisterVisualizer(this);
    }

    public int Priority {
      get {
        if (
          !this.config.controlLights ||
          this.config.lightsOff ||
          this.config.redAlert
        ) {
          return 3;
        }
        return 0;
      }
    }

    private bool enabled = false;
    public bool Enabled {
      get {
        return this.enabled;
      }
      set {
        if (value == this.enabled) {
          return;
        }
        if (value) {
          this.shouldRun = true;
        }
        this.enabled = value;
      }
    }

    public Input[] GetInputs() {
      return new Input[] { };
    }

    public void Visualize() {
      bool shouldUpdate = false;
      if (
        this.lastControlLights != this.config.controlLights ||
        this.lastLightsOff != this.config.lightsOff ||
        this.lastRedAlert != this.config.redAlert ||
        this.lastBrighten != this.config.brighten ||
        this.lastSat != this.config.sat ||
        this.lastColorslide != this.config.colorslide
      ) {
        this.lastControlLights = this.config.controlLights;
        this.lastLightsOff = this.config.lightsOff;
        this.lastRedAlert = this.config.redAlert;
        this.lastBrighten = this.config.brighten;
        this.lastSat = this.config.sat;
        this.lastColorslide = this.config.colorslide;
        shouldUpdate = true;
      }

      if (!this.shouldRun && !shouldUpdate) {
        return;
      }

      HueCommand command;
      if (this.config.lightsOff) {
        command = new HueCommand() {
          on = false,
        };
      } else if (this.config.redAlert) {
        command = new HueCommand() {
          on = true,
          bri = 1,
          hue = 1,
          sat = 254,
          effect = "none",
        };
      } else {
        int newbri = Math.Min(Math.Max(254 + 64 * this.config.brighten, 1), 254);
        int newsat = Math.Min(Math.Max(126 + 63 * this.config.sat, 0), 254);
        int newhue = Math.Min(Math.Max(16384 + this.config.colorslide * 4096, 0), 65535);
        command = new HueCommand() {
          on = true,
          bri = newbri,
          hue = newhue,
          sat = newsat,
          effect = "none",
        };
      }

      // We need to spam a bunch of these commands because the Hue hub sucks and
      // executes commands we give it out-of-order
      int timesToRun = this.shouldRun ? 15 : 1;
      for (int i = 0; i < timesToRun; i++) {
        for (int j = 0; j < this.config.hueIndices.Length; j++) {
          this.hue.SendLightCommand(j, command);
        }
      }

      this.shouldRun = false;
    }

  }

}
