using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Numerics;

namespace Spectrum.Visualizers {

  // An animated "noise cloud": every pixel is tinted by a fractal
  // gradient-noise field sampled over the dome's baked unit-sphere positions,
  // so the texture is coherent across the real surface (no projection seam).
  // The spatial sample point is fixed per pixel; the field is animated by
  // advancing a fourth "time" coordinate linearly, so the cloud morphs in
  // place with no directional drift.
  //
  // Gradient (Perlin-style) noise rather than value noise matters here: a
  // value-noise field animated through time changes only via the time-axis
  // interpolation fades, which are shared by every pixel and have zero
  // derivative at every time-lattice crossing — so the whole dome visibly
  // eased to a standstill and surged again, over and over, no matter how the
  // time path was shaped (measured; a circular path through two time axes
  // only moved the stalls around). Gradient noise's temporal derivative also
  // carries the per-corner gradient components, which never vanish for every
  // pixel at once, so the morph rate stays steady.
  //
  // Separately, because only a few independent blobs span the dome, the
  // field's dome-wide *statistics* drift as it morphs: both its average level
  // (a brightness "breath") and its spread (a contrast "breath" — the whole
  // texture rhythmically sharpening then going flat). Either reads as a pulse
  // even with a steady morph rate. So Visualize renormalizes the field to a
  // constant spatial mean *and* spread every frame (below), leaving only the
  // local pattern animated. Center-free and input-free like
  // Background/Twinkle — no data source, it just paints texture. Meant to
  // composite under Multiply (to break up a flat fill below) or Add (to
  // sprinkle glow).
  //
  // The noise is deliberately cheap: 4D gradient noise (3 spatial + 1 time)
  // built on an integer hash with smoothstep interpolation and a handful of
  // octaves — 16 corner hashes per octave, each corner's gradient pulled from
  // a 32-entry table and dotted with the corner offset; no trig, no per-frame
  // allocation. A few thousand pixels times 16 hashes per octave is trivial
  // on the operator thread.
  //
  // Per-layer params (visualizer-consumed, read each frame): scale (spatial
  // frequency), morph speed, octaves (detail), contrast (midtone steepness),
  // and the tint color.
  class LEDDomeNoiseCloudVisualizer : DomeLayerVisualizer {

    private readonly Configuration config;
    private readonly LEDDomeOutput dome;
    private readonly LEDDomeOutputBuffer buffer;

    // Baked once: the unit-sphere position of every pixel, so the noise samples
    // the true 3D dome surface rather than the flat projection.
    private readonly Vector3[] positions;

    // Scratch holding this frame's raw per-pixel noise between the two Visualize
    // passes, so the field's spatial mean and spread can be measured and
    // normalized out without evaluating the (expensive) noise twice. Sized once
    // at construction.
    private readonly double[] noiseField;

    // The spatial spread (standard deviation) the displayed field is
    // renormalized to each frame — the raw noise's own spread is measured and
    // divided out, so this constant alone (with the `contrast` param) sets how
    // much texture shows.
    private const double NominalSpread = 0.15;

    // Wall-clock accumulator advanced by speed * elapsed each frame so the
    // animation is frame-rate independent (same pattern as Caustics/Wave).
    private readonly System.Diagnostics.Stopwatch frameTimer =
      new System.Diagnostics.Stopwatch();
    private double time;

    public LEDDomeNoiseCloudVisualizer(
      Configuration config,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeOutputBuffer();
      this.positions = this.buffer.BakePixelPositions();
      this.noiseField = new double[this.buffer.pixels.Length];
    }

    public int Priority {
      get {
        return DomeLayerSettings.StackActivates(
          this.config.domeLayerStack, "noise-cloud"
        ) ? 2 : 0;
      }
    }

    public string LayerKey => "noise-cloud";
    public LEDDomeOutputBuffer LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { });
    }

    public void Visualize() {
      var stack = this.config.domeLayerStack;
      double scale =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "scale");
      double speed =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "speed");
      int octaves = (int)DomeLayerSettings.ParamValue(
        stack, this.LayerKey, "octaves"
      );
      double contrast =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "contrast");
      int tint = (int)DomeLayerSettings.ParamValue(stack, this.LayerKey, "color");

      // Advance the time coordinate by wall-clock elapsed * speed, so `speed`
      // is lattice units per second regardless of the Operator loop rate. The
      // spatial sample stays pinned to each pixel; only time moves, so the
      // cloud boils in place with no directional drift.
      double elapsed = 0;
      if (!this.frameTimer.IsRunning) {
        this.frameTimer.Restart();
      } else {
        elapsed = this.frameTimer.Elapsed.TotalSeconds;
        this.frameTimer.Restart();
      }
      this.time += speed * elapsed;
      double w = this.time;

      double tr = (tint >> 16) & 0xFF;
      double tg = (tint >> 8) & 0xFF;
      double tb = tint & 0xFF;

      // Pass 1: evaluate the raw field and accumulate its spatial mean and
      // variance. Both drift frame to frame as the field morphs; normalizing
      // them out (pass 2) is what kills the brightness and contrast "breaths".
      int count = this.buffer.pixels.Length;
      double sum = 0, sumSq = 0;
      for (int i = 0; i < count; i++) {
        Vector3 p = this.positions[i];
        double n = Fbm(p.X * scale, p.Y * scale, p.Z * scale, w, octaves);
        this.noiseField[i] = n;
        sum += n;
        sumSq += n * n;
      }
      double mean = sum / count;
      double variance = sumSq / count - mean * mean;
      // Floor guards a degenerate near-flat frame (e.g. very low scale) from a
      // divide-by-zero that would explode the texture.
      double std = Math.Sqrt(variance > 1e-8 ? variance : 1e-8);
      // Rescale each frame so the field has a constant mean (0.5) and constant
      // spread (NominalSpread): (n - mean) / std * NominalSpread + 0.5. Holds the
      // dome-wide brightness and contrast steady over time.
      double gain = NominalSpread / std;

      // Pass 2: renormalize, steepen around the midtone, clamp, and tint.
      for (int i = 0; i < count; i++) {
        double n = 0.5 + (this.noiseField[i] - mean) * gain;
        // Steepen around the 0.5 midtone so the texture has punch without the
        // field clipping to flat 0/1 across whole regions; clamp to [0,1].
        n = (n - 0.5) * contrast + 0.5;
        if (n < 0) {
          n = 0;
        } else if (n > 1) {
          n = 1;
        }
        int r = (int)(tr * n);
        int g = (int)(tg * n);
        int b = (int)(tb * n);
        this.buffer.pixels[i].color = (r << 16) | (g << 8) | b;
      }
    }

    // Fractional Brownian motion: sum `octaves` of gradient noise at doubling
    // frequency and halving amplitude, normalized back to a fixed range. The
    // time coordinate doubles with the spatial frequency too, so finer detail
    // also boils faster — the usual fBm behavior.
    private static double Fbm(
      double x, double y, double z, double w, int octaves
    ) {
      if (octaves < 1) {
        octaves = 1;
      }
      double sum = 0, amp = 1, norm = 0;
      for (int o = 0; o < octaves; o++) {
        sum += amp * GradientNoise(x, y, z, w);
        norm += amp;
        amp *= 0.5;
        x *= 2;
        y *= 2;
        z *= 2;
        w *= 2;
      }
      return sum / norm;
    }

    // Perlin's 4D gradient set: every vector with one component 0 and the
    // other three ±1. Components are exact in float, so double math on them
    // stays exact; the dot products reduce to adds/subtracts.
    private static readonly Vector4[] Gradients = BuildGradients();

    private static Vector4[] BuildGradients() {
      var gradients = new Vector4[32];
      int i = 0;
      for (int zero = 0; zero < 4; zero++) {
        for (int signs = 0; signs < 8; signs++) {
          var g = new float[4];
          int bit = 0;
          for (int axis = 0; axis < 4; axis++) {
            if (axis != zero) {
              g[axis] = ((signs >> bit++) & 1) != 0 ? 1f : -1f;
            }
          }
          gradients[i++] = new Vector4(g[0], g[1], g[2], g[3]);
        }
      }
      return gradients;
    }

    // 4D gradient noise: at each of the 2^4 = 16 integer lattice corners
    // around (x,y,z,w), dot a hash-selected gradient with the offset from that
    // corner, then multilinearly interpolate with a smoothstep fade on each
    // axis. Output is signed, roughly ±0.6 (Visualize renormalizes it, so the
    // exact range is irrelevant). The corner sum is written as a loop over the
    // 4-bit corner index rather than 15 nested lerps — same result, far less
    // to read.
    private static double GradientNoise(double x, double y, double z, double w) {
      int xi = FastFloor(x);
      int yi = FastFloor(y);
      int zi = FastFloor(z);
      int wi = FastFloor(w);
      double fx = x - xi;
      double fy = y - yi;
      double fz = z - zi;
      double fw = w - wi;
      double u = Smooth(fx);
      double v = Smooth(fy);
      double s = Smooth(fz);
      double t = Smooth(fw);

      double sum = 0;
      for (int c = 0; c < 16; c++) {
        int bx = c & 1;
        int by = (c >> 1) & 1;
        int bz = (c >> 2) & 1;
        int bw = (c >> 3) & 1;
        // Each corner's weight is the product of its per-axis fade: the fade
        // if the corner is the +1 neighbor on that axis (bit set), else
        // 1 - fade.
        double weight =
          (bx != 0 ? u : 1 - u) *
          (by != 0 ? v : 1 - v) *
          (bz != 0 ? s : 1 - s) *
          (bw != 0 ? t : 1 - t);
        Vector4 g = Gradients[Hash(xi + bx, yi + by, zi + bz, wi + bw) >> 27];
        sum += weight * (
          g.X * (fx - bx) + g.Y * (fy - by) + g.Z * (fz - bz) + g.W * (fw - bw)
        );
      }
      return sum;
    }

    // Smoothstep fade (6t^5 - 15t^4 + 10t^3) — the standard Perlin easing that
    // removes the grid-aligned creases plain linear interpolation would show.
    private static double Smooth(double t) {
      return t * t * t * (t * (t * 6 - 15) + 10);
    }

    // Deterministic integer hash of a lattice corner. A handful of
    // odd-constant multiplies and xor-shifts — cheap and well-mixed enough
    // that the noise shows no obvious repetition at these scales. The top 5
    // bits select a gradient.
    private static uint Hash(int x, int y, int z, int w) {
      uint h = (uint)(x * 374761393 + y * 668265263 +
        z * 1274126177 + w * 1911520717);
      h = (h ^ (h >> 13)) * 1274126177u;
      return h ^ (h >> 16);
    }

    // floor() that returns an int directly (Math.Floor returns a double and is
    // slower); correct for negatives, unlike a plain (int) truncation.
    private static int FastFloor(double x) {
      int xi = (int)x;
      return x < xi ? xi - 1 : xi;
    }
  }
}
