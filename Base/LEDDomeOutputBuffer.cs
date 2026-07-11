using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Spectrum.Base {

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

    // 0..1 hue defined independently of the visible color/alpha, so a
    // visualizer can publish "what hue is at this point in the field" every
    // frame even where it draws nothing (e.g. Metaball outside its potential
    // threshold). Not touched by Fade/Clear/color — a future hue-inherit
    // blend mode reads it directly from the source layer's buffer rather
    // than deriving hue from src's (possibly black/transparent) RGB.
    public double hue;

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
      hue = src.hue;
      updateColor();
    }

    // ---- Generic single-repack channel ops for the blend classes ----------
    // The per-blend math itself lives in the DomeBlend implementations
    // (DomeBlendModes.cs / DomePrismBlends.cs); these are the only ways a blend
    // mutates a composite pixel. Each op touches the double-precision channels
    // directly and repacks via a single updateColor(), like Fade — using the
    // public r/g/b setters instead would repack three times per pixel.

    // Overwrite the color channels (coverage and hue untouched).
    public void SetRGB(double r, double g, double b) {
      _r = r;
      _g = g;
      _b = b;
      updateColor();
    }

    // Accumulate onto the color channels (Add / EdgeSpectrum style).
    public void AddRGB(double dr, double dg, double db) {
      _r += dr;
      _g += dg;
      _b += db;
      updateColor();
    }

    // Lerp the color channels toward (tr, tg, tb) by weight w (coverage and hue
    // untouched) — the masked-adjustment shape shared by Desaturate, Hue and the
    // prism blends.
    public void LerpRGB(double tr, double tg, double tb, double w) {
      _r = tr * w + _r * (1 - w);
      _g = tg * w + _g * (1 - w);
      _b = tb * w + _b * (1 - w);
      updateColor();
    }

    // Lerp color and coverage toward (tr, tg, tb, ta) by weight w — the Over
    // shape, where coverage also accumulates (ta = 1 lerps _a toward opaque).
    public void LerpRGBA(double tr, double tg, double tb, double ta, double w) {
      _r = tr * w + _r * (1 - w);
      _g = tg * w + _g * (1 - w);
      _b = tb * w + _b * (1 - w);
      _a = ta * w + _a * (1 - w);
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

    // Baked spatial neighbor table (docs/prism.md), used by the spatial prism
    // blends to resample the composite at an offset. For each pixel we store the
    // nearest pixel to (x + r·cosθ, y + r·sinθ) over NeighborDirections evenly
    // spaced angles and NeighborRadii radius steps, flattened as
    // (pixel * NeighborRadii + radiusBin) * NeighborDirections + dirBin. Built
    // lazily (EnsureNeighborTable) only on the buffer a spatial blend actually
    // runs on — the composite buffer — so layer buffers never pay for it. Like
    // strutStartIndex it depends only on the baked x/y geometry, so it survives
    // RebakeBuffer (which only re-derives device indexes) unchanged. A tap that
    // finds no pixel near its target stores the pixel's own index, degrading to
    // an in-place read rather than smearing across a gap in the layout.
    public const int NeighborDirections = 16;
    public const int NeighborRadii = 4;
    // Projected-plane units between radius steps; a dome LED pitch is ~0.013, so
    // the four steps span ~1.5–6 LED pitches.
    public const double NeighborRadiusStep = 0.02;
    private int[] neighborTable;

    // Baked unit-sphere normals (BakePixelPositions), cached lazily for the
    // Iridescence blend the same way the orientation layers cache them.
    private Vector3[] normals;

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

    // ---- Spatial-sampling infrastructure for the prism blends -------------
    // (docs/prism.md) The blends themselves are DomeBlend classes; the baked
    // neighbor table and normals stay here because they belong to this buffer's
    // geometry, not to any one blend.

    // Copy every pixel from `other` into this buffer. Used to snapshot the
    // composite before a spatial blend, since those read neighbors of the buffer
    // they mutate and must read the pre-pass state. LEDDomeOutputPixel is a
    // struct, so this copies values, not references.
    public void CopyFrom(LEDDomeOutputBuffer other) {
      Array.Copy(other.pixels, this.pixels, this.pixels.Length);
    }

    // Nearest dir-bin for an arbitrary angle (radians).
    public static int DirBin(double angle) {
      double turns = angle / (2 * Math.PI);
      turns -= Math.Floor(turns);
      int bin = (int)Math.Round(turns * NeighborDirections) % NeighborDirections;
      return bin;
    }

    // Nearest radius-bin for a distance in projected-plane units.
    public static int RadiusBin(double distance) {
      int bin = (int)Math.Round(distance / NeighborRadiusStep) - 1;
      if (bin < 0) {
        return 0;
      }
      if (bin >= NeighborRadii) {
        return NeighborRadii - 1;
      }
      return bin;
    }

    // The baked neighbor of `pixel` in direction bin `dirBin` at radius bin
    // `radiusBin` (its own index if the tap found nothing near its target).
    public int NeighborAt(int pixel, int dirBin, int radiusBin) {
      return this.neighborTable[
        (pixel * NeighborRadii + radiusBin) * NeighborDirections + dirBin];
    }

    // Build neighborTable once from the baked x/y positions. Uses a uniform
    // spatial grid (cell = the max tap radius) so each nearest lookup only scans
    // the 3×3 block of cells around its target — the block is guaranteed to
    // contain any pixel within the max radius. Cheap enough to run on the
    // operator thread the first frame a spatial blend appears; a no-op after.
    // Public so the spatial DomeBlend classes can bake before sampling.
    public void EnsureNeighborTable() {
      if (this.neighborTable != null) {
        return;
      }
      int n = this.pixels.Length;
      double cell = NeighborRadii * NeighborRadiusStep;
      // The nearest real pixel must fall within maxMatch of a tap's target for
      // the tap to count; otherwise the tap reads the pixel in place. Keeps
      // fringes from jumping across gaps or grabbing rim pixels for off-dome
      // targets.
      double maxMatch = 1.5 * NeighborRadiusStep;
      double maxMatchSq = maxMatch * maxMatch;

      double minX = double.MaxValue, minY = double.MaxValue;
      double maxX = double.MinValue, maxY = double.MinValue;
      for (int i = 0; i < n; i++) {
        double x = this.pixels[i].x, y = this.pixels[i].y;
        if (x < minX) { minX = x; }
        if (y < minY) { minY = y; }
        if (x > maxX) { maxX = x; }
        if (y > maxY) { maxY = y; }
      }
      int cols = Math.Max(1, (int)((maxX - minX) / cell) + 1);
      int rows = Math.Max(1, (int)((maxY - minY) / cell) + 1);
      // Bucket every pixel into its grid cell (counting-sort layout: one flat
      // index array + per-cell start offsets, so no per-cell List allocations).
      int cellCount = cols * rows;
      int[] cellOf = new int[n];
      int[] counts = new int[cellCount + 1];
      for (int i = 0; i < n; i++) {
        int cx = (int)((this.pixels[i].x - minX) / cell);
        int cy = (int)((this.pixels[i].y - minY) / cell);
        if (cx >= cols) { cx = cols - 1; }
        if (cy >= rows) { cy = rows - 1; }
        int c = cy * cols + cx;
        cellOf[i] = c;
        counts[c + 1]++;
      }
      for (int c = 0; c < cellCount; c++) {
        counts[c + 1] += counts[c];
      }
      int[] cellStart = (int[])counts.Clone();
      int[] byCell = new int[n];
      int[] fill = (int[])cellStart.Clone();
      for (int i = 0; i < n; i++) {
        byCell[fill[cellOf[i]]++] = i;
      }

      var table = new int[n * NeighborRadii * NeighborDirections];
      for (int i = 0; i < n; i++) {
        double px = this.pixels[i].x, py = this.pixels[i].y;
        for (int r = 0; r < NeighborRadii; r++) {
          double radius = (r + 1) * NeighborRadiusStep;
          for (int d = 0; d < NeighborDirections; d++) {
            double theta = 2 * Math.PI * d / NeighborDirections;
            double tx = px + radius * Math.Cos(theta);
            double ty = py + radius * Math.Sin(theta);
            int best = i;
            double bestSq = maxMatchSq;
            int tcx = (int)((tx - minX) / cell);
            int tcy = (int)((ty - minY) / cell);
            for (int gy = tcy - 1; gy <= tcy + 1; gy++) {
              if (gy < 0 || gy >= rows) { continue; }
              for (int gx = tcx - 1; gx <= tcx + 1; gx++) {
                if (gx < 0 || gx >= cols) { continue; }
                int c = gy * cols + gx;
                for (int k = cellStart[c]; k < cellStart[c + 1]; k++) {
                  int j = byCell[k];
                  double dx = this.pixels[j].x - tx;
                  double dy = this.pixels[j].y - ty;
                  double dsq = dx * dx + dy * dy;
                  if (dsq < bestSq) {
                    bestSq = dsq;
                    best = j;
                  }
                }
              }
            }
            table[(i * NeighborRadii + r) * NeighborDirections + d] = best;
          }
        }
      }
      this.neighborTable = table;
    }

    // Baked unit-sphere normals, lazily cached for the Iridescence blend the
    // same way the orientation layers cache BakePixelPositions().
    public Vector3[] Normals {
      get {
        if (this.normals == null) {
          this.normals = this.BakePixelPositions();
        }
        return this.normals;
      }
    }
  }
}
