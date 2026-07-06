using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Spectrum {

  class LEDDomeTVStaticVisualizer : Visualizer {

    private readonly Configuration config;
    private readonly LEDDomeOutput dome;
    private readonly LEDDomeOutputBuffer buffer;

    private Random random = new Random();

    public LEDDomeTVStaticVisualizer(
      Configuration config,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeOutputBuffer();
    }

    public int Priority {
      get {
        return this.config.domeActiveVis == 8 ? 2 : 0;
      }
    }

    public bool Enabled { get; set; }

    public Input[] GetInputs() {
      return System.Array.Empty<Input>();
    }

    public void Static() {
      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        this.buffer.pixels[i].color = this.RandomColor();
      }
      this.dome.WriteBuffer(this.buffer);
    }

    public void Visualize() {
      this.Static();
      this.dome.Flush();
    }

    private int RandomColor() {
      int brightnessByte = (int)(
        0xFF * this.config.domeMaxBrightness *
        this.config.domeBrightness
      );
      int color = 0;
      for (int i = 0; i < 3; i++) {
        color |= (int)(random.NextDouble() * brightnessByte) << (i * 8);
      }
      return color;
    }
  }

}
