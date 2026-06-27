using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
      }
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
      // Matches the old "if (color != 0)" guard; black pixels stay black.
      if (_color == 0) {
        return;
      }
      _r = _r * mul - sub;
      _g = _g * mul - sub;
      _b = _b * mul - sub;
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
  }

  public class LEDDomeOutputBuffer {
    public LEDDomeOutputPixel[] pixels;

    public LEDDomeOutputBuffer(LEDDomeOutputPixel[] pixels) {
      this.pixels = pixels;
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
  }
}
