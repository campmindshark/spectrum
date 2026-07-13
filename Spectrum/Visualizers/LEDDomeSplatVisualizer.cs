using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using static Spectrum.MathUtil;

namespace Spectrum {

  class LEDDomeSplatVisualizer : DomeLayerVisualizer {

    private Configuration config;
    private readonly LayerRendererRuntime runtime;
    private AudioInput audio;
    private BeatBroadcaster beat;
    private LEDDomeOutput dome;
    private DomeFrame buffer;

    private double lastProgress;

    public LEDDomeSplatVisualizer(
      Configuration config,
      LayerRendererRuntime runtime,
      AudioInput audio,
      BeatBroadcaster beat,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.runtime = runtime;
      this.audio = audio;
      this.beat = beat;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeFrame();
    }

    public int Priority => 2;

    public string LayerKey => "splat";
    public DomeFrame LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.audio });
    }

    void Render() {

      int paletteBank =
        this.runtime.GetOptions<PaletteLayerOptions>().Palette;

      double level = this.audio.Volume;
      // Sqrt makes values larger and gives more resolution for lower values
      double adjustedLevel = Math.Clamp(Math.Sqrt(level), 0.1, 1);

      double progress = this.beat.ProgressThroughMeasure;

      buffer.Fade(0.96, 0);

      if (progress < this.lastProgress) {
        var rand = new Random();
        var cx = Map(rand.NextDouble(), 0, 1, 0.1, 0.9);
        var cy = Map(rand.NextDouble(), 0, 1, 0.1, 0.9);
        double radius = adjustedLevel * 0.25;
        var color = rand.Next() % 8;

        for (int i = 0; i < buffer.pixels.Length; i++) {
          ref var pixel = ref buffer.pixels[i];
          DomeTopologyPixel point = buffer.Topology.PixelAt(i);

          var dx = point.X - cx;
          var dy = point.Y - cy;
          var dist = Math.Sqrt(dx * dx + dy * dy);
          if (dist < radius) {
            pixel.color = this.dome.GetGradientColor(
              color,
              dist/radius,
              0,
              true,
              paletteBank
            );
          }
        }
      }

      this.lastProgress = progress;
    }

    public void Visualize() {
      this.Render();
    }
  }

}
