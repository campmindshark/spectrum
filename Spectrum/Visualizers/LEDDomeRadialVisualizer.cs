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
    private LEDDomeOutputBuffer buffer;

    private double currentAngle;
    private double currentGradient;
    private double currentCenterAngle;
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
      this.buffer = this.dome.MakeDomeOutputBuffer();
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

    void Render() {
      buffer.Fade(1 - Math.Pow(10, -this.config.domeGlobalFadeSpeed), 0);
      buffer.HueRotate(Math.Pow(10, -this.config.domeGlobalHueSpeed));
      double level = this.audio.Volume;
      // Sqrt makes values larger and gives more resolution for lower values
      double adjustedLevel = Clamp(Math.Sqrt(level), 0.1, 1);

      double progress = this.config.beatBroadcaster.ProgressThroughMeasure;
      // rotation is scaled by 1/4
      // otherwise it is way too fast and will make people vomit
      currentAngle += this.config.domeVolumeRotationSpeed *
        Wrap(progress - this.lastProgress, 0, 1) * 0.25;
      currentAngle = Wrap(currentAngle, 0, 1);
      currentGradient += this.config.domeGradientSpeed *
        Wrap(progress - this.lastProgress, 0, 1);
      currentGradient = Wrap(currentGradient, 0, 1);
      currentCenterAngle += this.config.domeRadialCenterSpeed *
        Wrap(progress - this.lastProgress, 0, 1) * 0.25;
      currentCenterAngle = Wrap(currentCenterAngle, 0, 1);
      this.lastProgress = progress;

      var centerOffset = StrutLayoutFactory.PolarToCartesian(
        config.domeRadialCenterAngle + currentCenterAngle * 2 * Math.PI,
        config.domeRadialCenterDistance
      );

      for (int i = 0; i < buffer.pixels.Length; i++) {
        var pixel = buffer.pixels[i];

        var p = StrutLayoutFactory.GetProjectedLEDPointParametric(
          pixel.strutIndex,
          pixel.strutLEDIndex,
          centerOffset.Item1,
          centerOffset.Item2
        );

        // map angle to 0-1
        var angle = MapWrap(p.Item3, -Math.PI, Math.PI, 0.0, 1.0);
        var dist = p.Item4;

        double val = 0;
        double gradientVal = 0;

        switch (this.config.domeRadialEffect) {
          case 0:
            // radar mapping
            val = MapWrap(angle, currentAngle, 1 + currentAngle, 0, 1);
            // scale val according to radial frequency
            val = Wrap(val * this.config.domeRadialFrequency, 0, 1);
            // center around val = 1/0 (0.5 maps to 0, 0 and 1 map to 1)
            val = Math.Abs(Map(val, 0, 1, -1, 1));

            gradientVal = dist;
            break;
          case 1:
            // pulse mapping
            val = MapWrap(dist, currentAngle, 1 + currentAngle, 0, 1);
            // scale val according to radial frequency
            val = Wrap(val * this.config.domeRadialFrequency, 0, 1);
            // center around val = 1/0 (0.5 maps to 0, 0 and 1 map to 1)
            val = Math.Abs(Map(val, 0, 1, -1, 1));

            gradientVal = Math.Abs(Map(angle, 0, 1, -1, 1));
            break;
          case 2:
            // spiral mapping
            val = MapWrap(
              angle + dist / this.config.domeRadialFrequency,
              currentAngle,
              1 + currentAngle,
              0,
              1
            );
            // scale val according to radial frequency
            val = Wrap(val * this.config.domeRadialFrequency, 0, 1);
            // center around val = 1/0 (0.5 maps to 0, 0 and 1 map to 1)
            val = Math.Abs(Map(val, 0, 1, -1, 1));

            gradientVal = dist;
            break;
          case 3:
            // bubble mapping
            var a = MapWrap(angle, currentAngle, 1 + currentAngle, 0, 1);
            // scale val according to radial frequency
            a = Wrap(a * this.config.domeRadialFrequency, 0, 1);
            // center around val = 1/0 (0.5 maps to 0, 0 and 1 map to 1)
            a = Math.Abs(Map(a, 0, 1, -1, 1));
            val = Clamp(dist - a, 0, 1);

            gradientVal = dist;
            break;
        }

        // size limit is scaled according the size slider and the current
        // level
        var sizeLimit = this.config.domeRadialSize * adjustedLevel;
        if(val <= sizeLimit) {
          // use level to determine which colors to use
          int whichGradient = (int)(level * 8);
          buffer.pixels[i].color = this.dome.GetGradientColor(
              whichGradient,
              gradientVal,
              currentGradient,
              true
            );
        }
      }
      this.dome.WriteBuffer(buffer);
    }

    // Map value x from range a-b to range c-d
    private static double Map(
      double x,
      double a,
      double b,
      double c,
      double d
    ) {
      return (x - a) * (d - c) / (b - a) + c;
    }

    // Map value x from range a-b to range c-d, clamp values outside of range
    // c-d to c or d
    private static double MapClamp(
      double x,
      double a,
      double b,
      double c,
      double d
    ) {
      return Clamp(Map(x, a, b, c, d), c, d);
    }

    // Map value x from range a-b to range c-d, wrap values outside or range c-d
    // Example: if we map to range 0-10, but get result 11.3, this is wrapped to
    // 1.3
    private static double MapWrap(
      double x,
      double a,
      double b,
      double c,
      double d
    ) {
      return Wrap(Map(x, a, b, c, d), c, d);
    }

    // Clamp value x inside range a-b
    private static double Clamp(double x, double a, double b) {
      if (x < a) return a;
      if (x > b) return b;
      return x;
    }

    // Wrap value x around range a-b
    // Example, 2.5 wrapped to 0-1 becomes 0.5
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
