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
  // The `method` enum is an analytic fidelity ladder tuned on-site rather than
  // guessed in advance:
  //   0 Shimmer      — 4 summed sines, pow-sharpened. Soft bands, not real
  //                    filaments; proves the plumbing.
  //   1 Interference — the classic iterated domain-warp trig construction that
  //                    yields the authentic cellular caustic web. The expected
  //                    default.
  //   2 Lens         — an explicit water surface h(x,y,t) (a small sum of
  //                    dispersive sine waves) shaded by the thin-lens focus
  //                    form 1/|1 − f·∇²h|: bright exactly where the surface
  //                    focuses. Physically consistent — the same surface's ∇h
  //                    (closed form, no finite differences) feeds Refract, so
  //                    shimmer and brightness agree.
  // Ripple Tank now lives in its own orientation-only layer.
  //
  // Per-layer params (visualizer-consumed, read each frame): method, scale
  // (feature size / wavenumber), speed (churn rate), sharpness (filament
  // thinness — the pow exponent), brightness (output gain), and tint color.
  //
  // When this layer's blend is Refract (docs/caustics.md), the layer is a
  // displacement field rather than paint: alongside the color (which Refract,
  // like every adjustment blend, ignores) it publishes the raw field's
  // gradient through the pixel side channels — direction into `hue`
  // (0..1 = 0..2π) and magnitude into alpha, which the blend reads as both
  // displacement and mask. The gradient is only computed when Refract is
  // actually selected (for tiers 0–1 it costs two extra field evaluations per
  // pixel via forward differences; Lens gets it closed-form for free); under
  // any other blend alpha keeps its ordinary drawing semantics (opaque where
  // painted).
  class LEDDomeCausticsVisualizer : DomeLayerVisualizer {

    private readonly LayerRendererRuntime runtime;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;

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
      LayerRendererRuntime runtime,
      DomeRenderContext dome
    ) {
      this.runtime = runtime;
      this.dome = dome;
      this.buffer = this.dome.MakeDomeFrame();
    }

    public int Priority => 2;

    public string LayerKey => "caustics";
    public DomeFrame LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    public Input[] GetInputs() => Array.Empty<Input>();

    public void Visualize() {
      CausticsLayerOptions options =
        this.runtime.GetOptions<CausticsLayerOptions>();
      int method = options.Method;
      double scale = options.Scale;
      double speed = options.Speed;
      double sharpness = options.Sharpness;
      double brightness = options.Brightness;
      int tint = options.Color;

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

      // Publish the gradient side channels only when this layer's blend will
      // read them (see the class comment).
      bool refracting =
        this.runtime.Snapshot.OperationId == DomeBlend.Refract.Id;

      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        ref var pixel = ref this.buffer.pixels[i];
        DomeTopologyPixel point = this.buffer.Topology.PixelAt(i);

        double v, gx = 0, gy = 0, gradGain = GradGain;
        if (method == 2) {
          // Lens evaluates its wave sum once per pixel and gets luminance,
          // ∇h, and ∇²h from the same pass — the gradient is closed form, so
          // Refract costs no extra field evaluations here.
          double px = point.X * scale + OffsetX;
          double py = point.Y * scale + OffsetY;
          v = LensField(px, py, t, refracting, out gx, out gy);
          gradGain = LensGradGain;
        } else {
          double px = point.X * scale + OffsetX;
          double py = point.Y * scale + OffsetY;
          v = Field(method, px, py, t);
          if (refracting) {
            // Forward-difference gradient of the raw field (pre-sharpness —
            // the pow-sharpened luminance is near-flat everywhere except at
            // the filaments, which is exactly wrong for a displacement
            // field). Sampled in scaled coordinates, so `scale` sets the
            // shimmer's spatial frequency without changing its published
            // magnitude.
            gx = (Field(method, px + GradEps, py, t) - v) / GradEps;
            gy = (Field(method, px, py + GradEps, t) - v) / GradEps;
          }
        }

        double lum = Math.Pow(v, sharpness) * brightness;
        if (lum < 0) {
          lum = 0;
        } else if (lum > 1) {
          lum = 1;
        }

        int r = (int)(tr * lum);
        int g = (int)(tg * lum);
        int b = (int)(tb * lum);
        pixel.color = (r << 16) | (g << 8) | b;

        if (refracting) {
          double angle = Math.Atan2(gy, gx);
          double huePart = angle / (2 * Math.PI);
          pixel.hue = huePart - Math.Floor(huePart);
          double mag = Math.Sqrt(gx * gx + gy * gy) * gradGain;
          // After pixel.color above (whose setter forces alpha opaque).
          pixel.SetAlpha(mag > 1 ? 1 : mag);
        }
      }
    }

    // Gradient sampling step, in scaled field coordinates (feature wavelength
    // is ~2π there, so this is well sub-feature) — big enough to smooth over
    // Interference's reciprocal-trig spikes rather than chase them.
    private const double GradEps = 0.25;
    // Normalizes typical field slopes (~0.2 for Shimmer, up to ~1 at
    // Interference's filament walls) so the published magnitude spans the
    // 0..1 alpha range and saturates where the surface is steepest.
    private const double GradGain = 2.5;

    // The raw (pre-sharpness) field for the selected method, in [0, ~1] —
    // the shared evaluation for both luminance and the gradient taps.
    private static double Field(int method, double x, double y, double t) {
      return method == 0
        ? ShimmerField(x, y, t)
        : InterferenceField(x, y, t);
    }

    // Tier 0: four summed sines folded to [0,1] (the caller pow-sharpens).
    // Cheap and smooth — soft interfering bands, not true filaments, but it
    // proves the layer/compositor plumbing end to end.
    private static double ShimmerField(double x, double y, double t) {
      double s = Math.Sin(x + t)
        + Math.Sin(y - 0.9 * t)
        + Math.Sin(0.7 * (x + y) + 1.3 * t)
        + Math.Sin(0.7 * (x - y) - 0.7 * t);
      return s * 0.125 + 0.5; // s in [-4,4] -> [0,1]
    }

    // Tier 1: the classic iterated domain-warp caustic. Each iteration warps the
    // sample point by trig of its own coordinates (a cheap cellular-web
    // construction) and accumulates an inverse-distance term whose reciprocal
    // sinusoids concentrate brightness onto thin focused filaments — exactly the
    // structure that distinguishes a caustic web from generic plasma. The
    // caller's pow exponent (`sharpness`) sets filament thinness.
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

    private static double InterferenceField(double x, double y, double t) {
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
      return Math.Abs(c);
    }

    // Tier 2: an explicit water surface h = Σᵢ Aᵢ sin(kᵢ·p − ωᵢt + φᵢ) over
    // LensWaveCount directions, with deep-water dispersion (ω ∝ √k) and
    // Aᵢ ∝ 1/kᵢ² so every component contributes equally to ∇²h — which makes
    // the Laplacian a plain sum of sines, closed form. The thin-lens focus
    // shading
    //   v = min(1, c / (|1 − f·∇²h| + ε))
    // is bright exactly where the surface focuses light, the structure that
    // distinguishes a caustic web from generic plasma. ∇h is closed form too
    // and comes back through gx/gy when the blend is Refract, so shimmer and
    // brightness are the same surface.
    private const int LensWaveCount = 6;
    // Per-component wave vectors, ∇h weights (the Aᵢ = 1/kᵢ² amplitudes
    // folded in: kᵢ/kᵢ² = d̂ᵢ/kᵢ; the overall surface amplitude is folded
    // into LensFocus / LensGradGain), dispersion rates, and phases.
    private static readonly double[] LensKx = new double[LensWaveCount];
    private static readonly double[] LensKy = new double[LensWaveCount];
    private static readonly double[] LensGradX = new double[LensWaveCount];
    private static readonly double[] LensGradY = new double[LensWaveCount];
    private static readonly double[] LensOmega = new double[LensWaveCount];
    private static readonly double[] LensPhase = new double[LensWaveCount];

    static LEDDomeCausticsVisualizer() {
      // Directions stepped by the golden angle (no two near-parallel or
      // near-opposite), wavenumbers a geometric ladder spanning ~1.7 octaves
      // so the web has features at several sizes.
      const double goldenAngle = 2.399963229728653;
      for (int i = 0; i < LensWaveCount; i++) {
        double k = Math.Pow(1.28, i);
        double theta = i * goldenAngle;
        LensKx[i] = k * Math.Cos(theta);
        LensKy[i] = k * Math.Sin(theta);
        LensGradX[i] = LensKx[i] / (k * k);
        LensGradY[i] = LensKy[i] / (k * k);
        LensOmega[i] = Math.Sqrt(k); // deep-water dispersion
        LensPhase[i] = 1.7 * i;
      }
    }

    // f·A₀ in the focus form: how strongly surface curvature bends the light.
    // Filaments appear where Σsin ≈ −1/LensFocus ≈ −2.2 — reachable but rare
    // (the 6-component sum has σ ≈ 1.7), so the web stays sparse.
    private const double LensFocus = 0.45;
    // Numerator / denominator floor of the focus form: together they set the
    // saturated filament-core width (|1 − f·∇²h| ≤ c − ε) and the base level
    // away from focus (~c, dimmed further by the caller's pow-sharpening).
    private const double LensBright = 0.2;
    private const double LensEps = 0.04;
    // Spans the published displacement magnitude over 0..1: typical |∇h| of
    // the wave sum is ~1.1, peaking ~3.5, so alpha saturates only at the
    // steepest surface slopes.
    private const double LensGradGain = 0.55;

    private static double LensField(
      double x, double y, double t, bool wantGrad, out double gx, out double gy
    ) {
      double lap = 0;
      gx = 0;
      gy = 0;
      for (int i = 0; i < LensWaveCount; i++) {
        double arg = LensKx[i] * x + LensKy[i] * y - LensOmega[i] * t
          + LensPhase[i];
        lap -= Math.Sin(arg); // ∇²hᵢ = −Aᵢkᵢ² sin(arg), and Aᵢkᵢ² ≡ 1
        if (wantGrad) {
          double c = Math.Cos(arg);
          gx += LensGradX[i] * c;
          gy += LensGradY[i] * c;
        }
      }
      double v = LensBright / (Math.Abs(1 - LensFocus * lap) + LensEps);
      return v > 1 ? 1 : v;
    }

  }
}
