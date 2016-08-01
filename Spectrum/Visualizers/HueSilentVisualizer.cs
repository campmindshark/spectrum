using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.Hues;
using Spectrum.Audio;
using System.Diagnostics;

namespace Spectrum {

  class HueSilentVisualizer : Visualizer {

    private Configuration config;
    private AudioInput audio;
    private HueOutput hue;

    private bool silentMode = false;
    private int silentCounter = 40;
    private Stopwatch stopwatch;

    private int hueIndex = 0;
    private int satIndex = 254;
    private int lightIndex = 0;

    public HueSilentVisualizer(
      Configuration config,
      AudioInput audio,
      HueOutput hue
    ) {
      this.config = config;
      this.hue = hue;
      this.audio = audio;
      this.hue.RegisterVisualizer(this);
      this.stopwatch = new Stopwatch();
      this.stopwatch.Start();
    }

    public int Priority {
      get {
        if (silentMode) {
          return 2;
        }
        return 1;
      }
    }

    // We don't actually care about this
    public bool Enabled { get; set; } = false;

    public Input[] GetInputs() {
      return new Input[] { this.audio };
    }

    public void Visualize() {
      if (this.audio.Volume >= 0.01) {
        this.silentCounter = 40;
        this.silentMode = false;
        return;
      }

      if (this.silentCounter > 0) {
        this.silentCounter--;
        return;
      }

      this.silentMode = true;

      if (this.hue.BufferSize != 0) {
        return;
      }

      if (this.stopwatch.ElapsedMilliseconds < 2000) {
        return;
      }
      this.stopwatch.Restart();

      this.hueIndex = (this.hueIndex + 10000) % 65535;
      this.lightIndex = (this.lightIndex + 1) % 5;
      this.hue.SendLightCommand(
        this.lightIndex,
        new HueCommand() {
          on = true,
          bri = 1,
          hue = this.hueIndex + 1,
          sat = Math.Min(this.satIndex, 254),
          transitiontime = 12,
          effect = "none",
        }
      );
      if (this.satIndex < 127) {
        this.satIndex++;
      }
      if (this.satIndex > 380) {
        this.satIndex--;
      }
    }

  }

}