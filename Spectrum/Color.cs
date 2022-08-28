using System;

namespace Spectrum {
  internal class Color {

    public byte R;
    public byte G;
    public byte B;
    public double H; // Hue - [0, 1] from Red to Green to Blue back to Red (periodic)
    public double S; // Saturation - [0, 1] (grayness)
    public double V; // Value - [0, 1] (brightness)

    public Color(byte _r, byte _g, byte _b) {
      R = _r;
      G = _g;
      B = _b;

      double r = _r / 255d;
      double g = _g / 255d;
      double b = _b / 255d;

      double max = Math.Max(Math.Max(r, g), b);
      double min = Math.Min(Math.Min(r, g), b);
      
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
      double hueDiff = HueDiff(a.H, b.H);
      double hueDir = HueDir(a.H, b.H);
      return new Color(
        Wrap(hueDir * hueDiff * alpha + a.H, 0, 1),
        ((b.S - a.S) * alpha + a.S),
        ((b.V - a.V) * alpha + a.V)
        );
    }
    private static double Clamp(double x, double a, double b) {
      if (x < a) return a;
      if (x > b) return b;
      return x;
    }
    private static double Wrap(double x, double a, double b) {
      var range = b - a;
      while (x < a) x += range;
      while (x > b) x -= range;
      return x;
    }
    // Hue values are in [0, 1] and wrap around a circle - 0 and 1 both represent red
    // Shortest value and direction between two hue values
    private static double HueDiff(double a, double b) {
      return Math.Min(Math.Min(Math.Abs(b - a), Math.Abs(b - a - 1)), Math.Abs(b - a + 1));
    }
    private static int HueDir(double a, double b) {
      // Ordinary case
      if (b > a) {
        if (Math.Abs(b - a) > .5) {
          return -1;
        } else {
          return 1;
        }
      // Reflected case
      } else {
        if (Math.Abs(b - a) > .5) {
          return 1;
        } else {
          return -1;
        }
      }
    }
    // Pixel blending modes
    // Add: just direct RGB addition
    public static Color BlendAdd(double alpha, Color a, Color b) {
      return new Color(
        ToByte(a.R + alpha * b.R), 
        ToByte(a.G + alpha * b.G), 
        ToByte(a.B + alpha * b.B));
    }
    // Lighten: brighten the original color by the V value of the second
    public static Color BlendLighten(double alpha, Color a, Color b) {
      return new Color(a.H, a.S, Clamp(a.V + alpha * b.V, 0, 1));
    }
    // Lighten2: apply the new color but limit its brightness by existing color
    public static Color BlendLighten2(double alpha, Color a, Color b) {
      return new Color(b.H, b.S, Clamp(a.V + alpha * b.V, 0, 1));
    }
    // Darken: darken the original color by the V value of the second
    public static Color BlendDarken(double alpha, Color a, Color b) {
      return new Color(a.H, a.S, Clamp(a.V - alpha * b.V, 0, 1));
    }
    // Hue: set the hue of the first color to the hue of the second
    public static Color BlendHue(double alpha, Color a, Color b) {
      double hueDiff = HueDiff(a.H, b.H);
      double hueDir = HueDir(a.H, b.H);

      return new Color(Wrap(hueDir * hueDiff * alpha + a.H, 0, 1), a.S, a.V);
    }
    // Saturate: set the saturation of the first color to the hue of the second
    public static Color BlendSaturation(double alpha, Color a, Color b) {
      return new Color(a.H, (b.S - a.S) * alpha + a.S, a.V);
    }
    // Value: set the value of the first color to the value of the second
    public static Color BlendValue(double alpha, Color a, Color b) {
      return new Color(a.H, a.S, (b.V - a.V) * alpha + a.V);
    }
    // Background: set to the new color only if the old color is black
    public static Color BlendBackground(Color a, Color b) {
      if (a.V != 0) {
        return a;
      } else {
        return b;
      }
    }

  }
}
