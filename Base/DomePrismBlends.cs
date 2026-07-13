using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;
using static Spectrum.Base.CompositeOptionValues;

namespace Spectrum.Base {

  // The prism family (docs/prism.md): adjustment blends (source alpha is the
  // mask) whose tunables live in each class's Params — the value rides the
  // Params bag of whichever layer selects the blend, and only the compositor
  // reads it, never a visualizer. Offsets are in projected-plane units (the
  // same normalized x/y the buffer bakes), which is why the defaults look
  // small: a dome LED pitch is ~0.013, and fringes want a few pitches to read
  // at dome scale.
  //
  // ChromaticFringe, EdgeSpectrum and Refract are *spatial* — they resample
  // the composite below through the baked neighbor table on
  // DomeFrame, so they declare neighbor-read requirements and read the
  // compositor's pre-pass copy. Iridescence is per-pixel but keyed to the
  // baked unit-sphere normals.

  // RGB channel-split aberration: R is pulled from +offset along the split
  // axis, B from the opposite offset, G stays in place. offset = how far apart
  // the R and B images land; spin = rotate the split axis over time
  // (turns/sec, signed; 0 holds a fixed axis); follow = drive the axis from
  // the spotlighted wand's orientation instead, in which case spin is
  // disregarded.
  internal sealed class ChromaticFringeBlend : DomeBlend {
    public override string Name => "ChromaticFringe";
    public override CompositeRequirements Requirements =>
      CompositeRequirements.ReadsSourceMask |
      CompositeRequirements.ReadsDestination |
      CompositeRequirements.ReadsDestinationNeighbors;
    public override IReadOnlyList<DomeLayerParam> Params => paramSchema;
    private static readonly DomeLayerParam[] paramSchema = new DomeLayerParam[] {
      new DomeLayerParam {
        Key = "offset", Label = "Fringe Offset",
        Type = DomeLayerParamType.Double,
        Min = 0.005, Max = 0.12, Step = 0.005, Default = 0.045,
        CompositorConsumed = true,
      },
      new DomeLayerParam {
        Key = "spin", Label = "Angle Spin",
        Type = DomeLayerParamType.Double,
        Min = -2, Max = 2, Step = 0.05, Default = 0,
        CompositorConsumed = true,
      },
      new DomeLayerParam {
        Key = "follow", Label = "Follow Orientation",
        Type = DomeLayerParamType.Bool,
        Default = 0,
        CompositorConsumed = true,
      },
    };

    public override ICompositeOptions CompileOptions(
      ImmutableDictionary<string, ParameterValue> parameters
    ) => new ChromaticFringeOptions(
      Value(parameters, "offset"), Value(parameters, "spin"),
      Value(parameters, "follow") != 0);

    public override void Blend(in DomeBlendContext ctx) {
      var options = (ChromaticFringeOptions)ctx.Options;
      double angle = ctx.PrismAngle(
        options.Spin, options.FollowOrientation);
      int radiusBin = DomeFrame.RadiusBin(
        options.Offset);
      DomeFrame dest = ctx.Dest;
      dest.EnsureNeighborTable();
      int fwd = DomeFrame.DirBin(angle);
      int back = (fwd + DomeFrame.NeighborDirections / 2)
        % DomeFrame.NeighborDirections;
      LEDDomeOutputPixel[] pixels = dest.pixels;
      LEDDomeOutputPixel[] src = ctx.Src.pixels;
      LEDDomeOutputPixel[] snapshot = ctx.Snapshot.pixels;
      double o = ctx.Opacity;
      for (int i = 0; i < pixels.Length; i++) {
        double mask = o * src[i].a;
        if (mask == 0) {
          continue;
        }
        int ri = dest.NeighborAt(i, fwd, radiusBin);
        int bi = dest.NeighborAt(i, back, radiusBin);
        // Lerp R and B toward the split samples; G lerps toward itself (a
        // no-op) so the whole step is one single-repack channel op.
        pixels[i].LerpRGB(
          snapshot[ri].r, pixels[i].g, snapshot[bi].b, mask);
      }
    }
  }

  // Estimate the composite's luminance gradient from the pre-pass snapshot
  // (central differences along ±x/±y at the sample radius) and add spectral
  // colour where it is steep — hue keyed to gradient direction through the
  // dispersion ramp, intensity to magnitude. Flat fills get nothing. strength
  // scales intensity; offset is the gradient sample radius.
  internal sealed class EdgeSpectrumBlend : DomeBlend {
    public override string Name => "EdgeSpectrum";
    public override CompositeRequirements Requirements =>
      CompositeRequirements.ReadsSourceMask |
      CompositeRequirements.ReadsDestination |
      CompositeRequirements.ReadsDestinationNeighbors;
    public override IReadOnlyList<DomeLayerParam> Params => paramSchema;
    private static readonly DomeLayerParam[] paramSchema = new DomeLayerParam[] {
      new DomeLayerParam {
        Key = "strength", Label = "Edge Strength",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 4, Step = 0.05, Default = 1.5,
        CompositorConsumed = true,
      },
      new DomeLayerParam {
        Key = "offset", Label = "Sample Radius",
        Type = DomeLayerParamType.Double,
        Min = 0.005, Max = 0.12, Step = 0.005, Default = 0.03,
        CompositorConsumed = true,
      },
    };

    public override ICompositeOptions CompileOptions(
      ImmutableDictionary<string, ParameterValue> parameters
    ) => new EdgeSpectrumOptions(
      Value(parameters, "strength"), Value(parameters, "offset"));

    public override void Blend(in DomeBlendContext ctx) {
      var options = (EdgeSpectrumOptions)ctx.Options;
      double strength = options.Strength;
      int radiusBin = DomeFrame.RadiusBin(
        options.Offset);
      DomeFrame dest = ctx.Dest;
      dest.EnsureNeighborTable();
      int right = DomeFrame.DirBin(0);
      int left = DomeFrame.DirBin(Math.PI);
      int up = DomeFrame.DirBin(Math.PI / 2);
      int down = DomeFrame.DirBin(3 * Math.PI / 2);
      LEDDomeOutputPixel[] pixels = dest.pixels;
      LEDDomeOutputPixel[] src = ctx.Src.pixels;
      LEDDomeOutputPixel[] snapshot = ctx.Snapshot.pixels;
      double o = ctx.Opacity;
      for (int i = 0; i < pixels.Length; i++) {
        double mask = o * src[i].a;
        if (mask == 0) {
          continue;
        }
        double gx =
          Luma(snapshot[dest.NeighborAt(i, right, radiusBin)]) -
          Luma(snapshot[dest.NeighborAt(i, left, radiusBin)]);
        double gy =
          Luma(snapshot[dest.NeighborAt(i, up, radiusBin)]) -
          Luma(snapshot[dest.NeighborAt(i, down, radiusBin)]);
        // Magnitude normalized to 0..1 over a 0..255 channel span.
        double mag = Math.Sqrt(gx * gx + gy * gy) / 255;
        if (mag <= 0) {
          continue;
        }
        double intensity = mag * strength;
        if (intensity > 1) {
          intensity = 1;
        }
        double t = (Math.Atan2(gy, gx) + Math.PI) / (2 * Math.PI);
        LEDColor.SpectralColor(t, out double sr, out double sg, out double sb);
        double w = intensity * mask;
        pixels[i].AddRGB(sr * w, sg * w, sb * w);
      }
    }

    private static double Luma(LEDDomeOutputPixel p) {
      return 0.299 * p.r + 0.587 * p.g + 0.114 * p.b;
    }
  }

  // Water-surface refraction (docs/caustics.md): make the composite below
  // shimmer as if seen through water. Structurally ChromaticFringe with a
  // per-pixel direction instead of a global one — the source layer publishes
  // a displacement *field* through its side channels (direction in `hue`,
  // 0..1 = 0..2π; magnitude in alpha, which doubles as the mask), and each
  // masked pixel lerps toward the snapshot sample displaced along that
  // vector. Caustics is the field publisher today (its analytic-surface
  // gradient), but the contract is just "hue + alpha", so any layer that
  // fills both can drive it. strength = max displacement in projected-plane
  // units; the neighbor table caps the reach at 4 × 0.02 = 0.08 (~6 LED
  // pitches) — plenty for shimmer.
  internal sealed class RefractBlend : DomeBlend {
    public override string Name => "Refract";
    public override CompositeRequirements Requirements =>
      CompositeRequirements.ReadsSourceMask |
      CompositeRequirements.ReadsDestination |
      CompositeRequirements.ReadsDestinationNeighbors;
    public override IReadOnlyList<DomeLayerParam> Params => paramSchema;
    private static readonly DomeLayerParam[] paramSchema = new DomeLayerParam[] {
      new DomeLayerParam {
        Key = "strength", Label = "Refraction Strength",
        Type = DomeLayerParamType.Double,
        Min = 0.01, Max = 0.08, Step = 0.005, Default = 0.05,
        CompositorConsumed = true,
      },
    };

    public override ICompositeOptions CompileOptions(
      ImmutableDictionary<string, ParameterValue> parameters
    ) => new RefractOptions(Value(parameters, "strength"));

    public override void Blend(in DomeBlendContext ctx) {
      double strength = ((RefractOptions)ctx.Options).Strength;
      DomeFrame dest = ctx.Dest;
      dest.EnsureNeighborTable();
      LEDDomeOutputPixel[] pixels = dest.pixels;
      LEDDomeOutputPixel[] src = ctx.Src.pixels;
      LEDDomeOutputPixel[] snapshot = ctx.Snapshot.pixels;
      double o = ctx.Opacity;
      for (int i = 0; i < pixels.Length; i++) {
        // The field magnitude is both how far the sample is displaced and how
        // strongly the displaced sample replaces the pixel — flat water
        // (gradient ~0) leaves the composite untouched.
        double mag = src[i].a;
        double mask = o * mag;
        if (mask == 0) {
          continue;
        }
        int dir = DomeFrame.DirBin(src[i].hue * 2 * Math.PI);
        int radius = DomeFrame.RadiusBin(mag * strength);
        int j = dest.NeighborAt(i, dir, radius);
        pixels[i].LerpRGB(snapshot[j].r, snapshot[j].g, snapshot[j].b, mask);
      }
    }
  }

  // Thin-film sheen keyed to the baked unit-sphere normals: for each masked
  // pixel, the angle between its normal and a virtual light picks a spectral
  // tint (repeated `bands` times across the curvature); the tint is scaled to
  // the pixel's own brightness so unlit pixels stay dark. strength = how far
  // it recolours (0 = off, 1 = full); spin = sweep the light's azimuth over
  // time (turns/sec, signed; 0 holds it fixed); follow = drive the azimuth
  // from the spotlighted wand's orientation instead, in which case spin is
  // disregarded. No neighbor sampling, so no snapshot needed.
  internal sealed class IridescenceBlend : DomeBlend {
    public override string Name => "Iridescence";
    public override CompositeRequirements Requirements =>
      CompositeRequirements.ReadsSourceMask |
      CompositeRequirements.ReadsDestination;
    public override IReadOnlyList<DomeLayerParam> Params => paramSchema;
    private static readonly DomeLayerParam[] paramSchema = new DomeLayerParam[] {
      new DomeLayerParam {
        Key = "strength", Label = "Sheen Strength",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 1, Step = 0.02, Default = 0.5,
        CompositorConsumed = true,
      },
      new DomeLayerParam {
        Key = "bands", Label = "Spectral Bands",
        Type = DomeLayerParamType.Double,
        Min = 0.5, Max = 8, Step = 0.5, Default = 2,
        CompositorConsumed = true,
      },
      new DomeLayerParam {
        Key = "spin", Label = "Light Spin",
        Type = DomeLayerParamType.Double,
        Min = -2, Max = 2, Step = 0.05, Default = 0.2,
        CompositorConsumed = true,
      },
      new DomeLayerParam {
        Key = "follow", Label = "Follow Orientation",
        Type = DomeLayerParamType.Bool,
        Default = 0,
        CompositorConsumed = true,
      },
    };

    public override ICompositeOptions CompileOptions(
      ImmutableDictionary<string, ParameterValue> parameters
    ) => new IridescenceOptions(
      Value(parameters, "strength"), Value(parameters, "bands"),
      Value(parameters, "spin"), Value(parameters, "follow") != 0);

    public override void Blend(in DomeBlendContext ctx) {
      var options = (IridescenceOptions)ctx.Options;
      double strength = options.Strength;
      double bands = options.Bands;
      double azim = ctx.PrismAngle(
        options.Spin, options.FollowOrientation);
      // Virtual light swept in azimuth, held at a fixed elevation so the
      // sheen bands read as horizontal-ish arcs across the dome.
      Vector3 light = Vector3.Normalize(new Vector3(
        (float)Math.Cos(azim), (float)Math.Sin(azim), 0.6f));
      ImmutableArray<Vector3> normals = ctx.Dest.Normals;
      LEDDomeOutputPixel[] pixels = ctx.Dest.pixels;
      LEDDomeOutputPixel[] src = ctx.Src.pixels;
      double o = ctx.Opacity;
      for (int i = 0; i < pixels.Length; i++) {
        double w = o * src[i].a * strength;
        if (w == 0) {
          continue;
        }
        double d = Vector3.Dot(normals[i], light); // -1..1
        double t = (d + 1) * 0.5 * bands;
        t -= Math.Floor(t); // wrap into 0..1 so bands repeat
        LEDColor.SpectralColor(t, out double sr, out double sg, out double sb);
        double v = Math.Max(
          pixels[i].r, Math.Max(pixels[i].g, pixels[i].b)) / 255;
        pixels[i].LerpRGB(sr * v, sg * v, sb * v, w);
      }
    }
  }

  internal static class CompositeOptionValues {
    public static double Value(
      ImmutableDictionary<string, ParameterValue> parameters, string key
    ) => parameters != null && parameters.TryGetValue(
      key, out ParameterValue value) ? value.Value : 0;
  }
}
