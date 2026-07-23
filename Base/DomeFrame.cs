using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;

namespace Spectrum.Base {

  // Mutable channels for one logical dome pixel. Identity and geometry live once
  // in DomeTopology, so renderer, compositor, and scratch frames do not clone
  // strut addresses or projected positions for every pixel.
  public struct LEDDomeOutputPixel {

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

    // Publish coverage independently of color, clamped to 0..1. The color
    // setter forces _a = 1 (drawing means opaque), so a layer that rides a
    // per-pixel field magnitude in its alpha — Caustics and Ripple Tank publish
    // their refraction gradients for the Refract blend — calls this after
    // setting color.
    public void SetAlpha(double a) {
      _a = a < 0 ? 0 : (a > 1 ? 1 : a);
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

    // Copy mutable frame channels only. Logical identity and projected
    // position belong to DomeTopology and are never part of a scratch copy.
    public void CopyChannelsFrom(LEDDomeOutputPixel src) {
      _color = src._color;
      _r = src._r;
      _g = src._g;
      _b = src._b;
      _a = src._a;
      hue = src.hue;
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

  public readonly record struct DomeTopologyPixel(
    int StrutIndex, int LedIndex,
    double StripX, double StripY,
    double TopDownX, double TopDownY
  ) {
    // Preserve the established planar-renderer contract while making the
    // projection explicit for new code.
    public double X => this.StripX;
    public double Y => this.StripY;

    // Small synthetic topologies historically supplied only planar positions.
    // Using those positions for both projections preserves their behavior.
    public DomeTopologyPixel(
      int strutIndex, int ledIndex, double x, double y
    ) : this(strutIndex, ledIndex, x, y, x, y) { }
  }

  // Immutable logical pixel geometry shared by every frame. The neighbor and
  // unit-sphere tables are lazy implementation caches; Lazy publishes each
  // immutable value once even if multiple renderer instances request it.
  public sealed class DomeTopology {
    public const int NeighborDirections = 16;
    public const int NeighborRadii = 4;
    public const double NeighborRadiusStep = 0.02;

    private readonly DomeTopologyPixel[] pixels;
    private readonly int[] strutStartIndex;
    private readonly int[] strutPixelCount;
    private readonly Lazy<int[]> neighborTable;
    private readonly Lazy<ImmutableArray<Vector3>> normals;
    private readonly Lazy<TopDownSpatialIndex> topDownSpatialIndex;

    public int PixelCount => this.pixels.Length;
    public ImmutableArray<Vector3> Normals => this.normals.Value;

    public DomeTopology(DomeTopologyPixel[] pixels) {
      if (pixels == null) {
        throw new ArgumentNullException(nameof(pixels));
      }
      this.pixels = (DomeTopologyPixel[])pixels.Clone();
      int maxStrut = -1;
      foreach (DomeTopologyPixel pixel in this.pixels) {
        maxStrut = Math.Max(maxStrut, pixel.StrutIndex);
      }
      this.strutStartIndex = new int[maxStrut + 1];
      this.strutPixelCount = new int[maxStrut + 1];
      for (int i = 0; i < this.pixels.Length; i++) {
        if (this.pixels[i].LedIndex == 0) {
          this.strutStartIndex[this.pixels[i].StrutIndex] = i;
        }
        this.strutPixelCount[this.pixels[i].StrutIndex] = Math.Max(
          this.strutPixelCount[this.pixels[i].StrutIndex],
          this.pixels[i].LedIndex + 1);
      }
      this.neighborTable = new Lazy<int[]>(this.BuildNeighborTable);
      this.normals = new Lazy<ImmutableArray<Vector3>>(
        this.BuildNormals);
      this.topDownSpatialIndex = new Lazy<TopDownSpatialIndex>(
        () => new TopDownSpatialIndex(this.pixels));
    }

    public DomeTopologyPixel PixelAt(int logicalPixel) =>
      this.pixels[logicalPixel];

    internal int FrameIndexAt(int strutIndex, int ledIndex) =>
      this.strutStartIndex[strutIndex] + ledIndex;

    internal int StrutPixelCount(int strutIndex) =>
      this.strutPixelCount[strutIndex];

    internal void EnsureNeighborTable() => _ = this.neighborTable.Value;

    internal int NeighborAt(int pixel, int dirBin, int radiusBin) =>
      this.neighborTable.Value[
        (pixel * NeighborRadii + radiusBin) * NeighborDirections + dirBin];

    internal int NearestTopDownPixel(double x, double y) =>
      this.topDownSpatialIndex.Value.FindNearest(x, y);

    private ImmutableArray<Vector3> BuildNormals() {
      var positions = ImmutableArray.CreateBuilder<Vector3>(
        this.pixels.Length);
      for (int i = 0; i < this.pixels.Length; i++) {
        DomeTopologyPixel point = this.pixels[i];
        double x = 2 * point.TopDownX - 1;
        double y = 1 - 2 * point.TopDownY;
        double radius = Math.Sqrt(x * x + y * y);
        if (radius > 1) {
          x /= radius;
          y /= radius;
          radius = 1;
        }
        double z = Math.Sqrt(Math.Max(0, 1 - radius * radius));
        positions.Add(Vector3.Normalize(new Vector3(
          (float)x, (float)y, (float)z)));
      }
      return positions.MoveToImmutable();
    }

    // Build the shared nearest-neighbor lookup from projected positions.
    private int[] BuildNeighborTable() {
      int n = this.pixels.Length;
      if (n == 0) {
        return Array.Empty<int>();
      }
      double cell = NeighborRadii * NeighborRadiusStep;
      double maxMatch = 1.5 * NeighborRadiusStep;
      double maxMatchSq = maxMatch * maxMatch;

      double minX = double.MaxValue, minY = double.MaxValue;
      double maxX = double.MinValue, maxY = double.MinValue;
      for (int i = 0; i < n; i++) {
        double x = this.pixels[i].X, y = this.pixels[i].Y;
        if (x < minX) { minX = x; }
        if (y < minY) { minY = y; }
        if (x > maxX) { maxX = x; }
        if (y > maxY) { maxY = y; }
      }
      int cols = Math.Max(1, (int)((maxX - minX) / cell) + 1);
      int rows = Math.Max(1, (int)((maxY - minY) / cell) + 1);
      int cellCount = cols * rows;
      int[] cellOf = new int[n];
      int[] counts = new int[cellCount + 1];
      for (int i = 0; i < n; i++) {
        int cx = (int)((this.pixels[i].X - minX) / cell);
        int cy = (int)((this.pixels[i].Y - minY) / cell);
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
        double px = this.pixels[i].X, py = this.pixels[i].Y;
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
                  double dx = this.pixels[j].X - tx;
                  double dy = this.pixels[j].Y - ty;
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
      return table;
    }

    // Arbitrary top-down resampling for coordinate-transform operations such
    // as Kaleidoscope. Unlike the short fixed-radius neighbor table, this
    // uniform spatial index can resolve any point in or near the projected
    // dome. Queries expand only until the current nearest candidate is closer
    // than every unvisited cell, so ordinary on-dome samples inspect a small
    // handful of buckets while still returning the exact nearest pixel.
    private sealed class TopDownSpatialIndex {
      private const double CellSize = 0.04;

      private readonly DomeTopologyPixel[] pixels;
      private readonly Dictionary<(int X, int Y), int[]> buckets;
      private readonly int minCellX;
      private readonly int maxCellX;
      private readonly int minCellY;
      private readonly int maxCellY;

      public TopDownSpatialIndex(DomeTopologyPixel[] pixels) {
        this.pixels = pixels;
        var pending = new Dictionary<(int X, int Y), List<int>>();
        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;
        for (int i = 0; i < pixels.Length; i++) {
          int x = Cell(pixels[i].TopDownX);
          int y = Cell(pixels[i].TopDownY);
          (int X, int Y) key = (x, y);
          if (!pending.TryGetValue(key, out List<int> bucket)) {
            bucket = new List<int>();
            pending.Add(key, bucket);
          }
          bucket.Add(i);
          minX = Math.Min(minX, x);
          maxX = Math.Max(maxX, x);
          minY = Math.Min(minY, y);
          maxY = Math.Max(maxY, y);
        }
        this.buckets = new Dictionary<(int X, int Y), int[]>(pending.Count);
        foreach (KeyValuePair<(int X, int Y), List<int>> entry in pending) {
          this.buckets.Add(entry.Key, entry.Value.ToArray());
        }
        this.minCellX = minX;
        this.maxCellX = maxX;
        this.minCellY = minY;
        this.maxCellY = maxY;
      }

      public int FindNearest(double x, double y) {
        if (this.pixels.Length == 0) {
          return -1;
        }
        int centerX = Cell(x);
        int centerY = Cell(y);
        int maxRing = Math.Max(
          Math.Max(Math.Abs(centerX - this.minCellX),
            Math.Abs(centerX - this.maxCellX)),
          Math.Max(Math.Abs(centerY - this.minCellY),
            Math.Abs(centerY - this.maxCellY)));
        int nearest = -1;
        double nearestDistanceSq = double.MaxValue;
        for (int ring = 0; ring <= maxRing; ring++) {
          int left = centerX - ring;
          int right = centerX + ring;
          int top = centerY - ring;
          int bottom = centerY + ring;
          for (int cellX = left; cellX <= right; cellX++) {
            this.SearchBucket(
              cellX, top, x, y, ref nearest, ref nearestDistanceSq);
            if (bottom != top) {
              this.SearchBucket(
                cellX, bottom, x, y, ref nearest, ref nearestDistanceSq);
            }
          }
          for (int cellY = top + 1; cellY < bottom; cellY++) {
            this.SearchBucket(
              left, cellY, x, y, ref nearest, ref nearestDistanceSq);
            if (right != left) {
              this.SearchBucket(
                right, cellY, x, y, ref nearest, ref nearestDistanceSq);
            }
          }
          if (nearest >= 0) {
            double boundaryDistance = Math.Min(
              Math.Min(x - left * CellSize,
                (right + 1) * CellSize - x),
              Math.Min(y - top * CellSize,
                (bottom + 1) * CellSize - y));
            if (nearestDistanceSq <=
                boundaryDistance * boundaryDistance) {
              return nearest;
            }
          }
        }
        return nearest;
      }

      private void SearchBucket(
        int cellX, int cellY, double x, double y,
        ref int nearest, ref double nearestDistanceSq
      ) {
        if (!this.buckets.TryGetValue(
            (cellX, cellY), out int[] candidates)) {
          return;
        }
        for (int candidateIndex = 0;
            candidateIndex < candidates.Length; candidateIndex++) {
          int candidate = candidates[candidateIndex];
          double dx = this.pixels[candidate].TopDownX - x;
          double dy = this.pixels[candidate].TopDownY - y;
          double distanceSq = dx * dx + dy * dy;
          if (distanceSq < nearestDistanceSq ||
              (distanceSq == nearestDistanceSq && candidate < nearest)) {
            nearest = candidate;
            nearestDistanceSq = distanceSq;
          }
        }
      }

      private static int Cell(double coordinate) =>
        (int)Math.Floor(coordinate / CellSize);
    }
  }

  public class DomeFrame {
    public readonly LEDDomeOutputPixel[] pixels;
    public DomeTopology Topology { get; }

    // DomeTopology maps a strut and LED-within-strut to the logical frame
    // index. It depends only on logical identity, not physical cable mapping.
    // Baked spatial neighbor table used by the spatial prism
    // blends to resample the composite at an offset. For each pixel we store the
    // nearest pixel to (x + r·cosθ, y + r·sinθ) over NeighborDirections evenly
    // spaced angles and NeighborRadii radius steps, flattened as
    // (pixel * NeighborRadii + radiusBin) * NeighborDirections + dirBin. Built
    // lazily (EnsureNeighborTable) on the first spatial consumer. Prism blends
    // normally reach it through the composite buffer; surface simulations such
    // as Living Skin reuse the same topology cache through a layer buffer. Like
    // strutStartIndex it depends only on the shared x/y geometry. A tap that
    // finds no pixel near its target stores the pixel's own index, degrading to
    // an in-place read rather than smearing across a gap in the layout.
    public const int NeighborDirections = DomeTopology.NeighborDirections;
    public const int NeighborRadii = DomeTopology.NeighborRadii;
    // Projected-plane units between radius steps; a dome LED pitch is ~0.013, so
    // the four steps span ~1.5–6 LED pitches.
    public const double NeighborRadiusStep = DomeTopology.NeighborRadiusStep;

    public DomeFrame(DomeTopology topology) {
      this.Topology = topology ?? throw new ArgumentNullException(nameof(topology));
      this.pixels = new LEDDomeOutputPixel[topology.PixelCount];
    }

    // Writes a pixel addressed by strut and LED-within-strut — the same
    // addressing the legacy LEDDomeOutput.SetPixel path used — so strut-based
    // visualizers can render into the buffer and flush it once via WriteBuffer.
    public void SetPixel(int strutIndex, int ledIndex, int color) {
      this.pixels[this.Topology.FrameIndexAt(strutIndex, ledIndex)].color = color;
    }

    // Erase a strut-addressed pixel to fully transparent black (see
    // LEDDomeOutputPixel.Clear) — "reveal below", not "paint black".
    public void ClearPixel(int strutIndex, int ledIndex) {
      this.pixels[this.Topology.FrameIndexAt(strutIndex, ledIndex)].Clear();
    }

    // Returns the topology's shared, immutable unit-sphere positions. The
    // normalized projected y is flipped and out-of-disc pixels flatten to z=0,
    // preserving the legacy BakePixelPositions coordinate contract.
    public ImmutableArray<Vector3> BakePixelPositions() =>
      this.Topology.Normals;

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

    // Initialize every mutable channel before executing a non-empty render
    // plan. Pixel.Clear deliberately preserves a renderer's published hue, so
    // the compositor resets that auxiliary channel explicitly as well.
    public void ResetComposite() {
      for (int i = 0; i < pixels.Length; i++) {
        pixels[i].Clear();
        pixels[i].hue = 0;
      }
    }

    // ---- Spatial-sampling infrastructure for the prism blends -------------
    // The blends themselves are DomeBlend classes; the baked
    // neighbor table and normals stay here because they belong to this buffer's
    // geometry, not to any one blend.

    // Copy every pixel from `other` into this buffer. Used to snapshot the
    // composite before a spatial blend, since those read neighbors of the buffer
    // they mutate and must read the pre-pass state. LEDDomeOutputPixel is a
    // struct, so this copies values, not references.
    public void CopyFrom(DomeFrame other) {
      this.EnsureCompatible(other, nameof(other));
      for (int i = 0; i < this.pixels.Length; i++) {
        this.pixels[i].CopyChannelsFrom(other.pixels[i]);
      }
    }

    private void EnsureCompatible(DomeFrame other, string parameterName) {
      if (other == null) {
        throw new ArgumentNullException(parameterName);
      }
      if (!ReferenceEquals(this.Topology, other.Topology)) {
        throw new ArgumentException(
          "Frames must share one DomeTopology.", parameterName);
      }
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
    public int NeighborAt(int pixel, int dirBin, int radiusBin) =>
      this.Topology.NeighborAt(pixel, dirBin, radiusBin);

    // Nearest logical pixel to an arbitrary point in the dome's baked
    // top-down projection. Returns -1 only for an empty topology.
    public int NearestTopDownPixel(double x, double y) =>
      this.Topology.NearestTopDownPixel(x, y);

    // Build neighborTable once from the baked x/y positions. Uses a uniform
    // spatial grid (cell = the max tap radius) so each nearest lookup only scans
    // the 3×3 block of cells around its target — the block is guaranteed to
    // contain any pixel within the max radius. Cheap enough to run on the
    // operator thread the first frame a spatial blend appears; a no-op after.
    // Public so the spatial DomeBlend classes can bake before sampling.
    public void EnsureNeighborTable() =>
      this.Topology.EnsureNeighborTable();

    // Baked unit-sphere normals, lazily cached for the Iridescence blend the
    // same way the orientation layers cache BakePixelPositions().
    public ImmutableArray<Vector3> Normals => this.Topology.Normals;
  }

}
