using Spectrum.Base;
using Spectrum.LEDs;
using System;

namespace Spectrum.Visualizers {

  // Caustics: the dancing filament web of light seen through a layer of water,
  // as if the dome were the floor of a sunlit pool (docs/caustics.md). The
  // pattern lives in plan view, so it samples the baked projected (x, y) that
  // every pixel already carries — no new geometry, and it reads correctly from
  // inside the dome (light dancing "down through water" onto the shell).
  // Center-free and input-free like Background/NoiseCloud: it paints color+alpha
  // into its own layer buffer, meant to composite under Add or Screen.
  //
  // The `method` enum is a fidelity ladder tuned on-site rather than guessed in
  // advance. This first cut ships the two analytic rungs:
  //   0 Shimmer      — 4 summed sines, pow-sharpened. Soft bands, not real
  //                    filaments; proves the plumbing.
  //   1 Interference — the classic iterated domain-warp trig construction that
  //                    yields the authentic cellular caustic web. The expected
  //                    default.
  // Higher rungs (Lens, Ripple tank, GPU) append to the enum later without
  // shifting these indices.
  //
  // Per-layer params (visualizer-consumed, read each frame): method, scale
  // (feature size / wavenumber), speed (churn rate), sharpness (filament
  // thinness — the pow exponent), brightness (output gain), and the tint color.
  class LEDDomeCausticsVisualizer : DomeLayerVisualizer {

    private readonly Configuration config;
    private readonly LEDDomeOutput dome;
    private readonly LEDDomeOutputBuffer buffer;

    // Wall-clock accumulator advanced by speed * elapsed each frame so the
    // animation is frame-rate independent (same pattern as NoiseCloud/Wave).
    // Starts mid-churn rather than at 0: Interference is degenerate at t = 0
    // (every iteration's time term collapses to 0), which renders as a
    // visibly rectilinear web for the first moments.
    private readonly System.Diagnostics.Stopwatch frameTimer =
      new System.Diagnostics.Stopwatch();
    private double time = 30;

    // Fixed offsets into the analytic field. Sampling near the coordinate origin
    // is degenerate (trig arguments all near 0), so we shift each pixel's plane
    // coordinate into a region with richer, asymmetric variety.
    private const double OffsetX = 23.0;
    private const double OffsetY = 17.0;

    public LEDDomeCausticsVisualizer(
      Configuration config,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeOutputBuffer();
    }

    public int Priority {
      get {
        return DomeLayerSettings.StackActivates(
          this.config.domeLayerStack, "caustics"
        ) ? 2 : 0;
      }
    }

    public string LayerKey => "caustics";
    public LEDDomeOutputBuffer LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { });
    }

    public void Visualize() {
      var stack = this.config.domeLayerStack;
      int method =
        (int)DomeLayerSettings.ParamValue(stack, this.LayerKey, "method");
      double scale =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "scale");
      double speed =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "speed");
      double sharpness =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "sharpness");
      double brightness =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "brightness");
      int tint = (int)DomeLayerSettings.ParamValue(stack, this.LayerKey, "color");

      // Advance the churn clock by wall-clock elapsed * speed so the pattern
      // evolves at a steady rate regardless of the Operator loop speed.
      double elapsed = 0;
      if (!this.frameTimer.IsRunning) {
        this.frameTimer.Restart();
      } else {
        elapsed = this.frameTimer.Elapsed.TotalSeconds;
        this.frameTimer.Restart();
      }
      this.time += speed * elapsed;
      double t = this.time;

      double tr = (tint >> 16) & 0xFF;
      double tg = (tint >> 8) & 0xFF;
      double tb = tint & 0xFF;

      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        ref var pixel = ref this.buffer.pixels[i];
        double px = pixel.x * scale + OffsetX;
        double py = pixel.y * scale + OffsetY;

        double lum = method == 0
          ? Shimmer(px, py, t, sharpness)
          : Interference(px, py, t, sharpness);
        lum *= brightness;
        if (lum < 0) {
          lum = 0;
        } else if (lum > 1) {
          lum = 1;
        }

        int r = (int)(tr * lum);
        int g = (int)(tg * lum);
        int b = (int)(tb * lum);
        pixel.color = (r << 16) | (g << 8) | b;
      }
    }

    // Tier 0: four summed sines folded to [0,1] and pow-sharpened. Cheap and
    // smooth — soft interfering bands, not true filaments, but it proves the
    // layer/compositor plumbing end to end.
    private static double Shimmer(
      double x, double y, double t, double sharpness
    ) {
      double s = Math.Sin(x + t)
        + Math.Sin(y - 0.9 * t)
        + Math.Sin(0.7 * (x + y) + 1.3 * t)
        + Math.Sin(0.7 * (x - y) - 0.7 * t);
      double v = s * 0.125 + 0.5; // s in [-4,4] -> [0,1]
      return Math.Pow(v, sharpness);
    }

    // Tier 1: the classic iterated domain-warp caustic. Each iteration warps the
    // sample point by trig of its own coordinates (a cheap cellular-web
    // construction) and accumulates an inverse-distance term whose reciprocal
    // sinusoids concentrate brightness onto thin focused filaments — exactly the
    // structure that distinguishes a caustic web from generic plasma. The pow
    // exponent (`sharpness`) sets filament thinness.
    private const int Iterations = 5;
    // Numerator of each iteration's reciprocal term. The source construction
    // computes it as plane-coordinate * intensity with the plane coordinate
    // pinned near 250 and intensity 0.005 — i.e. effectively this constant.
    // Feeding the pixel's own (much smaller) coordinate here instead made
    // every term ~10x too large, pushing the accumulated field far past the
    // 1.17 crossover below; the Math.Abs then folded it back bright, turning
    // the whole dome near-white with dark seams (measured mean luminance 0.9+)
    // instead of bright filaments on dark (~0.3 with this constant).
    private const double TermScale = 1.25;

    private static double Interference(
      double x, double y, double t, double sharpness
    ) {
      double ix = x, iy = y;
      double c = 1.0;
      for (int n = 1; n <= Iterations; n++) {
        double tn = t * (1.0 - 3.5 / n);
        double nx = x + Math.Cos(tn - ix) + Math.Sin(tn + iy);
        double ny = y + Math.Sin(tn - iy) + Math.Cos(tn + ix);
        ix = nx;
        iy = ny;
        // sin/cos near 0 sends the term to +/-Inf, which contributes 0 after
        // the reciprocal — no guard needed.
        double dx = TermScale / Math.Sin(ix + tn);
        double dy = TermScale / Math.Cos(iy + tn);
        c += 1.0 / Math.Sqrt(dx * dx + dy * dy);
      }
      c /= Iterations;
      c = 1.17 - Math.Pow(c, 1.4);
      return Math.Pow(Math.Abs(c), sharpness);
    }
  }
}
