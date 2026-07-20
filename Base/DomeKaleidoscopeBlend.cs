using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using static Spectrum.Base.CompositeOptionValues;

namespace Spectrum.Base {

  // Fold the composite below into repeated angular sectors in the dome's
  // top-down projection. The selecting layer supplies only an alpha mask;
  // its color is ignored. Each destination pixel resolves a transformed
  // top-down coordinate through DomeTopology's arbitrary spatial index and
  // reads the compositor's pre-pass snapshot, avoiding order-dependent smear.
  internal sealed class KaleidoscopeBlend : DomeBlend {
    public override string Id => "Kaleidoscope";
    public override CompositeRequirements Requirements =>
      CompositeRequirements.ReadsSourceMask |
      CompositeRequirements.ReadsDestination |
      CompositeRequirements.ReadsDestinationNeighbors |
      CompositeRequirements.ReadsOrientation;
    public override IReadOnlyList<DomeLayerParam> Params => paramSchema;

    private static readonly DomeLayerParam[] paramSchema =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "sectors", Label = "Sector Count",
          Type = DomeLayerParamType.Double,
          Min = 2, Max = 24, Step = 1, Default = 8,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "mirror", Label = "Sector Mode",
          Type = DomeLayerParamType.Enum,
          Options = new string[] { "Repeat", "Mirror" }, Default = 1,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "spin", Label = "Sector Spin",
          Type = DomeLayerParamType.Double,
          Min = -2, Max = 2, Step = 0.05, Default = 0.05,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "focalAngle", Label = "Focal Angle",
          Type = DomeLayerParamType.Double,
          Min = -Math.PI, Max = Math.PI, Step = 0.01, Default = 0,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "focalDistance", Label = "Focal Distance",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 0.8, Step = 0.01, Default = 0,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "follow", Label = "Follow Orientation",
          Type = DomeLayerParamType.Bool, Default = 0,
          CompositorConsumed = true,
        },
      };

    public override ICompositeOptions CompileOptions(
      ImmutableDictionary<string, ParameterValue> parameters
    ) => new KaleidoscopeOptions(
      (int)Math.Round(Value(parameters, "sectors")),
      Value(parameters, "mirror") != 0,
      Value(parameters, "spin"), Value(parameters, "focalAngle"),
      Value(parameters, "focalDistance"),
      Value(parameters, "follow") != 0);

    public override void Blend(in DomeBlendContext ctx) {
      double opacity = ctx.Opacity;
      if (opacity == 0) {
        return;
      }
      var options = (KaleidoscopeOptions)ctx.Options;
      int sectors = Math.Max(1, options.SectorCount);
      double focalAngle = options.FocalAngle;
      if (options.FollowOrientation && ctx.Orientation != null &&
          ctx.Orientation.TryGetAngle(out double orientationAngle)) {
        focalAngle = orientationAngle;
      }
      double focalX = options.FocalDistance * Math.Cos(focalAngle);
      double focalY = options.FocalDistance * Math.Sin(focalAngle);
      double phase = 2 * Math.PI * options.Spin * ctx.Seconds;
      DomeFrame dest = ctx.Dest;
      LEDDomeOutputPixel[] pixels = dest.pixels;
      LEDDomeOutputPixel[] maskPixels = ctx.Src.pixels;
      LEDDomeOutputPixel[] snapshot = ctx.Snapshot.pixels;
      for (int i = 0; i < pixels.Length; i++) {
        double mask = opacity * maskPixels[i].a;
        if (mask == 0) {
          continue;
        }
        DomeTopologyPixel point = dest.Topology.PixelAt(i);
        double x = 2 * point.TopDownX - 1 - focalX;
        double y = 1 - 2 * point.TopDownY - focalY;
        FoldCoordinate(
          x, y, sectors, options.MirrorSectors, phase,
          out double sampleX, out double sampleY);
        double topDownX = (sampleX + focalX + 1) * 0.5;
        double topDownY = (1 - sampleY - focalY) * 0.5;
        int sample = dest.NearestTopDownPixel(topDownX, topDownY);
        if (sample < 0) {
          continue;
        }
        pixels[i].LerpRGB(
          snapshot[sample].r, snapshot[sample].g,
          snapshot[sample].b, mask);
      }
    }

    private static void FoldCoordinate(
      double x, double y, int sectors, bool mirror, double phase,
      out double sampleX, out double sampleY
    ) {
      double radius = Math.Sqrt(x * x + y * y);
      if (radius == 0) {
        sampleX = 0;
        sampleY = 0;
        return;
      }
      double sectorWidth = 2 * Math.PI / sectors;
      double relative = Math.Atan2(y, x) - phase;
      relative -= Math.Floor(relative / (2 * Math.PI)) * 2 * Math.PI;
      int sector = Math.Min(
        sectors - 1, (int)Math.Floor(relative / sectorWidth));
      double local = relative - sector * sectorWidth;
      if (mirror && (sector & 1) != 0) {
        local = sectorWidth - local;
      }
      double sampleAngle = phase + local;
      sampleX = radius * Math.Cos(sampleAngle);
      sampleY = radius * Math.Sin(sampleAngle);
    }
  }
}
