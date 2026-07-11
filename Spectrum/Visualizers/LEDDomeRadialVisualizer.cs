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

    private Configuration config;
    private AudioInput audio;
    private BeatBroadcaster beat;
    private LEDDomeOutput dome;
    private LEDDomeOutputBuffer buffer;

    private double currentAngle;
    private double currentGradient;
    private double currentCenterAngle;
    private double lastProgress;

    public LEDDomeRadialVisualizer(
      Configuration config,
      AudioInput audio,
      BeatBroadcaster beat,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.audio = audio;
      this.beat = beat;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeOutputBuffer();
    }

    public int Priority {
      get {
        return DomeLayerSettings.StackActivates(
          this.config.domeLayerStack, "radial"
        ) ? 2 : 0;
      }
    }

    public string LayerKey => "radial";
    public LEDDomeOutputBuffer LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.audio });
    }

    void Render() {
      // This layer's own tuning, resolved once per frame from its stack entry
      // (fade/hue speed stay global — they are shared scene state, not layer
      // params).
      IList<DomeLayerSettings> stack = this.config.domeLayerStack;
      int effect =
        (int)DomeLayerSettings.ParamValue(stack, this.LayerKey, "effect");
      double size = DomeLayerSettings.ParamValue(stack, this.LayerKey, "size");
      double frequency =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "frequency");
      double centerAngle =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "centerAngle");
      double centerDistance =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "centerDistance");
      double centerSpeed =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "centerSpeed");
      double rotationSpeed =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "rotationSpeed");
      double gradientSpeed =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "gradientSpeed");
      int paletteBank =
        (int)DomeLayerSettings.ParamValue(stack, this.LayerKey, "palette");

      buffer.Fade(1 - Math.Pow(10, -this.config.domeGlobalFadeSpeed), 0);
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

        // The static projected (x, y) is precomputed once in
        // MakeDomeOutputBuffer (pixel.x/pixel.y come straight from
        // GetProjectedLEDPoint), so only the parametric transform relative to
        // the moving centerOffset needs to run per frame (M2). This inlines
        // GetProjectedLEDPointParametric without its per-pixel
        // GetProjectedLEDPoint/GetNumLEDs recomputation.
        double px = (pixel.x + centerOffset.Item1) * 2 - 1;
        double py = (pixel.y + centerOffset.Item2) * 2 - 1;

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
              paletteBank
            );
        }
      }
    }

    public void Visualize() {
      this.Render();
    }

  }

}
