using System;
using System.Collections.Generic;
using System.Numerics;

namespace Spectrum.Base {

  // The prism family (docs/prism.md): adjustment blends (source alpha is the
  // mask) whose tunables live in each class's Params — the value rides the
  // Params bag of whichever layer selects the blend, and only the compositor
  // reads it, never a visualizer. Offsets are in projected-plane units (the
  // same normalized x/y the buffer bakes), which is why the defaults look
  // small: a dome LED pitch is ~0.013, and fringes want a few pitches to read
  // at dome scale.
  //
  // ChromaticFringe and EdgeSpectrum are *spatial* — they resample the
  // composite below through the baked neighbor table on LEDDomeOutputBuffer,
  // so they declare NeedsSnapshot and read the compositor's pre-pass copy.
  // Iridescence is per-pixel but keyed to the baked unit-sphere normals.

  // RGB channel-split aberration: R is pulled from +offset along the split
  // axis, B from the opposite offset, G stays in place. offset = how far apart
  // the R and B images land; spin = rotate the split axis over time
  // (turns/sec, signed; 0 holds a fixed axis); follow = drive the axis from
  // the spotlighted wand's orientation instead, in which case spin is
  // disregarded.
  internal sealed class ChromaticFringeBlend : DomeBlend {
    public override string Name => "ChromaticFringe";
    public override bool NeedsSnapshot => true;
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

    public override void Blend(in DomeBlendContext ctx) {
      double angle = ctx.PrismAngle(
        this.Param(ctx.Settings, "spin"),
        this.Param(ctx.Settings, "follow") != 0);
      int radiusBin = LEDDomeOutputBuffer.RadiusBin(
        this.Param(ctx.Settings, "offset"));
      LEDDomeOutputBuffer dest = ctx.Dest;
      dest.EnsureNeighborTable();
      int fwd = LEDDomeOutputBuffer.DirBin(angle);
      int back = (fwd + LEDDomeOutputBuffer.NeighborDirections / 2)
        % LEDDomeOutputBuffer.NeighborDirections;
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
    public override bool NeedsSnapshot => true;
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

    public override void Blend(in DomeBlendContext ctx) {
      double strength = this.Param(ctx.Settings, "strength");
      int radiusBin = LEDDomeOutputBuffer.RadiusBin(
        this.Param(ctx.Settings, "offset"));
      LEDDomeOutputBuffer dest = ctx.Dest;
      dest.EnsureNeighborTable();
      int right = LEDDomeOutputBuffer.DirBin(0);
      int left = LEDDomeOutputBuffer.DirBin(Math.PI);
      int up = LEDDomeOutputBuffer.DirBin(Math.PI / 2);
      int down = LEDDomeOutputBuffer.DirBin(3 * Math.PI / 2);
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

    public override void Blend(in DomeBlendContext ctx) {
      double strength = this.Param(ctx.Settings, "strength");
      double bands = this.Param(ctx.Settings, "bands");
      double azim = ctx.PrismAngle(
        this.Param(ctx.Settings, "spin"),
        this.Param(ctx.Settings, "follow") != 0);
      // Virtual light swept in azimuth, held at a fixed elevation so the
      // sheen bands read as horizontal-ish arcs across the dome.
      Vector3 light = Vector3.Normalize(new Vector3(
        (float)Math.Cos(azim), (float)Math.Sin(azim), 0.6f));
      Vector3[] normals = ctx.Dest.Normals;
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
}
