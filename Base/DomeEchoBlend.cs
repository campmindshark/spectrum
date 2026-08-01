using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using static Spectrum.Base.CompositeOptionValues;

namespace Spectrum.Base {

  // Retain the composite below and screen delayed, transformed copies back
  // over it. History is supplied by the compositor per layer instance because
  // DomeBlend registry objects are shared singletons. The selecting renderer
  // contributes only its alpha mask.
  internal sealed class EchoBlend : DomeBlend {
    public override string Id => "Echo";
    public override CompositeRequirements Requirements =>
      CompositeRequirements.ReadsSourceMask |
      CompositeRequirements.ReadsDestination |
      CompositeRequirements.ReadsDestinationNeighbors |
      CompositeRequirements.ReadsHistory;
    public override IReadOnlyList<DomeLayerParam> Params => paramSchema;

    private static readonly DomeLayerParam[] paramSchema =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "copies", Label = "Copy Count",
          Type = DomeLayerParamType.Double,
          Min = 1, Max = 8, Step = 1, Default = 4,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "delay", Label = "Copy Delay",
          Type = DomeLayerParamType.Double,
          Min = .05, Max = 2, Step = .05, Default = .2,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "rotation", Label = "Rotation Per Copy",
          Type = DomeLayerParamType.Double,
          Min = -Math.PI, Max = Math.PI, Step = .01, Default = .12,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "scale", Label = "Scale Per Copy",
          Type = DomeLayerParamType.Double,
          Min = .7, Max = 1.3, Step = .01, Default = .94,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "drift", Label = "Drift Per Copy",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = .25, Step = .005, Default = .025,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "direction", Label = "Drift Direction",
          Type = DomeLayerParamType.Double,
          Min = -Math.PI, Max = Math.PI, Step = .01, Default = 0,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "decay", Label = "Copy Decay",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 1, Step = .01, Default = .65,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "hueShift", Label = "Hue Shift Per Copy",
          Type = DomeLayerParamType.Double,
          Min = -.5, Max = .5, Step = .01, Default = .04,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "saturation", Label = "Saturation Per Copy",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 1, Step = .01, Default = 1,
          CompositorConsumed = true,
        },
      };

    public override ICompositeOptions CompileOptions(
      ImmutableDictionary<string, ParameterValue> parameters
    ) => new EchoOptions(
      (int)Math.Round(Value(parameters, "copies")),
      Value(parameters, "delay"), Value(parameters, "rotation"),
      Value(parameters, "scale"), Value(parameters, "drift"),
      Value(parameters, "direction"), Value(parameters, "decay"),
      Value(parameters, "hueShift"),
      parameters != null && parameters.ContainsKey("saturation")
        ? Value(parameters, "saturation")
        : 1);

    public override void Blend(in DomeBlendContext ctx) {
      if (ctx.History == null || ctx.Snapshot == null) {
        return;
      }
      var options = (EchoOptions)ctx.Options;
      int copyCount = Math.Max(1, options.CopyCount);
      ctx.History.Capture(
        ctx.Snapshot, ctx.Seconds, options.Delay * copyCount);
      if (ctx.Opacity == 0) {
        return;
      }

      DomeFrame dest = ctx.Dest;
      LEDDomeOutputPixel[] pixels = dest.pixels;
      LEDDomeOutputPixel[] maskPixels = ctx.Src.pixels;
      EchoSamplingState sampling = ctx.History.GetOrCreateState(
        () => new EchoSamplingState());
      sampling.Ensure(dest.Topology, options, copyCount);
      double weight = 1;
      for (int copy = 1; copy <= copyCount; copy++) {
        if (!ctx.History.TryGetAtOrBefore(
            ctx.Seconds - copy * options.Delay,
            out int[]? historicalColors)) {
          continue;
        }
        double hueShift = copy * options.HueShift;
        double saturation = Math.Pow(options.Saturation, copy);
        for (int i = 0; i < pixels.Length; i++) {
          double mask = ctx.Opacity * maskPixels[i].a * weight;
          if (mask == 0) {
            continue;
          }
          int sample = sampling.SampleAt(copy - 1, i);
          if (sample < 0) {
            continue;
          }
          var echo = new LEDDomeOutputPixel {
            color = historicalColors[sample],
          };
          ScaleSaturation(ref echo, saturation);
          echo.HueRotate(hueShift);
          ApplyScreen(ref pixels[i], echo, mask);
        }
        weight *= options.Decay;
        if (weight == 0) {
          break;
        }
      }
    }

    // Echo's geometric transform depends only on immutable operation options
    // and the shared topology. Cache its exact nearest-pixel result with the
    // compositor-owned per-layer state instead of repeating dictionary-backed
    // spatial searches for every copy of every frame.
    private sealed class EchoSamplingState {
      private DomeTopology? topology;
      private EchoOptions? options;
      private int copyCount;
      private int pixelCount;
      private int[] samples = Array.Empty<int>();

      public void Ensure(
        DomeTopology topology, EchoOptions options, int copyCount
      ) {
        int pixelCount = topology.PixelCount;
        if (ReferenceEquals(this.topology, topology) &&
            ReferenceEquals(this.options, options) &&
            this.copyCount == copyCount &&
            this.pixelCount == pixelCount) {
          return;
        }

        int required = checked(copyCount * pixelCount);
        if (this.samples.Length != required) {
          this.samples = new int[required];
        }
        double driftX = options.Drift * Math.Cos(options.DriftDirection);
        double driftY = options.Drift * Math.Sin(options.DriftDirection);
        for (int copyIndex = 0; copyIndex < copyCount; copyIndex++) {
          int copy = copyIndex + 1;
          double angle = copy * options.Rotation;
          double cos = Math.Cos(angle);
          double sin = Math.Sin(angle);
          double scale = Math.Pow(options.Scale, copy);
          double offsetX = copy * driftX;
          double offsetY = copy * driftY;
          int sampleOffset = copyIndex * pixelCount;
          for (int i = 0; i < pixelCount; i++) {
            DomeTopologyPixel point = topology.PixelAt(i);
            double x = 2 * point.TopDownX - 1 - offsetX;
            double y = 1 - 2 * point.TopDownY - offsetY;
            double sampleX = (cos * x + sin * y) / scale;
            double sampleY = (-sin * x + cos * y) / scale;
            this.samples[sampleOffset + i] =
              sampleX * sampleX + sampleY * sampleY > 1
                ? -1
                : topology.NearestTopDownPixel(
                    (sampleX + 1) * .5, (1 - sampleY) * .5);
          }
        }
        this.topology = topology;
        this.options = options;
        this.copyCount = copyCount;
        this.pixelCount = pixelCount;
      }

      public int SampleAt(int copyIndex, int pixelIndex) =>
        this.samples[copyIndex * this.pixelCount + pixelIndex];
    }

    // Scale HSV saturation without a full RGB/HSV round trip. Pulling every
    // channel toward the current maximum preserves both hue and HSV value;
    // compounding the factor per copy makes older echoes progressively paler.
    private static void ScaleSaturation(
      ref LEDDomeOutputPixel pixel, double scale
    ) {
      if (scale >= 1 || pixel.color == 0) {
        return;
      }
      double max = Math.Max(pixel.r, Math.Max(pixel.g, pixel.b));
      if (scale <= 0) {
        pixel.SetRGB(max, max, max);
        return;
      }
      pixel.SetRGB(
        max - (max - pixel.r) * scale,
        max - (max - pixel.g) * scale,
        max - (max - pixel.b) * scale);
    }

    private static void ApplyScreen(
      ref LEDDomeOutputPixel dest, LEDDomeOutputPixel echo, double weight
    ) {
      dest.SetRGB(
        255 - (255 - dest.r) * (1 - weight * echo.r / 255),
        255 - (255 - dest.g) * (1 - weight * echo.g / 255),
        255 - (255 - dest.b) * (1 - weight * echo.b / 255));
    }
  }
}
