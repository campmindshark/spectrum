using System;

namespace Spectrum.Base {

  // The plain paint and adjustment blends. All math is on the 0..255 double
  // channels via the single-repack ops on LEDDomeOutputPixel. The paint blends
  // (Over/Add/Screen/Lighten/Multiply) forward src's published hue to the
  // composite so a hue-publishing layer (e.g. Metaball) can feed a Hue layer
  // above; the adjustment blends (Desaturate/Hue) leave the carried hue alone.

  // Src's alpha is its coverage: a foreground layer only paints where it
  // actually drew (w = opacity * src.a), and coverage accumulates into the
  // composite so a subsequent Over layer blends against the real alpha.
  internal sealed class OverBlend : DomeBlend {
    public override string Name => "Over";
    public override void Blend(in DomeBlendContext ctx) {
      LEDDomeOutputPixel[] dest = ctx.Dest.pixels;
      LEDDomeOutputPixel[] src = ctx.Src.pixels;
      double o = ctx.Opacity;
      for (int i = 0; i < dest.Length; i++) {
        double w = o * src[i].a;
        dest[i].LerpRGBA(src[i].r, src[i].g, src[i].b, 1, w);
        dest[i].hue = src[i].hue;
      }
    }
  }

  // The additive blends ignore coverage — black is their identity.
  internal sealed class AddBlend : DomeBlend {
    public override string Name => "Add";
    public override void Blend(in DomeBlendContext ctx) {
      LEDDomeOutputPixel[] dest = ctx.Dest.pixels;
      LEDDomeOutputPixel[] src = ctx.Src.pixels;
      double o = ctx.Opacity;
      for (int i = 0; i < dest.Length; i++) {
        dest[i].AddRGB(src[i].r * o, src[i].g * o, src[i].b * o);
        dest[i].hue = src[i].hue;
      }
    }
  }

  internal sealed class ScreenBlend : DomeBlend {
    public override string Name => "Screen";
    public override void Blend(in DomeBlendContext ctx) {
      LEDDomeOutputPixel[] dest = ctx.Dest.pixels;
      LEDDomeOutputPixel[] src = ctx.Src.pixels;
      double o = ctx.Opacity;
      for (int i = 0; i < dest.Length; i++) {
        dest[i].SetRGB(
          255 - (255 - dest[i].r) * (255 - src[i].r * o) / 255,
          255 - (255 - dest[i].g) * (255 - src[i].g * o) / 255,
          255 - (255 - dest[i].b) * (255 - src[i].b * o) / 255);
        dest[i].hue = src[i].hue;
      }
    }
  }

  internal sealed class LightenBlend : DomeBlend {
    public override string Name => "Lighten";
    public override void Blend(in DomeBlendContext ctx) {
      LEDDomeOutputPixel[] dest = ctx.Dest.pixels;
      LEDDomeOutputPixel[] src = ctx.Src.pixels;
      double o = ctx.Opacity;
      for (int i = 0; i < dest.Length; i++) {
        dest[i].SetRGB(
          Math.Max(dest[i].r, src[i].r * o),
          Math.Max(dest[i].g, src[i].g * o),
          Math.Max(dest[i].b, src[i].b * o));
        dest[i].hue = src[i].hue;
      }
    }
  }

  // Opacity lerps toward white (not black) so o = 0 is the identity.
  internal sealed class MultiplyBlend : DomeBlend {
    public override string Name => "Multiply";
    public override void Blend(in DomeBlendContext ctx) {
      LEDDomeOutputPixel[] dest = ctx.Dest.pixels;
      LEDDomeOutputPixel[] src = ctx.Src.pixels;
      double o = ctx.Opacity;
      for (int i = 0; i < dest.Length; i++) {
        dest[i].SetRGB(
          dest[i].r * (255 - o * (255 - src[i].r)) / 255,
          dest[i].g * (255 - o * (255 - src[i].g)) / 255,
          dest[i].b * (255 - o * (255 - src[i].b)) / 255);
        dest[i].hue = src[i].hue;
      }
    }
  }

  // An adjustment blend: ignore src's color, use its alpha as a mask, and
  // reprocess the composite below it into grayscale luma. The mask restricts
  // the effect to where the layer above (e.g. a wave) drew.
  internal sealed class DesaturateBlend : DomeBlend {
    public override string Name => "Desaturate";
    public override void Blend(in DomeBlendContext ctx) {
      LEDDomeOutputPixel[] dest = ctx.Dest.pixels;
      LEDDomeOutputPixel[] src = ctx.Src.pixels;
      double o = ctx.Opacity;
      for (int i = 0; i < dest.Length; i++) {
        double mask = o * src[i].a;
        if (mask == 0) {
          continue;
        }
        double luma =
          0.299 * dest[i].r + 0.587 * dest[i].g + 0.114 * dest[i].b;
        dest[i].LerpRGB(luma, luma, luma, mask);
      }
    }
  }

  // The other adjustment blend: src acts as a pure brightness mask (its own
  // value, from its own rendered color — e.g. Background's flat fill, or
  // Wave's painted brightness pattern), masked by its own alpha like Over,
  // fully recolored at max saturation using the composite's carried hue — the
  // hue forwarded up from whatever hue-publishing layer sits further below
  // (e.g. Metaball's dedicated `hue` field), as long as an intervening paint
  // blend hasn't overwritten it with its own src hue.
  //
  // Deliberately ignores src's own saturation rather than doing a "true" HSV
  // Hue blend (S and V both from src): a Photoshop-style Hue blend has no
  // visible effect against an achromatic src (e.g. Background's default white
  // fill — there's no chroma to redirect), which defeats the point here.
  // Forcing full saturation means any brightness-only src becomes a pure
  // canvas for the carried hue.
  internal sealed class HueBlend : DomeBlend {
    public override string Name => "Hue";
    public override void Blend(in DomeBlendContext ctx) {
      LEDDomeOutputPixel[] dest = ctx.Dest.pixels;
      LEDDomeOutputPixel[] src = ctx.Src.pixels;
      double o = ctx.Opacity;
      for (int i = 0; i < dest.Length; i++) {
        double mask = o * src[i].a;
        if (mask == 0) {
          continue;
        }
        // src's brightness (HSV value) is just its max channel.
        double v = Math.Max(src[i].r, Math.Max(src[i].g, src[i].b)) / 255;
        HSVToRGB(dest[i].hue, 1, v,
          out double nr, out double ng, out double nb);
        dest[i].LerpRGB(nr, ng, nb, mask);
      }
    }

    // Full-saturation HSV -> 0..255 RGB (kept separate from the pixel's
    // in-place HueRotate, which early-outs on black and never leaves the
    // struct).
    private static void HSVToRGB(
      double h, double s, double v,
      out double r255, out double g255, out double b255
    ) {
      int i = (int)Math.Floor(h * 6);
      double f = h * 6 - i;
      double p = v * (1 - s);
      double q = v * (1 - f * s);
      double t = v * (1 - (1 - f) * s);
      double r = 0, g = 0, b = 0;
      switch (((i % 6) + 6) % 6) {
        case 0: r = v; g = t; b = p; break;
        case 1: r = q; g = v; b = p; break;
        case 2: r = p; g = v; b = t; break;
        case 3: r = p; g = q; b = v; break;
        case 4: r = t; g = p; b = v; break;
        case 5: r = v; g = p; b = q; break;
      }
      r255 = r * 255;
      g255 = g * 255;
      b255 = b * 255;
    }
  }
}
