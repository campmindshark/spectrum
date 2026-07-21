using System;

namespace Spectrum {

  // Stateless range-remap and predicate helpers shared across the dome layer
  // visualizers. They live in the Spectrum namespace (which encloses
  // Spectrum.Visualizers), so a `using static Spectrum.MathUtil;` lets callers
  // in either namespace keep their original unqualified call sites.
  static class MathUtil {

    // Map value x from range a-b to range c-d.
    public static double Map(double x, double a, double b, double c, double d) {
      return (x - a) * (d - c) / (b - a) + c;
    }

    // Map value x from range a-b to range c-d, clamping results outside c-d to
    // c or d.
    public static double MapClamp(double x, double a, double b, double c, double d) {
      return Math.Clamp(Map(x, a, b, c, d), c, d);
    }

    // Map value x from range a-b to range c-d, wrapping results outside c-d
    // around the range. Example: mapping to 0-10 but getting 11.3 wraps to 1.3.
    public static double MapWrap(double x, double a, double b, double c, double d) {
      return Wrap(Map(x, a, b, c, d), c, d);
    }

    // Wrap value x around range a-b. Example: 2.5 wrapped to 0-1 becomes 0.5.
    public static double Wrap(double x, double a, double b) {
      var range = b - a;
      while (x < a) x += range;
      while (x > b) x -= range;
      return x;
    }

    // Closed-interval membership test: a <= x <= b.
    public static bool Between(double x, double a, double b) {
      return x >= a && x <= b;
    }

    // Whether x and y are within tolerance of each other.
    public static bool CloseTo(double x, double y, double tolerance) {
      return Math.Abs(x - y) < tolerance;
    }

    // V channel (max of RGB, normalized) of a packed color, matching Color.V.
    // Kept as a standalone helper rather than `new Color(color).V` because it
    // runs per pixel in the paint loop, where allocating a Color (which also
    // computes the unused H and S) would show up.
    public static double ValueFromInt(int color) {
      return MaxChannelFromInt(color) / 255d;
    }

    // Highest packed RGB channel, which is the HSV value component before
    // normalization. This lets hot light-paint paths compare brightness
    // without decoding a heap-allocated Color.
    public static int MaxChannelFromInt(int color) {
      int r = (color >> 16) & 0xFF;
      int g = (color >> 8) & 0xFF;
      int b = color & 0xFF;
      return Math.Max(r, Math.Max(g, b));
    }

    // Allocation-free equivalent of Color(double h, double s, double v).ToInt().
    // Keep the sector calculation, modulo behavior, and byte truncation in
    // lockstep with Color so wrapped hues and boundary values render exactly as
    // they did before this helper was introduced.
    public static int HsvToInt(double h, double s, double v) {
      double r = 0;
      double g = 0;
      double b = 0;

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

      int red = (byte)(255 * r);
      int green = (byte)(255 * g);
      int blue = (byte)(255 * b);
      return (red << 16) | (green << 8) | blue;
    }

    // Allocation-free packed RGB to HSV conversion matching Color(int). This
    // is used when a render loop needs to vary the value of a palette color.
    public static void HsvFromInt(
      int color, out double h, out double s, out double v
    ) {
      double r = ((color >> 16) & 0xFF) / 255d;
      double g = ((color >> 8) & 0xFF) / 255d;
      double b = (color & 0xFF) / 255d;

      double max = Math.Max(Math.Max(r, g), b);
      double min = Math.Min(Math.Min(r, g), b);
      double d = max - min;
      s = max == 0 ? 0 : d / max;
      v = max;
      h = 0;

      if (max == min) {
        return;
      }
      if (r > g) {
        if (r > b) {
          h = (g - b) / d + (g < b ? 6 : 0);
        } else {
          h = (r - g) / d + 4;
        }
      } else if (g > b) {
        h = (b - r) / d + 2;
      } else {
        h = (r - g) / d + 4;
      }
      h /= 6;
    }
  }
}
