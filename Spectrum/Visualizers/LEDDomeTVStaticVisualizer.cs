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

    private Random random = new Random();

    public LEDDomeTVStaticVisualizer(
      Configuration config,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
    }

    public int Priority {
      get {
        return 1;
      }
    }

    public bool Enabled { get; set; }

    public Input[] GetInputs() {
      return new Input[] { };
    }

    public void Static() {
      for (int i = 0; i < LEDDomeOutput.GetNumStruts(); i++) {
        Strut strut = Strut.FromIndex(this.config, i);
        for (int j = 0; j < strut.Length; j++) {
          this.dome.SetPixel(i, j, this.RandomColor());
        }
      }
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
