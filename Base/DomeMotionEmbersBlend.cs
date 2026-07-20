using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using static Spectrum.Base.CompositeOptionValues;

namespace Spectrum.Base {

  // Replace the masked composite below with only the changes observed since
  // the previous frame. Detected changes seed a small per-layer RGB afterglow;
  // unchanged areas decay to black while destination coverage and published
  // hue remain untouched. The selecting renderer contributes only its mask.
  internal sealed class MotionEmbersBlend : DomeBlend {
    private const int SourceColor = 0;
    private const int EmberHeatColor = 1;
    private const int DifferenceColor = 2;

    public override string Id => "MotionEmbers";
    public override string DisplayName => "Motion Embers";
    public override CompositeRequirements Requirements =>
      CompositeRequirements.ReadsSourceMask |
      CompositeRequirements.ReadsDestination |
      CompositeRequirements.ReadsHistory;
    public override IReadOnlyList<DomeLayerParam> Params => paramSchema;

    private static readonly DomeLayerParam[] paramSchema =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "changeThreshold", Label = "Change Threshold",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 1, Step = .01, Default = .08,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "emberBrightness", Label = "Ember Brightness",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 4, Step = .05, Default = 1.5,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "retention", Label = "Retention Half-Life (s)",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 5, Step = .05, Default = .75,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "colorMode", Label = "Color Mode",
          Type = DomeLayerParamType.Enum,
          Options = new string[] { "Source", "Ember Heat", "Difference" },
          Default = EmberHeatColor,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "countFading", Label = "Count Fading as Motion",
          Type = DomeLayerParamType.Bool, Default = 0,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "countHueChanges", Label = "Count Hue Changes as Motion",
          Type = DomeLayerParamType.Bool, Default = 0,
          CompositorConsumed = true,
        },
      };

    public override ICompositeOptions CompileOptions(
      ImmutableDictionary<string, ParameterValue> parameters
    ) => new MotionEmbersOptions(
      Value(parameters, "changeThreshold"),
      Value(parameters, "emberBrightness"),
      Value(parameters, "retention"),
      Math.Clamp((int)Math.Round(Value(parameters, "colorMode")),
        SourceColor, DifferenceColor),
      Value(parameters, "countFading") != 0,
      Value(parameters, "countHueChanges") != 0);

    public override void Blend(in DomeBlendContext ctx) {
      if (ctx.History == null) {
        return;
      }
      var options = (MotionEmbersOptions)ctx.Options;
      LEDDomeOutputPixel[] pixels = ctx.Dest.pixels;
      LEDDomeOutputPixel[] masks = ctx.Src.pixels;
      MotionEmberState state = ctx.History.GetOrCreateState(
        () => new MotionEmberState());
      bool compare = state.Prepare(pixels.Length, ctx.Seconds,
        options.Retention, out double decay);

      for (int i = 0; i < pixels.Length; i++) {
        int current = pixels[i].color;
        state.Red[i] *= decay;
        state.Green[i] *= decay;
        state.Blue[i] *= decay;

        if (compare && TryMeasureChange(
            state.PreviousColors[i], current, options,
            out double motion, out double colorR,
            out double colorG, out double colorB)) {
          double gain = motion * options.EmberBrightness;
          state.Red[i] = Math.Max(
            state.Red[i], Math.Min(255, colorR * gain));
          state.Green[i] = Math.Max(
            state.Green[i], Math.Min(255, colorG * gain));
          state.Blue[i] = Math.Max(
            state.Blue[i], Math.Min(255, colorB * gain));
        }
        state.PreviousColors[i] = current;

        double mask = ctx.Opacity * masks[i].a;
        if (mask != 0) {
          pixels[i].LerpRGB(
            state.Red[i], state.Green[i], state.Blue[i], mask);
        }
      }
      state.Finish(ctx.Seconds);
    }

    private static bool TryMeasureChange(
      int previous, int current, MotionEmbersOptions options,
      out double motion, out double colorR,
      out double colorG, out double colorB
    ) {
      double oldR = (previous >> 16) & 0xFF;
      double oldG = (previous >> 8) & 0xFF;
      double oldB = previous & 0xFF;
      double newR = (current >> 16) & 0xFF;
      double newG = (current >> 8) & 0xFF;
      double newB = current & 0xFF;
      double oldValue = Math.Max(oldR, Math.Max(oldG, oldB)) / 255;
      double newValue = Math.Max(newR, Math.Max(newG, newB)) / 255;
      double rising = Math.Max(0, newValue - oldValue);
      double falling = options.CountFading
        ? Math.Max(0, oldValue - newValue) : 0;
      double hueChange = options.CountHueChanges
        ? ChromaChange(
            oldR, oldG, oldB, oldValue,
            newR, newG, newB, newValue)
        : 0;
      double rawMotion = Math.Max(rising, Math.Max(falling, hueChange));
      if (rawMotion <= options.ChangeThreshold ||
          options.ChangeThreshold >= 1) {
        motion = colorR = colorG = colorB = 0;
        return false;
      }
      motion = (rawMotion - options.ChangeThreshold) /
        (1 - options.ChangeThreshold);

      switch (options.ColorMode) {
        case EmberHeatColor:
          HeatColor(motion, out colorR, out colorG, out colorB);
          break;
        case DifferenceColor:
          colorR = Math.Abs(newR - oldR);
          colorG = Math.Abs(newG - oldG);
          colorB = Math.Abs(newB - oldB);
          break;
        default:
          bool usePrevious = falling > rising && falling >= hueChange;
          colorR = usePrevious ? oldR : newR;
          colorG = usePrevious ? oldG : newG;
          colorB = usePrevious ? oldB : newB;
          break;
      }
      return true;
    }

    // Compare chromatic shape independently of HSV value. Scaling by the
    // dimmer endpoint prevents unstable hue near black from becoming motion.
    private static double ChromaChange(
      double oldR, double oldG, double oldB, double oldValue,
      double newR, double newG, double newB, double newValue
    ) {
      if (oldValue == 0 || newValue == 0) {
        return 0;
      }
      double oldScale = 255 * oldValue;
      double newScale = 255 * newValue;
      double delta = Math.Max(
        Math.Abs(oldR / oldScale - newR / newScale),
        Math.Max(
          Math.Abs(oldG / oldScale - newG / newScale),
          Math.Abs(oldB / oldScale - newB / newScale)));
      return delta * Math.Min(oldValue, newValue);
    }

    private static void HeatColor(
      double motion, out double r, out double g, out double b
    ) {
      if (motion >= 1 - 1e-9) {
        r = g = b = 255;
        return;
      }
      r = 255;
      g = 72 + 183 * Math.Sqrt(motion);
      b = motion <= .55 ? 12 : 12 + 243 * (motion - .55) / .45;
    }

    private sealed class MotionEmberState {
      public int[] PreviousColors { get; private set; } = Array.Empty<int>();
      public double[] Red { get; private set; } = Array.Empty<double>();
      public double[] Green { get; private set; } = Array.Empty<double>();
      public double[] Blue { get; private set; } = Array.Empty<double>();
      private bool hasPrevious;
      private double lastSeconds;

      public bool Prepare(
        int pixelCount, double seconds, double retention,
        out double decay
      ) {
        if (this.PreviousColors.Length != pixelCount) {
          this.PreviousColors = new int[pixelCount];
          this.Red = new double[pixelCount];
          this.Green = new double[pixelCount];
          this.Blue = new double[pixelCount];
          this.hasPrevious = false;
        }
        if (!double.IsFinite(seconds) ||
            (this.hasPrevious && seconds < this.lastSeconds)) {
          Array.Clear(this.Red);
          Array.Clear(this.Green);
          Array.Clear(this.Blue);
          this.hasPrevious = false;
        }
        double elapsed = this.hasPrevious
          ? Math.Max(0, seconds - this.lastSeconds) : 0;
        decay = retention > 0 && double.IsFinite(retention)
          ? Math.Pow(.5, elapsed / retention) : 0;
        return this.hasPrevious;
      }

      public void Finish(double seconds) {
        this.lastSeconds = double.IsFinite(seconds) ? seconds : 0;
        this.hasPrevious = true;
      }
    }
  }
}
