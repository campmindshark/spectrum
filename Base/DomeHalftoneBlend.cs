using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using static Spectrum.Base.CompositeOptionValues;

namespace Spectrum.Base {

  // Replace the masked composite with a regular field of luminous cells whose
  // occupied area carries the sampled brightness. Dots and triangles use the
  // dome's top-down projection; strut segments use the physical LED address so
  // that their breaks remain coherent along deployed hardware. Each cell reads
  // one point from the pre-pass snapshot, preserving broad value shapes without
  // letting the output pixel order smear the pattern.
  internal sealed class HalftoneBlend : DomeBlend {
    private const double SqrtThreeOverTwo = 0.8660254037844386;
    private const double NominalLedPitch = 0.013;

    public override string Id => "Halftone";
    public override CompositeRequirements Requirements =>
      CompositeRequirements.ReadsSourceMask |
      CompositeRequirements.ReadsDestination |
      CompositeRequirements.ReadsDestinationNeighbors;
    public override IReadOnlyList<DomeLayerParam> Params => paramSchema;

    private static readonly DomeLayerParam[] paramSchema =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "cellType", Label = "Cell Type",
          Type = DomeLayerParamType.Enum,
          Options = new string[] { "Dots", "Triangles", "Strut Segments" },
          Default = 0, CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "scale", Label = "Cell Scale",
          Type = DomeLayerParamType.Double,
          Min = 0.04, Max = 0.4, Step = 0.01, Default = 0.14,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "threshold", Label = "Brightness Threshold",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 0.95, Step = 0.01, Default = 0.05,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "dotMin", Label = "Minimum Dot",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 1, Step = 0.02, Default = 0.08,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "dotMax", Label = "Maximum Dot",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 1, Step = 0.02, Default = 0.94,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "rotation", Label = "Rotation",
          Type = DomeLayerParamType.Double,
          Min = -Math.PI, Max = Math.PI, Step = 0.01, Default = 0,
          CompositorConsumed = true,
        },
        new DomeLayerParam {
          Key = "palette", Label = "Palette",
          Type = DomeLayerParamType.Enum,
          Options = DefaultPaletteOptions(),
          Default = 0, CompositorConsumed = true,
        },
      };

    private static string[] DefaultPaletteOptions() {
      var options = new string[PaletteService.MaxPalettes];
      for (int i = 0; i < options.Length; i++) {
        options[i] = "Palette " + (i + 1);
      }
      return options;
    }

    public override ICompositeOptions CompileOptions(
      ImmutableDictionary<string, ParameterValue> parameters
    ) => new HalftoneOptions(
      (int)Math.Round(Value(parameters, "cellType")),
      Value(parameters, "scale"), Value(parameters, "threshold"),
      Value(parameters, "dotMin"), Value(parameters, "dotMax"),
      Value(parameters, "rotation"),
      (int)Math.Round(Value(parameters, "palette")));

    public override void Blend(in DomeBlendContext ctx) {
      double opacity = ctx.Opacity;
      if (opacity == 0) {
        return;
      }
      var options = (HalftoneOptions)ctx.Options;
      double scale = Math.Max(NominalLedPitch, options.Scale);
      double minimum = Math.Min(options.DotMinimum, options.DotMaximum);
      double maximum = Math.Max(options.DotMinimum, options.DotMaximum);
      LEDDomeOutputPixel[] pixels = ctx.Dest.pixels;
      LEDDomeOutputPixel[] mask = ctx.Src.pixels;
      LEDDomeOutputPixel[] snapshot = ctx.Snapshot.pixels;

      for (int i = 0; i < pixels.Length; i++) {
        double adjustmentMask = opacity * mask[i].a;
        if (adjustmentMask == 0) {
          continue;
        }

        CellSample cell = options.CellType switch {
          1 => TriangleCell(ctx.Dest, i, scale, options.Rotation),
          2 => StrutCell(ctx.Dest, i, scale, options.Rotation),
          _ => DotCell(ctx.Dest, i, scale, options.Rotation),
        };
        LEDDomeOutputPixel sampled = snapshot[cell.SampleIndex];
        // Additive layers retain unclamped floating-point channels internally;
        // constrain their luma before it reaches the normalized palette API.
        double luma = Math.Clamp(Luma(sampled) / 255, 0, 1);
        double level = NormalizeLevel(luma, options.Threshold);
        double size = minimum + (maximum - minimum) * level;
        double coverage = level > 0 ? cell.Coverage(size) : 0;

        ResolveColor(ctx, sampled, luma, options.Palette,
          out double red, out double green, out double blue);
        pixels[i].LerpRGB(
          red * coverage, green * coverage, blue * coverage,
          adjustmentMask);
      }
    }

    private static CellSample DotCell(
      DomeFrame frame, int pixel, double scale, double rotation
    ) {
      Project(frame.Topology.PixelAt(pixel), rotation, out double x,
        out double y);
      double cellX = Math.Floor(x / scale) + 0.5;
      double cellY = Math.Floor(y / scale) + 0.5;
      double centerX = cellX * scale;
      double centerY = cellY * scale;
      int sample = ProjectedSample(frame, centerX, centerY, rotation);
      double dx = x / scale - cellX;
      double dy = y / scale - cellY;
      double distance = Math.Sqrt(dx * dx + dy * dy);
      double antialias = Antialias(scale);
      return new CellSample(sample, 0.5, -distance, antialias);
    }

    private static CellSample TriangleCell(
      DomeFrame frame, int pixel, double scale, double rotation
    ) {
      Project(frame.Topology.PixelAt(pixel), rotation, out double x,
        out double y);
      // Resolve the point in an equilateral-triangle basis. Each parallelogram
      // is split along a+b=1, giving alternating upright/inverted cells.
      double basisB = y / (scale * SqrtThreeOverTwo);
      double basisA = x / scale - basisB * 0.5;
      double wholeA = Math.Floor(basisA);
      double wholeB = Math.Floor(basisB);
      double fractionA = basisA - wholeA;
      double fractionB = basisB - wholeB;
      double minimumBarycentric;
      double centerA;
      double centerB;
      if (fractionA + fractionB <= 1) {
        minimumBarycentric = Math.Min(
          1 - fractionA - fractionB,
          Math.Min(fractionA, fractionB));
        centerA = wholeA + 1.0 / 3;
        centerB = wholeB + 1.0 / 3;
      } else {
        minimumBarycentric = Math.Min(
          fractionA + fractionB - 1,
          Math.Min(1 - fractionA, 1 - fractionB));
        centerA = wholeA + 2.0 / 3;
        centerB = wholeB + 2.0 / 3;
      }
      double centerX = scale * (centerA + centerB * 0.5);
      double centerY = scale * SqrtThreeOverTwo * centerB;
      int sample = ProjectedSample(frame, centerX, centerY, rotation);
      double antialias = Antialias(scale) / 3;
      return new CellSample(
        sample, 1.0 / 3, minimumBarycentric - 1.0 / 3, antialias);
    }

    private static CellSample StrutCell(
      DomeFrame frame, int pixel, double scale, double rotation
    ) {
      DomeTopologyPixel point = frame.Topology.PixelAt(pixel);
      int cellLength = Math.Max(1, (int)Math.Round(scale / NominalLedPitch));
      double phase = rotation / (2 * Math.PI) * cellLength;
      double coordinate = point.LedIndex + phase;
      double cell = Math.Floor(coordinate / cellLength);
      double local = coordinate / cellLength - cell;
      int centerLed = (int)Math.Round(
        (cell + 0.5) * cellLength - phase);
      int ledCount = frame.Topology.StrutPixelCount(point.StrutIndex);
      centerLed = Math.Clamp(centerLed, 0, Math.Max(0, ledCount - 1));
      int sample = frame.Topology.FrameIndexAt(point.StrutIndex, centerLed);
      double distance = Math.Abs(local - 0.5);
      double antialias = 0.5 / cellLength;
      return new CellSample(sample, 0.5, -distance, antialias);
    }

    private static void Project(
      DomeTopologyPixel point, double rotation, out double x, out double y
    ) {
      double px = 2 * point.TopDownX - 1;
      double py = 1 - 2 * point.TopDownY;
      double cosine = Math.Cos(rotation);
      double sine = Math.Sin(rotation);
      x = px * cosine + py * sine;
      y = -px * sine + py * cosine;
    }

    private static int ProjectedSample(
      DomeFrame frame, double x, double y, double rotation
    ) {
      double cosine = Math.Cos(rotation);
      double sine = Math.Sin(rotation);
      double px = x * cosine - y * sine;
      double py = x * sine + y * cosine;
      return frame.NearestTopDownPixel((px + 1) * 0.5, (1 - py) * 0.5);
    }

    private static double NormalizeLevel(double luma, double threshold) {
      if (luma <= threshold) {
        return 0;
      }
      return Math.Min(1, (luma - threshold) / Math.Max(1e-9, 1 - threshold));
    }

    private static double Antialias(double scale) =>
      Math.Min(0.12, NominalLedPitch / Math.Max(scale, NominalLedPitch));

    private static double SmoothCoverage(double signedDistance, double width) {
      if (width <= 0) {
        return signedDistance >= 0 ? 1 : 0;
      }
      double t = (signedDistance + width) / (2 * width);
      if (t <= 0) {
        return 0;
      }
      if (t >= 1) {
        return 1;
      }
      return t * t * (3 - 2 * t);
    }

    private static double Luma(LEDDomeOutputPixel pixel) =>
      0.299 * pixel.r + 0.587 * pixel.g + 0.114 * pixel.b;

    private static void ResolveColor(
      in DomeBlendContext ctx, LEDDomeOutputPixel sampled, double luma,
      int palette,
      out double red, out double green, out double blue
    ) {
      if (ctx.PaletteColor != null) {
        int color = ctx.PaletteColor(palette, luma);
        red = (color >> 16) & 0xFF;
        green = (color >> 8) & 0xFF;
        blue = color & 0xFF;
        return;
      }

      // A palette-less host still gets true halftone area modulation: lift the
      // sampled color to full value, while its original luma controls cell size.
      double value = Math.Max(sampled.r, Math.Max(sampled.g, sampled.b));
      if (value <= 0) {
        red = green = blue = 0;
        return;
      }
      double lift = 255 / value;
      red = sampled.r * lift;
      green = sampled.g * lift;
      blue = sampled.b * lift;
    }

    private readonly record struct CellSample(
      int SampleIndex, double SizeCoefficient, double Offset,
      double AntialiasWidth
    ) {
      public double Coverage(double size) => SmoothCoverage(
        this.SizeCoefficient * size + this.Offset,
        this.AntialiasWidth);
    }
  }
}
