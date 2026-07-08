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
      int r = (color >> 16) & 0xFF;
      int g = (color >> 8) & 0xFF;
      int b = color & 0xFF;
      int max = Math.Max(r, Math.Max(g, b));
      return max / 255d;
    }
  }
}
