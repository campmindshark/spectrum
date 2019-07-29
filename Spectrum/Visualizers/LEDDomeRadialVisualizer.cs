using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Spectrum {

  class LEDDomeRadialVisualizer : Visualizer {

    private Configuration config;
    private AudioInput audio;
    private LEDDomeOutput dome;

    public LEDDomeRadialVisualizer(
      Configuration config,
      AudioInput audio,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.audio = audio;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
    }

    public int Priority {
      get {
        return this.config.domeActiveVis == 1 ? 2 : 0;
      }
    }

    public bool Enabled { get; set; }

    public Input[] GetInputs() {
      return new Input[] { this.audio };
    }

    public void ParametricTest() {

      double progress = this.config.beatBroadcaster.ProgressThroughBeat(
        this.config.domeVolumeRotationSpeed
      );

      double level = this.audio.LevelForChannel(0);

      for (int i = 0; i < LEDDomeOutput.GetNumStruts(); i++) {
        Strut strut = Strut.FromIndex(this.config, i);
        var leds = LEDDomeOutput.GetNumLEDs(i);
        for (int j = 0; j < leds; j++) {
          var p = StrutLayoutFactory.GetProjectedLEDPointParametric(i, j);
          double r = 0;
          double g = 0;
          double b = 0;

          //radar effect
          double a = (p.Item3 + Math.PI) / (Math.PI * 2);
          r = progress - a;
          if (r < 0) r += 1;
          if (r > 1) r = 1;
          /*
          //pulse effect
          double dist = Math.Abs(progress - p.Item4);
          r = 1 - dist;
          if (r < 0.9) r = 0;

          //spiral effect
          double m = p.Item4 - a;
          if (m < 0) m += 1;
          double n = progress - m;
          if (n < 0) n += 1;
          r = 1 - n;
          */
          this.dome.SetPixel(i, j, LEDColor.FromDoubles(r,g,b));
        }
      }
    }

    public void Render() {

      double progress = this.config.beatBroadcaster.ProgressThroughBeat(
        this.config.domeVolumeRotationSpeed
      );

      double level = this.audio.LevelForChannel(0);

      for (int i = 0; i < LEDDomeOutput.GetNumStruts(); i++) {
        Strut strut = Strut.FromIndex(this.config, i);
        var leds = LEDDomeOutput.GetNumLEDs(i);
        for (int j = 0; j < leds; j++) {
          var p = StrutLayoutFactory.GetProjectedLEDPointParametric(i, j);

          //map angle to 0-1
          var angle = MapWrap(p.Item3, -Math.PI, Math.PI, 0.0, 1.0);
          var val = MapWrap(angle, progress, 1 + progress, 0, 1);

          this.dome.SetPixel(i, j, LEDColor.FromDoubles(val, 0, 0));
        }
      }
    }

    // Map value x from range a-b to range c-d
    private static double Map(double x, double a, double b, double c, double d) {
      return (x - a) * (d - c) / (b - a) + c;
    }

    // Map value x from range a-b to range c-d, clamp values outside of range c-d to c or d
    private static double MapClamp(double x, double a, double b, double c, double d) {
      double y = (x - a) * (d - c) / (b - a) + c;
      if (y < c) return c;
      if (y > d) return d;
      return y;
    }

    // Map value x from range a-b to range c-d, wrap values outside or range c-d
    // Example: if we map to range 0-10, but get result 11.3, this is wrapped to 1.3
    private static double MapWrap(double x, double a, double b, double c, double d) {
      double mapped = (x - a) * (d - c) / (b - a); //map to final range but don't add the lower value c
      double wrapped = mapped % (d - c); // modulus across final range
      return wrapped + c; // add c and return
    }

    public void Visualize() {
      this.Render();

      this.dome.Flush();
    }

  }

}
