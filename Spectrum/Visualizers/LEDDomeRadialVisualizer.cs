using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using static Spectrum.MathUtil;

namespace Spectrum {

  class LEDDomeRadialVisualizer : DomeLayerVisualizer {

    private readonly DomeLayerEnvironment environment;
    private readonly LayerRendererRuntime runtime;
    private AudioInput audio;
    private BeatBroadcaster beat;
    private DomeRenderContext dome;
    private DomeFrame buffer;

    private double currentAngle;
    private double currentGradient;
    private double currentCenterAngle;
    private double lastProgress;

    public LEDDomeRadialVisualizer(
      DomeLayerEnvironment environment,
      LayerRendererRuntime runtime,
      AudioInput audio,
      BeatBroadcaster beat,
      DomeRenderContext dome
    ) {
      this.environment = environment;
      this.runtime = runtime;
      this.audio = audio;
      this.beat = beat;
      this.dome = dome;
      this.buffer = this.dome.MakeDomeFrame();
    }

    public int Priority => 2;

    public string LayerKey => "radial";
    public DomeFrame LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.audio });
    }

    void Render() {
      // This layer's own tuning, read from its compiled runtime snapshot
      // (fade/hue speed stay global — they are shared scene state, not layer
      // params).
      RadialLayerOptions options =
        this.runtime.GetOptions<RadialLayerOptions>();
      int effect = options.Effect;
      double size = options.Size;
      double frequency = options.Frequency;
      double centerAngle = options.CenterAngle;
      double centerDistance = options.CenterDistance;
      double centerSpeed = options.CenterSpeed;
      double rotationSpeed = options.RotationSpeed;
      double gradientSpeed = options.GradientSpeed;
      int selectedPalette = options.Palette;

      buffer.Fade(1 - Math.Pow(10, -this.environment.GlobalFadeSpeed), 0);
      // Hue rotation is now applied globally by LEDDomeOutput, which rotates
      // every contributing layer's persisted buffer once per composited
      // frame — not per layer here.
      double level = this.audio.Volume;
      // Sqrt makes values larger and gives more resolution for lower values
      double adjustedLevel = Math.Clamp(Math.Sqrt(level), 0.1, 1);

      double progress = this.beat.ProgressThroughMeasure;
      // rotation is scaled by 1/4
      // otherwise it is way too fast and will make people vomit
      currentAngle += rotationSpeed *
        Wrap(progress - this.lastProgress, 0, 1) * 0.25;
      currentAngle = Wrap(currentAngle, 0, 1);
      currentGradient += gradientSpeed *
        Wrap(progress - this.lastProgress, 0, 1);
      currentGradient = Wrap(currentGradient, 0, 1);
      currentCenterAngle += centerSpeed *
        Wrap(progress - this.lastProgress, 0, 1) * 0.25;
      currentCenterAngle = Wrap(currentCenterAngle, 0, 1);
      this.lastProgress = progress;

      var centerOffset = StrutLayoutFactory.PolarToCartesian(
        centerAngle + currentCenterAngle * 2 * Math.PI,
        centerDistance
      );

      for (int i = 0; i < buffer.pixels.Length; i++) {
        ref var pixel = ref buffer.pixels[i];
        DomeTopologyPixel point = buffer.Topology.PixelAt(i);

        // The static projected (x, y) is precomputed once in
        // DomeTopology (point.X/point.Y come straight from
        // GetProjectedLEDPoint), so only the parametric transform relative to
        // the moving centerOffset needs to run per frame (M2). This inlines
        // GetProjectedLEDPointParametric without its per-pixel
        // GetProjectedLEDPoint/GetNumLEDs recomputation.
        double px = (point.X + centerOffset.Item1) * 2 - 1;
        double py = (point.Y + centerOffset.Item2) * 2 - 1;

        // map angle to 0-1
        var angle = MapWrap(Math.Atan2(py, px), -Math.PI, Math.PI, 0.0, 1.0);
        var dist = Math.Sqrt(px * px + py * py);

        double val = 0;
        double gradientVal = 0;

        switch (effect) {
          case 0:
            // radar mapping
            val = MapWrap(angle, currentAngle, 1 + currentAngle, 0, 1);
            // scale val according to radial frequency
            val = Wrap(val * frequency, 0, 1);
            // center around val = 1/0 (0.5 maps to 0, 0 and 1 map to 1)
            val = Math.Abs(Map(val, 0, 1, -1, 1));

            gradientVal = dist;
            break;
          case 1:
            // pulse mapping
            val = MapWrap(dist, currentAngle, 1 + currentAngle, 0, 1);
            // scale val according to radial frequency
            val = Wrap(val * frequency, 0, 1);
            // center around val = 1/0 (0.5 maps to 0, 0 and 1 map to 1)
            val = Math.Abs(Map(val, 0, 1, -1, 1));

            gradientVal = Math.Abs(Map(angle, 0, 1, -1, 1));
            break;
          case 2:
            // spiral mapping
            val = MapWrap(
              angle + dist / frequency,
              currentAngle,
              1 + currentAngle,
              0,
              1
            );
            // scale val according to radial frequency
            val = Wrap(val * frequency, 0, 1);
            // center around val = 1/0 (0.5 maps to 0, 0 and 1 map to 1)
            val = Math.Abs(Map(val, 0, 1, -1, 1));

            gradientVal = dist;
            break;
          case 3:
            // bubble mapping
            var a = MapWrap(angle, currentAngle, 1 + currentAngle, 0, 1);
            // scale val according to radial frequency
            a = Wrap(a * frequency, 0, 1);
            // center around val = 1/0 (0.5 maps to 0, 0 and 1 map to 1)
            a = Math.Abs(Map(a, 0, 1, -1, 1));
            val = Math.Clamp(dist - a, 0, 1);

            gradientVal = dist;
            break;
        }

        // size limit is scaled according the size param and the current
        // level
        var sizeLimit = size * adjustedLevel;
        if(val <= sizeLimit) {
          // use level to determine which colors to use
          int whichGradient = Math.Min(7, (int)(level * 8));
          pixel.color = this.dome.GetGradientColor(
              whichGradient,
              gradientVal,
              currentGradient,
              true,
              selectedPalette
            );
        }
      }
    }

    public void Visualize() {
      this.Render();
    }

  }

}
