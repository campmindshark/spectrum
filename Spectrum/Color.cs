using System;

namespace Spectrum {
  internal class Color {

    public byte R;
    public byte G;
    public byte B;
    public double H;
    public double S;
    public double V;

    public Color(byte r, byte g, byte b) {
      R = r;
      G = g;
      B = b;
      double max = Math.Max(Math.Max(r / 255.0d, g / 255.0d), b / 255.0d);
      double min = Math.Max(Math.Max(r / 255.0d, g / 255.0d), b / 255.0d);
      
      double d = max - min;
      double s = max == 0 ? 0 : d / max;
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
      H = h;
      S = s;
      V = v;
    }

    public Color(double h, double s, double v) {
      H = h;
      S = s;
      V = v;
      double r = 0, g = 0, b = 0;

      int i = (int)Math.Floor(h * 6);
      double f = h * 6 - i;
      double p = v * (1 - s);
      double q = v * (1 - f * s);
      double t = v * (1 - (1 - f) * s);

      switch (i % 6) {
        case 0: r = v; g = t; b = p; break;
        case 1: r = q; g = v; b = p; break;
        case 2: r = p; g = v; b = t; break;
        case 3: r = p; g = q; b = v; break;
        case 4: r = t; g = p; b = v; break;
        case 5: r = v; g = p; b = q; break;
      }

      R = (byte)(255 * r);
      G = (byte)(255 * g);
      B = (byte)(255 * b);
    }

    public override string ToString() {
      return $"0x{R:x2}{G:x2}{B:x2}";
    }

    public int ToInt() {
      return 256*256*(int)R + 256*(int)G + (int)B;
    }

    private static byte ToByte(double x) {
      return (byte)Math.Min(Math.Max(Math.Round(x), 0), 255);
    }

    public static Color BlendRGB(double alpha, Color a, Color b) {
      return new Color(
          ToByte((b.R - a.R) * alpha + a.R),
          ToByte((b.G - a.G) * alpha + a.G),
          ToByte((b.B - a.B) * alpha + a.B)
          );
    }
    public static Color BlendHSV(double alpha, Color a, Color b) {
      return new Color(
        ((b.H - a.H) * alpha + a.H),
        ((b.S - a.S) * alpha + a.S),
        ((b.V - a.V) * alpha + a.V)
        );
    }
  }
}
