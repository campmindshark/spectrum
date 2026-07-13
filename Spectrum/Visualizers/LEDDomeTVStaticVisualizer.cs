using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Spectrum {

  class LEDDomeTVStaticVisualizer : DomeLayerVisualizer {

    private readonly DomeLayerEnvironment environment;
    private readonly LEDDomeOutput dome;
    private readonly DomeFrame buffer;

    private Random random = new Random();

    public LEDDomeTVStaticVisualizer(
      DomeLayerEnvironment environment,
      LEDDomeOutput dome
    ) {
      this.environment = environment;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeFrame();
    }

    public int Priority => 2;

    public string LayerKey => "tv-static";
    public DomeFrame LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    public Input[] GetInputs() {
      return System.Array.Empty<Input>();
    }

    public void Static() {
      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        this.buffer.pixels[i].color = this.RandomColor();
      }
    }

    public void Visualize() {
      this.Static();
    }

    private int RandomColor() {
      int brightnessByte = this.environment.OutputBrightnessByte;
      int color = 0;
      for (int i = 0; i < 3; i++) {
        color |= (int)(random.NextDouble() * brightnessByte) << (i * 8);
      }
      return color;
    }
  }

}
