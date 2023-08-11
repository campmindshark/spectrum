using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Spectrum {

  class LEDDomeSplatVisualizer : Visualizer {

    private Configuration config;
    private AudioInput audio;
    private LEDDomeOutput dome;
    private LEDDomeOutputBuffer buffer;

    private double lastProgress;

    public LEDDomeSplatVisualizer(
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
        return this.config.domeActiveVis == 7 ? 2 : 0;
      }
    }

    public bool Enabled { get; set; }

    public Input[] GetInputs() {
      return new Input[] { this.audio };
    }

    void Render() {

      double level = this.audio.Volume;
      // Sqrt makes values larger and gives more resolution for lower values
      double adjustedLevel = Clamp(Math.Sqrt(level), 0.1, 1);

      double progress = this.config.beatBroadcaster.ProgressThroughMeasure;

      buffer.Fade(0.96, 0);

      if (progress < this.lastProgress) {
        var rand = new Random();
        var cx = Map(rand.NextDouble(), 0, 1, 0.1, 0.9);
        var cy = Map(rand.NextDouble(), 0, 1, 0.1, 0.9);
        double radius = adjustedLevel * 0.25;
        var color = rand.Next() % 8;

        for (int i = 0; i < buffer.pixels.Length; i++) {
          var pixel = buffer.pixels[i];

          var dx = pixel.x - cx;
          var dy = pixel.y - cy;
          var dist = Math.Sqrt(dx * dx + dy * dy);
          if (dist < radius) {
            buffer.pixels[i].color = this.dome.GetGradientColor(
              color,
              dist/radius,
              0,
              true
            );
          }
        }
      }

      this.dome.WriteBuffer(buffer);
      this.lastProgress = progress;
    }

    public void Visualize() {
      this.Render();

      this.dome.Flush();
    }

    // Clamp value x inside range a-b
    private static double Clamp(double x, double a, double b) {
      if (x < a) return a;
      if (x > b) return b;
      return x;
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
  }

}
