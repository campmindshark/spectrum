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
        case DomeBlendMode.Hue: {
          // The other adjustment blend: src acts as a pure brightness mask
          // (its own value, from its own rendered color — e.g. Background's
          // flat fill, or Wave's painted brightness pattern), masked by its
          // own alpha like Over, fully recolored at max saturation using
          // `hue` — the hue carried up from whatever hue-publishing layer
          // sits further below (e.g. Metaball's dedicated `hue` field,
          // forwarded here as long as an intervening paint mode hasn't
          // overwritten it with its own src.hue below).
          //
          // Deliberately ignores src's own saturation rather than doing a
          // "true" HSV Hue blend (S and V both from src): a Photoshop-style
          // Hue blend has no visible effect against an achromatic src (e.g.
          // Background's default white fill, s == 0 — there's no chroma to
          // redirect), which defeats the point here. Forcing full saturation
          // means any brightness-only src becomes a pure canvas for the
          // carried hue.
          double mask = o * src._a;
          if (mask == 0) {
            break;
          }
          RGBToHSV(sr, sg, sb, out _, out _, out double v);
          HSVToRGB(hue, 1, v, out double nr, out double ng, out double nb);
          _r = nr * mask + _r * (1 - mask);
          _g = ng * mask + _g * (1 - mask);
          _b = nb * mask + _b * (1 - mask);
          break;
        }
        case DomeBlendMode.Add:
          _r += sr * o;
          _g += sg * o;
          _b += sb * o;
          hue = src.hue;
          break;
        case DomeBlendMode.Screen:
          _r = 255 - (255 - _r) * (255 - sr * o) / 255;
          _g = 255 - (255 - _g) * (255 - sg * o) / 255;
          _b = 255 - (255 - _b) * (255 - sb * o) / 255;
          hue = src.hue;
          break;
        case DomeBlendMode.Lighten:
          _r = Math.Max(_r, sr * o);
          _g = Math.Max(_g, sg * o);
          _b = Math.Max(_b, sb * o);
          hue = src.hue;
          break;
        case DomeBlendMode.Multiply:
          _r = _r * (255 - o * (255 - sr)) / 255;
          _g = _g * (255 - o * (255 - sg)) / 255;
          _b = _b * (255 - o * (255 - sb)) / 255;
          hue = src.hue;
          break;
        case DomeBlendMode.Over:
        default:
          double w = o * src._a;
          _r = sr * w + _r * (1 - w);
          _g = sg * w + _g * (1 - w);
          _b = sb * w + _b * (1 - w);
          _a = w + _a * (1 - w);
          hue = src.hue;
          break;
      }
      updateColor();
    }

    // ---- Prism-family per-pixel steps (docs/prism.md) --------------------
    // Live on the struct like Fade/CompositeBlend so they mutate _r/_g/_b
    // directly and repack once. The buffer-level methods below do the spatial
    // sampling / geometry (they own the neighbor table and normals) and hand the
    // already-resolved neighbor values in here.

    // ChromaticFringe: replace this composite pixel's R with the R sampled from
    // one spatial offset and its B with the B from the opposite offset (G stays
    // in place), faded in by `mask` (o * src alpha). srcR/srcB come from the
    // pre-pass snapshot so the split never smears order-dependently along the
    // pixel array.
    public void ApplyChromaticFringe(double srcR, double srcB, double mask) {
      _r = srcR * mask + _r * (1 - mask);
      _b = srcB * mask + _b * (1 - mask);
      updateColor();
    }

    // EdgeSpectrum: additively lay spectral colour onto a luminance edge. The
    // caller has already scaled the add by mask * strength * gradient magnitude,
    // so flat fills (magnitude 0) get nothing and contours light up.
    public void ApplyEdgeSpectrum(double addR, double addG, double addB) {
      _r += addR;
      _g += addG;
      _b += addB;
      updateColor();
    }

    // Iridescence: recolour toward a spectral tint (already scaled to this
    // pixel's own brightness by the caller, so black stays black), faded in by
    // `w` (o * src alpha * strength). Like the Hue blend but keyed to geometry
    // rather than a carried hue.
    public void ApplyIridescence(
      double tintR, double tintG, double tintB, double w
    ) {
      _r = tintR * w + _r * (1 - w);
      _g = tintG * w + _g * (1 - w);
      _b = tintB * w + _b * (1 - w);
      updateColor();
    }

    // Shared RGB<->HSV conversion for the Hue blend (kept separate from
    // HueRotate's inline version above, which early-outs on black and
    // operates on this pixel's own _r/_g/_b in place rather than arbitrary
    // in/out values).
    private static void RGBToHSV(
      double r255, double g255, double b255,
      out double h, out double s, out double v
    ) {
      double r = r255 / 255d, g = g255 / 255d, b = b255 / 255d;
      double max = Math.Max(Math.Max(r, g), b);
      double min = Math.Min(Math.Min(r, g), b);
      double d = max - min;
      s = max == 0 ? 0 : d / max;
      v = max;
      h = 0;
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
    }

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

    // Blend an upper layer into this composite buffer.
    public void CompositeBlend(
      LEDDomeOutputBuffer src, DomeBlendMode mode, double opacity
    ) {
      for (int i = 0; i < pixels.Length; i++) {
        pixels[i].CompositeBlend(src.pixels[i], mode, opacity);
      }
    }

    // ---- Prism family: spatial + geometry blends (docs/prism.md) ----------

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
    private void EnsureNeighborTable() {
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

    private void EnsureNormals() {
      if (this.normals == null) {
        this.normals = this.BakePixelPositions();
      }
    }

    // ChromaticFringe blend: RGB channel-split aberration. Reads the pre-pass
    // `snapshot` (never `this`, which it mutates) through the neighbor table — R
    // pulled from +offset along `angle`, B from the opposite offset, G in place.
    // src's alpha × opacity is the mask, so the effect only bites where the
    // layer selecting this blend actually drew.
    public void CompositeChromaticFringe(
      LEDDomeOutputBuffer src, LEDDomeOutputBuffer snapshot,
      double opacity, double angle, int radiusBin
    ) {
      this.EnsureNeighborTable();
      int fwd = DirBin(angle);
      int back = (fwd + NeighborDirections / 2) % NeighborDirections;
      for (int i = 0; i < this.pixels.Length; i++) {
        double mask = opacity * src.pixels[i].a;
        if (mask == 0) {
          continue;
        }
        int ri = this.NeighborAt(i, fwd, radiusBin);
        int bi = this.NeighborAt(i, back, radiusBin);
        this.pixels[i].ApplyChromaticFringe(
          snapshot.pixels[ri].r, snapshot.pixels[bi].b, mask);
      }
    }

    // EdgeSpectrum blend: estimate the composite's luminance gradient from the
    // pre-pass `snapshot` (central differences along ±x/±y at radiusBin) and add
    // spectral colour where it is steep — hue keyed to gradient direction through
    // the dispersion ramp, intensity to magnitude. Flat fills get nothing.
    public void CompositeEdgeSpectrum(
      LEDDomeOutputBuffer src, LEDDomeOutputBuffer snapshot,
      double opacity, double strength, int radiusBin
    ) {
      this.EnsureNeighborTable();
      int right = DirBin(0);
      int left = DirBin(Math.PI);
      int up = DirBin(Math.PI / 2);
      int down = DirBin(3 * Math.PI / 2);
      for (int i = 0; i < this.pixels.Length; i++) {
        double mask = opacity * src.pixels[i].a;
        if (mask == 0) {
          continue;
        }
        double gx =
          Luma(snapshot.pixels[this.NeighborAt(i, right, radiusBin)]) -
          Luma(snapshot.pixels[this.NeighborAt(i, left, radiusBin)]);
        double gy =
          Luma(snapshot.pixels[this.NeighborAt(i, up, radiusBin)]) -
          Luma(snapshot.pixels[this.NeighborAt(i, down, radiusBin)]);
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
        Spectrum.Base.LEDColor.SpectralColor(
          t, out double sr, out double sg, out double sb);
        double w = intensity * mask;
        this.pixels[i].ApplyEdgeSpectrum(sr * w, sg * w, sb * w);
      }
    }

    // Iridescence blend: thin-film sheen. For each masked pixel, the angle
    // between its baked normal and `light` picks a spectral tint (repeated
    // `bands` times across the curvature); the tint is scaled to the pixel's own
    // brightness so unlit pixels stay dark, then faded in by opacity × src alpha
    // × strength. No neighbor sampling, so no snapshot needed.
    public void CompositeIridescence(
      LEDDomeOutputBuffer src, double opacity,
      Vector3 light, double bands, double strength
    ) {
      this.EnsureNormals();
      light = Vector3.Normalize(light);
      for (int i = 0; i < this.pixels.Length; i++) {
        double w = opacity * src.pixels[i].a * strength;
        if (w == 0) {
          continue;
        }
        double d = Vector3.Dot(this.normals[i], light); // -1..1
        double t = (d + 1) * 0.5 * bands;
        t -= Math.Floor(t); // wrap into 0..1 so bands repeat
        Spectrum.Base.LEDColor.SpectralColor(
          t, out double sr, out double sg, out double sb);
        double v = Math.Max(
          this.pixels[i].r, Math.Max(this.pixels[i].g, this.pixels[i].b)) / 255;
        this.pixels[i].ApplyIridescence(sr * v, sg * v, sb * v, w);
      }
    }

    private static double Luma(LEDDomeOutputPixel p) {
      return 0.299 * p.r + 0.587 * p.g + 0.114 * p.b;
    }
  }
}
