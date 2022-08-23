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
      _color = (int)(((byte)_r) << 16) +
        (int)(((byte)_g) << 8) +
        (int)(((byte)_b));
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
  }

  public class LEDDomeOutputBuffer {
    public LEDDomeOutputPixel[] pixels;

    public LEDDomeOutputBuffer(LEDDomeOutputPixel[] pixels) {
      this.pixels = pixels;
    }

    public void Fade(double mul, double sub) {
      for (int i = 0; i < pixels.Length; i++) {
        if (pixels[i].color != 0) {
          pixels[i].r = pixels[i].r * mul - sub;
          pixels[i].g = pixels[i].g * mul - sub;
          pixels[i].b = pixels[i].b * mul - sub;
        }
      }
    }

    public void HueRotate(double rate) {
      for (int i = 0; i < pixels.Length; i++) {
        double r = pixels[i].r / 255d;
        double g = pixels[i].g / 255d;
        double b = pixels[i].b / 255d;

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
          pixels[i].r = r * 255;
          pixels[i].g = g * 255;
          pixels[i].b = b * 255;
        }
      }
    }
  }
}
