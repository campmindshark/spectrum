using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;

namespace Spectrum.LEDs {

  public struct LEDDomeOutputPixel {
    // index by strut and led within that strut
    public int strutIndex;
    public int strutLEDIndex;
    // index by control box and pixel within that control box
    public int controlBoxIndex;
    public int controlBoxPixelIndex;
    // position in projection
    public double x;
    public double y;

    private int _color;
    private double _r;
    private double _g;
    private double _b;
    // Coverage / opacity of this pixel, 0..1, used only by the Over blend in the
    // layer compositor (additive modes and the wire ignore it). A freshly
    // constructed pixel is fully transparent (_a == 0); drawing a color makes it
    // opaque (_a == 1, in the color setter); Fade decays it back toward 0 so
    // faded-out trails stop occluding lower layers under Over.
    private double _a;

    // color
    public int color {
      get { return _color; }
      set {
        _color = value;
        var r = (byte)(_color >> 16);
        var g = (byte)(_color >> 8);
        var b = (byte)_color;
        _r = r;
        _g = g;
        _b = b;
        // Drawing means opaque. Untouched pixels keep _a == 0 (transparent).
        _a = 1;
      }
    }

    public double a {
      get { return _a; }
    }

    // Erase to fully transparent black — distinct from `color = 0`, which is
    // opaque black. Use where a visualizer means "reveal the layers below here"
    // rather than "paint this black".
    public void Clear() {
      _color = 0;
      _r = 0;
      _g = 0;
      _b = 0;
      _a = 0;
    }

    private void updateColor() {
      _color = (ClampByte(_r) << 16) +
        (ClampByte(_g) << 8) +
        ClampByte(_b);
    }

    // Fade() can drive _r/_g/_b out of [0,255] (negative in particular, via
    // "* mul - sub"), and a direct double->byte cast of an out-of-range value is
    // unspecified in C#. Clamp when packing so the wire color is always well
    // defined. The double-precision _r/_g/_b accumulators are intentionally left
    // unclamped so Fade's cross-frame sub-integer accumulation is unchanged.
    private static int ClampByte(double v) {
      if (v <= 0) {
        return 0;
      }
      if (v >= 255) {
        return 255;
      }
      return (int)v;
    }

    public double r {
      get { return _r; }
      set { _r = value; updateColor(); }
    }
    public double g {
      get { return _g; }
      set { _g = value; updateColor(); }
    }
    public double b {
      get { return _b; }
      set { _b = value; updateColor(); }
    }

    // Fade and HueRotate live on the struct (M3/L3) so they mutate _r/_g/_b
    // directly and repack via a single updateColor() per pixel, instead of the
    // three repacks the public r/g/b setters incur (one per component). Called
    // as pixels[i].Fade(...) on an array slot, which mutates in place. They keep
    // operating on the double-precision _r/_g/_b state (not the truncated packed
    // _color) so the sub-integer fade accumulation across frames is unchanged —
    // these are throughput optimizations, not visual changes.

    public void Fade(double mul, double sub) {
      // Nothing to do for a fully transparent black pixel. (The old guard was
      // just "_color == 0"; we also require _a == 0 now so an opaque-but-black
      // pixel's coverage still decays instead of freezing.)
      if (_color == 0 && _a == 0) {
        return;
      }
      _r = _r * mul - sub;
      _g = _g * mul - sub;
      _b = _b * mul - sub;
      // Coverage decays with the same multiplier (floor 0) so a fading trail
      // stops occluding lower layers under Over as it dims. mul is in (0,1) and
      // _a >= 0, so the product stays >= 0.
      _a *= mul;
      updateColor();
    }

    public void HueRotate(double rate) {
      // Black pixels have saturation 0, so the original skipped the write for
      // them anyway (the "if (s != 0)" branch below). Bailing early just avoids
      // the RGB->HSV round-trip for the (common, after a fade) all-black case.
      if (_color == 0) {
        return;
      }

      double r = _r / 255d;
      double g = _g / 255d;
      double b = _b / 255d;

      double max = Math.Max(Math.Max(r, g), b);
      double min = Math.Min(Math.Min(r, g), b);

      double d = max - min;
      double s = max == 0 ? 0 : d / max;
      if (s != 0) {
        double v = max;
        double h = 0;

        if (max != min) {
          if (r > g) {
            if (r > b) {
              h = (g - b) / d + (g < b ? 6 : 0);
            } else {
              h = (r - g) / d + 4;
            }
          } else {
            if (g > b) {
              h = (b - r) / d + 2;
            } else {
              h = (r - g) / d + 4;
            }
          }

          h /= 6;
        }
        double shifted_hue = (h + rate) % 1;
        if (shifted_hue > 1) {
          shifted_hue -= 1;
        }
        if (shifted_hue < 0) {
          shifted_hue += 1;
        }

        int j = (int)Math.Floor(shifted_hue * 6);
        double f = shifted_hue * 6 - j;
        double p = v * (1 - s);
        double q = v * (1 - f * s);
        double t = v * (1 - (1 - f) * s);

        switch (j % 6) {
          case 0: r = v; g = t; b = p; break;
          case 1: r = q; g = v; b = p; break;
          case 2: r = p; g = v; b = t; break;
          case 3: r = p; g = q; b = v; break;
          case 4: r = t; g = p; b = v; break;
          case 5: r = v; g = p; b = q; break;
        }
        _r = r * 255;
        _g = g * 255;
        _b = b * 255;
        updateColor();
      }
    }

    // Compositing (M-series perf note carries over): the layer compositor works
    // on the double-precision _r/_g/_b channels and packs once per pixel via a
    // single updateColor(), clamping only at pack time. src is another pixel of
    // the same buffer shape (index-aligned), so accessing its private channels
    // is fine (same struct type). o is the source layer's opacity, 0..1.

    // Bottom-most active layer: seed this composite pixel from src scaled by
    // opacity. Coverage is carried through (not scaled by opacity) so a
    // subsequent Over layer blends against the base's real alpha.
    public void CompositeCopyScaled(LEDDomeOutputPixel src, double o) {
      _r = src._r * o;
      _g = src._g * o;
      _b = src._b * o;
      _a = src._a;
      updateColor();
    }

    // Blend src (the layer above) into this composite pixel per blend mode. All
    // math is on the 0..255 double channels; see the blend table in the layers
    // design doc. Add/Screen/Lighten/Multiply ignore coverage (black is
    // identity); Over uses src's alpha so a foreground layer only paints where it
    // actually drew (w = o * S.a), and accumulates coverage into the composite.
    public void CompositeBlend(
      LEDDomeOutputPixel src, DomeBlendMode mode, double o
    ) {
      double sr = src._r, sg = src._g, sb = src._b;
      switch (mode) {
        case DomeBlendMode.Desaturate: {
          // An adjustment blend: ignore src's color, use its alpha as a mask,
          // and reprocess the composite below it into grayscale luma. The mask
          // w restricts the effect to where the layer above (the wave) drew.
          double mask = o * src._a;
          if (mask == 0) {
            break;
          }
          double luma = 0.299 * _r + 0.587 * _g + 0.114 * _b;
          _r = luma * mask + _r * (1 - mask);
          _g = luma * mask + _g * (1 - mask);
          _b = luma * mask + _b * (1 - mask);
          break;
        }
        case DomeBlendMode.Add:
          _r += sr * o;
          _g += sg * o;
          _b += sb * o;
          break;
        case DomeBlendMode.Screen:
          _r = 255 - (255 - _r) * (255 - sr * o) / 255;
          _g = 255 - (255 - _g) * (255 - sg * o) / 255;
          _b = 255 - (255 - _b) * (255 - sb * o) / 255;
          break;
        case DomeBlendMode.Lighten:
          _r = Math.Max(_r, sr * o);
          _g = Math.Max(_g, sg * o);
          _b = Math.Max(_b, sb * o);
          break;
        case DomeBlendMode.Multiply:
          _r = _r * (255 - o * (255 - sr)) / 255;
          _g = _g * (255 - o * (255 - sg)) / 255;
          _b = _b * (255 - o * (255 - sb)) / 255;
          break;
        case DomeBlendMode.Over:
        default:
          double w = o * src._a;
          _r = sr * w + _r * (1 - w);
          _g = sg * w + _g * (1 - w);
          _b = sb * w + _b * (1 - w);
          _a = w + _a * (1 - w);
          break;
      }
      updateColor();
    }
  }

  public class LEDDomeOutputBuffer {
    public LEDDomeOutputPixel[] pixels;

    // Maps a strut index to the position in `pixels` of that strut's LED 0.
    // MakeDomeOutputBuffer lays every strut's LEDs down contiguously in
    // ascending strutLEDIndex order, so pixel (strut, led) lives at
    // strutStartIndex[strut] + led. This lets strut-addressed visualizers write
    // through the buffer (and WriteBuffer) instead of the per-pixel dome.SetPixel
    // path. The mapping depends only on strut/LED identity (not the cable
    // permutation), so it survives RebakeBuffer unchanged.
    private readonly int[] strutStartIndex;

    public LEDDomeOutputBuffer(LEDDomeOutputPixel[] pixels) {
      this.pixels = pixels;
      int maxStrut = -1;
      for (int i = 0; i < pixels.Length; i++) {
        if (pixels[i].strutIndex > maxStrut) {
          maxStrut = pixels[i].strutIndex;
        }
      }
      this.strutStartIndex = new int[maxStrut + 1];
      for (int i = 0; i < pixels.Length; i++) {
        if (pixels[i].strutLEDIndex == 0) {
          this.strutStartIndex[pixels[i].strutIndex] = i;
        }
      }
    }

    // Writes a pixel addressed by strut and LED-within-strut — the same
    // addressing the legacy LEDDomeOutput.SetPixel path used — so strut-based
    // visualizers can render into the buffer and flush it once via WriteBuffer.
    public void SetPixel(int strutIndex, int ledIndex, int color) {
      this.pixels[this.strutStartIndex[strutIndex] + ledIndex].color = color;
    }

    // Erase a strut-addressed pixel to fully transparent black (see
    // LEDDomeOutputPixel.Clear) — "reveal below", not "paint black".
    public void ClearPixel(int strutIndex, int ledIndex) {
      this.pixels[this.strutStartIndex[strutIndex] + ledIndex].Clear();
    }

    // Bakes the static unit-sphere position of every pixel: maps each pixel's
    // normalized projected (x, y) — which come "out of" the top-left corner, so
    // y is flipped — onto the unit hemisphere. z is guarded against the
    // x² + y² > 1 case (a pixel projecting outside the disc) by flattening it to
    // 0 instead of taking the square root of a negative. The orientation-driven
    // layers cache this array once at construction rather than recomputing it
    // per frame.
    public Vector3[] BakePixelPositions() {
      var positions = new Vector3[this.pixels.Length];
      for (int i = 0; i < this.pixels.Length; i++) {
        var p = this.pixels[i];
        float x = (float)(2 * p.x - 1);
        float y = (float)(1 - 2 * p.y);
        float z = (x * x + y * y) > 1 ? 0 : (float)Math.Sqrt(1 - x * x - y * y);
        positions[i] = new Vector3(x, y, z);
      }
      return positions;
    }

    public void Fade(double mul, double sub) {
      for (int i = 0; i < pixels.Length; i++) {
        pixels[i].Fade(mul, sub);
      }
    }

    public void HueRotate(double rate) {
      for (int i = 0; i < pixels.Length; i++) {
        pixels[i].HueRotate(rate);
      }
    }

    // Seed this composite buffer from a bottom layer scaled by opacity.
    public void CompositeBottom(LEDDomeOutputBuffer src, double opacity) {
      for (int i = 0; i < pixels.Length; i++) {
        pixels[i].CompositeCopyScaled(src.pixels[i], opacity);
      }
    }

    // Blend an upper layer into this composite buffer.
    public void CompositeBlend(
      LEDDomeOutputBuffer src, DomeBlendMode mode, double opacity
    ) {
      for (int i = 0; i < pixels.Length; i++) {
        pixels[i].CompositeBlend(src.pixels[i], mode, opacity);
      }
    }
  }
}
