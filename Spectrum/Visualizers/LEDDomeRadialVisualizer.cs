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

    private double currentAngle;
    private double currentGradient;
    private double lastProgress;

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

      double progress = this.config.beatBroadcaster.ProgressThroughBeat(1.0);
      currentAngle += this.config.domeVolumeRotationSpeed * Wrap(progress - this.lastProgress, 0, 1);
      currentAngle = Wrap(currentAngle, 0, 1);
      currentGradient += this.config.domeGradientSpeed * Wrap(progress - this.lastProgress, 0, 1);
      currentGradient = Wrap(currentGradient, 0, 1);
      this.lastProgress = progress;

      double level = this.audio.LevelForChannel(0);
      double adjustedLevel = Map(level, 0, 1, 0.02, 1);

      for (int i = 0; i < LEDDomeOutput.GetNumStruts(); i++) {
        Strut strut = Strut.FromIndex(this.config, i);
        var leds = LEDDomeOutput.GetNumLEDs(i);
        for (int j = 0; j < leds; j++) {
          var p = StrutLayoutFactory.GetProjectedLEDPointParametric(i, j);

          // map angle to 0-1
          var angle = MapWrap(p.Item3, -Math.PI, Math.PI, 0.0, 1.0);
          var dist = p.Item4;

          double val = 0;
          double gradientVal = 0;

          switch (this.config.domeRadialEffect) {
            case 0:
              // radar mapping
              val = MapWrap(angle, currentAngle, 1 + currentAngle, 0, 1);
              gradientVal = dist;
              break;
            case 1:
              // pulse mapping
              val = MapWrap(dist, currentAngle, 1 + currentAngle, 0, 1);
              gradientVal = Math.Abs(Map(angle, 0, 1, -1, 1));
              break;
            case 2:
              // spiral mapping
              val = MapWrap(angle + dist / this.config.domeRadialFrequency, currentAngle, 1 + currentAngle, 0, 1);
              gradientVal = dist;
              break;
          }

          // scale val according to radial frequency
          val = Wrap(val * this.config.domeRadialFrequency, 0, 1);
          // center around val = 1/0 (0.5 maps to 0, 0 and 1 map to 1)
          var centeredVal = Math.Abs(Map(val, 0, 1, -1, 1));

          // size limit is scaled according the size slider and the current level
          var sizeLimit = this.config.domeRadialSize * adjustedLevel;
          bool on = centeredVal <= sizeLimit;

          var color = on ? this.dome.GetGradientColor(0, gradientVal, currentGradient, true) : 0;

          this.dome.SetPixel(i, j, color);
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
      return Wrap(Map(x, a, b, c, d), c, d);
      double mapped = (x - a) * (d - c) / (b - a); //map to final range but don't add the lower value c
      double wrapped = mapped % (d - c); // modulus across final range
      return wrapped + c; // add c and return
    }

    private static double Clamp(double x, double a, double b) {
      if (x < a) return a;
      if (x > b) return b;
      return x;
    }

    private static double Wrap(double x, double a, double b) {
      var range = b - a;
      while (x < a) x += range;
      while (x > b) x -= range;
      return x;
    }

    public void Visualize() {
      this.Render();

      this.dome.Flush();
    }

  }

}
