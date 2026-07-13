using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Numerics;

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
  // advance. The analytic rungs shipped so far:
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
  //   3 Ripple Tank  — the interactive rung: a damped wave-equation grid over
  //                    the plan-view square, shaded by the same thin-lens form
  //                    on the grid's Laplacian. Every moving orientation input
  //                    is a surface object whose projected path presses a wake
  //                    into the water. The LayerTrigger cluster (default Beat)
  //                    also drops droplets where the wand aims, audio loudness
  //                    stirs a continuous churn of small pokes, and 🧹 flattens
  //                    the water.
  // A GPU rung would append to the enum later without shifting these indices.
  //
  // Per-layer params (visualizer-consumed, read each frame): method, scale
  // (feature size / wavenumber; the tank maps it inversely to droplet size),
  // speed (churn rate; the tank maps it to sim step rate), sharpness (filament
  // thinness — the pow exponent), brightness (output gain), the tint color,
  // wake size/strength, and the trigger cluster (tank droplets only).
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

    private readonly Configuration config;
    private readonly AudioInput audio;
    private readonly OrientationInput orientationInput;
    private readonly OrientationCenter center;
    private readonly LEDDomeOutput dome;
    private readonly LEDDomeOutputBuffer buffer;
    private readonly LayerTrigger trigger;
    private readonly Random rand = new Random();

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
      AudioInput audio,
      OrientationInput orientationInput,
      OrientationCenter center,
      BeatBroadcaster beat,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.audio = audio;
      this.orientationInput = orientationInput;
      this.center = center;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeOutputBuffer();
      this.trigger = new LayerTrigger(
        config, orientationInput, this.LayerKey, beat, audio);
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
      return this.inputs
        ?? (this.inputs = new Input[] { this.orientationInput });
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

      // Publish the gradient side channels only when this layer's blend will
      // read them (see the class comment).
      DomeLayerSettings layer = DomeLayerSettings.ForKey(stack, this.LayerKey);
      bool refracting =
        layer != null && layer.BlendMode == DomeBlend.Refract.Name;

      if (method == 3) {
        this.TankAdvance(stack, scale, speed, elapsed, refracting);
      }

      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        ref var pixel = ref this.buffer.pixels[i];

        double v, gx = 0, gy = 0, gradGain = GradGain;
        if (method == 3) {
          // The tank's field lives on the sim grid: the same thin-lens focus
          // form as Lens, applied to the grid Laplacian, bilinearly sampled
          // at this pixel's baked grid coordinates. The gradient (for
          // Refract) is sampled from the grid's central differences.
          double lap = this.TankSampleAt(this.tankLap, i);
          v = TankBright / (Math.Abs(1 - TankFocus * lap) + TankEps);
          if (v > 1) {
            v = 1;
          }
          if (refracting) {
            gx = this.TankSampleAt(this.tankGradX, i);
            gy = this.TankSampleAt(this.tankGradY, i);
          }
          gradGain = TankGradGain;
        } else if (method == 2) {
          // Lens evaluates its wave sum once per pixel and gets luminance,
          // ∇h, and ∇²h from the same pass — the gradient is closed form, so
          // Refract costs no extra field evaluations here.
          double px = pixel.x * scale + OffsetX;
          double py = pixel.y * scale + OffsetY;
          v = LensField(px, py, t, refracting, out gx, out gy);
          gradGain = LensGradGain;
        } else {
          double px = pixel.x * scale + OffsetX;
          double py = pixel.y * scale + OffsetY;
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

    // ---- Tier 3: the ripple tank --------------------------------------------
    // A TankSize² damped wave-equation grid over the plan-view unit square
    // (leapfrog integration, reflective zero-gradient walls), stepped at a
    // fixed rate scaled by `speed` so the physics is frame-rate independent.
    // Shading is the same thin-lens focus form as Lens applied to the grid
    // Laplacian; Refract's side channels come from the grid's central
    // differences. Droplets (LayerTrigger fires) poke the water at the wand's
    // aim point; audio loudness stirs a churn of small random pokes. All
    // constants below were tuned against a scratch harness of this exact
    // update, not guessed — see the stats cited on each.

    private const int TankSize = 120;
    // Courant number squared (c·dt/dx)². 0.36 (C = 0.6) is comfortably inside
    // the 2D stability bound C ≤ 1/√2 and yields a wave speed of ~0.3
    // plane-units/s at speed 1 — a ring crosses the dome in ~3s.
    private const double TankC2 = 0.36;
    // Per-step displacement decay. At the 60 steps/s base rate this retains
    // ~0.84 amplitude per second: a droplet's rings stay readable for a few
    // seconds without the tank accumulating into a standing chop.
    private const double TankDamping = 0.997;
    // Base sim step rate at speed 1; `speed` (0–4) multiplies it. Step count
    // per frame is capped so a long stall can't burst the budget (the residual
    // is dropped, briefly slowing the water rather than freezing the app).
    private const double TankStepsPerSecond = 60;
    private const int TankMaxStepsPerFrame = 8;
    // Thin-lens shading of the grid Laplacian (numerator / focus / floor, the
    // Lens tier's form). At the churn+beat equilibrium the Laplacian's rms is
    // ~0.04 with p99 ~0.11 (measured), so focus 12 saturates ~3% of cells —
    // bright thin ringlets on dark, matching the Lens tier's sparse web.
    private const double TankBright = 0.2;
    private const double TankFocus = 12;
    private const double TankEps = 0.04;
    // Central-difference gradient magnitudes run rms ~0.15 / p99 ~0.33 at the
    // same equilibrium, so 2.5 spans alpha over 0..1 and saturates only on
    // the steepest wavefronts.
    private const double TankGradGain = 2.5;
    // Droplet depth (the Gaussian poke a trigger fire injects) — the value
    // the shading constants above were tuned against.
    private const double TankDropAmp = 0.75;
    // Churn: per sim step, one small random poke of amplitude
    // TankChurnAmp · volume² · rand — silence leaves the water still, loud
    // passages keep it shimmering between beats.
    private const double TankChurnAmp = 0.12;
    private const double TankChurnRadius = 2.5;
    // Random offset applied to each droplet's landing point so consecutive
    // beat drops at an idle (slowly wandering) aim point don't stack into a
    // standing bullseye.
    private const double TankDropJitter = 0.04;
    // Ignore sub-pixel orientation jitter, and re-baseline rather than drawing
    // a slash across the tank after calibration, reconnect, or a long stall.
    private const double TankWakeMinTravel = 0.001;
    private const double TankWakeMaxTravel = 0.25;
    // Converts the user-facing 0..1 Wake Strength into displacement. Sweeping
    // overlapping Gaussian presses along the travelled segment then produces
    // a bow wave plus a readable trailing wake without drawing the object.
    private const double TankWakeAmp = 0.32;

    // Sim state, allocated on the first Ripple Tank frame so the analytic
    // tiers never pay the ~600KB. tankCur/tankPrev are the leapfrog pair;
    // tankLap/tankGradX/tankGradY are per-cell derived fields refreshed only
    // when the surface changed (the dirty flags) — at 400Hz engine ticks the
    // sim steps ~once per 6–7 frames, so most frames just re-sample.
    private double[] tankCur, tankPrev;
    private double[] tankLap, tankGradX, tankGradY;
    private bool tankLapDirty = true, tankGradDirty = true;
    private double tankStepAccumulator;
    // Baked bilinear sampling of the grid at each pixel's plan position:
    // top-left cell index + fractional weights (pixel x/y are baked geometry,
    // so this never changes).
    private int[] tankCell;
    private double[] tankWeightX, tankWeightY;
    // Same edge-detect + first-frame-baseline idiom as
    // LayerTrigger.ManualFired, against config.domeLayerClearCounters (the 🧹
    // button): a clear flattens the water.
    private int lastClearCounter = -1;

    private struct TankObjectState {
      public double X;
      public double Y;
      public long SeenFrame;
    }
    // One previous projected position per live orientation device. This is
    // deliberately local to the layer: the tank needs trajectory history,
    // while OrientationCenter exposes only the shared current aim point.
    private readonly Dictionary<int, TankObjectState> tankObjects =
      new Dictionary<int, TankObjectState>();
    private readonly List<int> staleTankObjects = new List<int>();
    private long tankObjectFrame;
    // Device ids are one byte on the wire, so this cannot collide with a real
    // sensor. It lets the shared idle OrientationCenter carry trajectory state
    // through the exact same wake path as a physical input.
    private const int TankIdleObjectId = -1;

    // One frame of tank upkeep, called only in the Ripple Tank tier: handle
    // clear, fire droplets, stir churn, advance the fixed-step sim by the
    // elapsed wall time, and refresh the derived fields the pixel loop
    // samples.
    private void TankAdvance(
      IList<DomeLayerSettings> stack, double scale, double speed,
      double elapsed, bool refracting
    ) {
      this.TankEnsureAllocated();

      if (this.TankClearRequested()) {
        Array.Clear(this.tankCur, 0, this.tankCur.Length);
        Array.Clear(this.tankPrev, 0, this.tankPrev.Length);
        this.tankObjects.Clear();
        this.tankLapDirty = true;
        this.tankGradDirty = true;
      }

      int triggerSource =
        (int)DomeLayerSettings.ParamValue(stack, this.LayerKey, "trigger");
      int button =
        (int)DomeLayerSettings.ParamValue(stack, this.LayerKey, "button");
      double levelThreshold =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "level");
      double interval =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "interval");
      double wakeSize =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "wakeSize");
      double wakeStrength =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "wakeStrength");

      double level = this.audio.Volume;
      this.center.Update(level);
      // Fired() must run every frame while the tank is live so no source's
      // edge is missed (docs/triggers.md).
      bool fired =
        this.trigger.Fired(button, triggerSource, levelThreshold, interval);
      if (fired) {
        // The wand's aim in plan view: the sphere point CurrentCenter maps
        // onto Spot (ShootingStar's AimPoint), projected back through
        // BakePixelPositions' frame (x = 2px−1, y = 1−2py). With no wand
        // moving, OrientationCenter's idle drift wanders the drop point.
        Vector3 aim = Vector3.Transform(
          OrientationCenter.Spot,
          Quaternion.Conjugate(this.center.CurrentCenter)
        );
        double dropX = (aim.X + 1) / 2
          + (this.rand.NextDouble() - .5) * 2 * TankDropJitter;
        double dropY = (1 - aim.Y) / 2
          + (this.rand.NextDouble() - .5) * 2 * TankDropJitter;
        // `scale` maps inversely to droplet size, floored at ~2.5 cells so a
        // drop never injects grid-Nyquist energy (which renders as sparkle,
        // not rings — the LED-pitch constraint in docs/caustics.md).
        this.TankDrop(dropX, dropY, this.TankDropRadius(scale), TankDropAmp);
      }

      // Unlike the trigger droplet above, wakes use every eligible orientation
      // input independently. Respect the global spotlight exactly as the other
      // orientation layers do: a moving selected wand is solo; an unavailable
      // selection falls back to all moving wands; -2 forces idle/no objects.
      this.TankMoveObjects(wakeSize, wakeStrength, elapsed);

      this.tankStepAccumulator += elapsed * TankStepsPerSecond * speed;
      int steps = (int)this.tankStepAccumulator;
      if (steps > TankMaxStepsPerFrame) {
        steps = TankMaxStepsPerFrame;
        this.tankStepAccumulator = 0;
      } else {
        this.tankStepAccumulator -= steps;
      }
      for (int s = 0; s < steps; s++) {
        this.TankChurn(level);
        this.TankStep();
      }

      this.TankRefreshDerived(refracting);
    }

    private void TankMoveObjects(
      double wakeSize, double wakeStrength, double elapsed
    ) {
      this.tankObjectFrame++;
      IReadOnlyDictionary<int, OrientationDevice> devices =
        this.orientationInput.OperatorFrameDevices;
      int spotlight = this.config.orientationDeviceSpotlight;
      bool spotlightMoving = spotlight >= 0
        && devices.TryGetValue(spotlight, out OrientationDevice selected)
        && selected.isMoving;
      bool movedRealObject = false;

      if (spotlight != -2 && wakeStrength > 0) {
        foreach (var kvp in devices) {
          if (!kvp.Value.isMoving
              || (spotlightMoving && kvp.Key != spotlight)) {
            continue;
          }

          Vector3 aim = Vector3.Transform(
            OrientationCenter.Spot,
            Quaternion.Conjugate(kvp.Value.currentRotation())
          );
          double x = Math.Clamp((aim.X + 1) / 2, 0, 1);
          double y = Math.Clamp((1 - aim.Y) / 2, 0, 1);
          this.TankMoveObject(
            kvp.Key, x, y, wakeSize, wakeStrength, elapsed);
          movedRealObject = true;
        }
      }

      // OrientationCenter owns the installation's idle screen-saver drift.
      // When no physical input is eligible (including connected-but-still
      // sensors, which OrientationCenter classifies as idle), treat that
      // wandering aim as one virtual object so quiet installations still make
      // gentle, coherent waves rather than leaving the tank motionless.
      if (!movedRealObject && this.center.Idle && wakeStrength > 0) {
        Vector3 idleAim = Vector3.Transform(
          OrientationCenter.Spot,
          Quaternion.Conjugate(this.center.CurrentCenter)
        );
        this.TankMoveObject(
          TankIdleObjectId,
          Math.Clamp((idleAim.X + 1) / 2, 0, 1),
          Math.Clamp((1 - idleAim.Y) / 2, 0, 1),
          wakeSize, wakeStrength, elapsed);
      }

      // Forget disconnected, stationary, spotlight-filtered, and idle-forced
      // objects. If one becomes eligible again it starts cleanly at its new
      // position instead of drawing a stale cross-tank wake.
      this.staleTankObjects.Clear();
      foreach (var kvp in this.tankObjects) {
        if (kvp.Value.SeenFrame != this.tankObjectFrame) {
          this.staleTankObjects.Add(kvp.Key);
        }
      }
      for (int i = 0; i < this.staleTankObjects.Count; i++) {
        this.tankObjects.Remove(this.staleTankObjects[i]);
      }
    }

    private void TankMoveObject(
      int id, double x, double y, double wakeSize, double wakeStrength,
      double elapsed
    ) {
      if (!this.tankObjects.TryGetValue(id, out TankObjectState state)) {
        this.tankObjects[id] = new TankObjectState {
          X = x, Y = y, SeenFrame = this.tankObjectFrame,
        };
        return;
      }

      double dx = x - state.X, dy = y - state.Y;
      double travel = Math.Sqrt(dx * dx + dy * dy);
      // elapsed protects reactivation after the layer has been dormant;
      // travel protects calibration jumps and reconnect discontinuities.
      if (elapsed > 0.25 || travel > TankWakeMaxTravel) {
        state.X = x;
        state.Y = y;
      } else if (travel >= TankWakeMinTravel) {
        this.TankSweepWake(
          state.X, state.Y, x, y, travel, wakeSize, wakeStrength);
        state.X = x;
        state.Y = y;
      }
      // For sub-threshold travel, retain the old position so slow motion
      // accumulates until it is distinguishable from sensor/frame jitter.
      state.SeenFrame = this.tankObjectFrame;
      this.tankObjects[id] = state;
    }

    private void TankSweepWake(
      double x0, double y0, double x1, double y1, double travel,
      double wakeSize, double wakeStrength
    ) {
      double radiusCells = Math.Clamp(
        wakeSize * (TankSize - 1), 2.5, 18);
      // Half-radius spacing overlaps the presses into one continuous swept
      // object. Very short moves get proportionally less energy; longer paths
      // add presses, making wake energy track distance travelled.
      double spacing = Math.Max(0.5 * radiusCells / (TankSize - 1), 0.005);
      int samples = Math.Max(1, (int)Math.Ceiling(travel / spacing));
      double amp = TankWakeAmp * wakeStrength
        * Math.Min(1, travel / spacing);
      for (int i = 1; i <= samples; i++) {
        double p = (double)i / samples;
        this.TankDrop(
          x0 + (x1 - x0) * p,
          y0 + (y1 - y0) * p,
          radiusCells,
          amp);
      }
    }

    private double TankDropRadius(double scale) {
      double radius = 56 / Math.Max(scale, 1);
      return Math.Clamp(radius, 2.5, 12);
    }

    private void TankEnsureAllocated() {
      if (this.tankCur != null) {
        return;
      }
      int cells = TankSize * TankSize;
      this.tankCur = new double[cells];
      this.tankPrev = new double[cells];
      this.tankLap = new double[cells];
      this.tankGradX = new double[cells];
      this.tankGradY = new double[cells];

      int n = this.buffer.pixels.Length;
      this.tankCell = new int[n];
      this.tankWeightX = new double[n];
      this.tankWeightY = new double[n];
      for (int i = 0; i < n; i++) {
        double gx = Math.Clamp(this.buffer.pixels[i].x, 0, 1) * (TankSize - 1);
        double gy = Math.Clamp(this.buffer.pixels[i].y, 0, 1) * (TankSize - 1);
        int ix = Math.Min((int)gx, TankSize - 2);
        int iy = Math.Min((int)gy, TankSize - 2);
        this.tankCell[i] = iy * TankSize + ix;
        this.tankWeightX[i] = gx - ix;
        this.tankWeightY[i] = gy - iy;
      }
    }

    private bool TankClearRequested() {
      int counter = 0;
      this.config.domeLayerClearCounters?.TryGetValue(
        this.LayerKey, out counter);
      if (this.lastClearCounter == -1) {
        this.lastClearCounter = counter;
        return false;
      }
      bool cleared = counter != this.lastClearCounter;
      this.lastClearCounter = counter;
      return cleared;
    }

    // One leapfrog step: the next field is written into tankPrev (overwriting
    // the oldest state), then the two arrays swap roles. Walls are
    // zero-gradient (reflective), so rings bounce off the tank edge rather
    // than draining energy into a dark rim.
    private void TankStep() {
      double[] prev = this.tankPrev;
      double[] cur = this.tankCur;
      for (int y = 1; y < TankSize - 1; y++) {
        int row = y * TankSize;
        for (int x = 1; x < TankSize - 1; x++) {
          int i = row + x;
          double lap = cur[i - 1] + cur[i + 1]
            + cur[i - TankSize] + cur[i + TankSize] - 4 * cur[i];
          prev[i] = TankDamping * (2 * cur[i] - prev[i] + TankC2 * lap);
        }
      }
      for (int x = 0; x < TankSize; x++) {
        prev[x] = prev[x + TankSize];
        prev[(TankSize - 1) * TankSize + x] = prev[(TankSize - 2) * TankSize + x];
      }
      for (int y = 0; y < TankSize; y++) {
        prev[y * TankSize] = prev[y * TankSize + 1];
        prev[y * TankSize + TankSize - 1] = prev[y * TankSize + TankSize - 2];
      }
      this.tankPrev = cur;
      this.tankCur = prev;
      this.tankLapDirty = true;
      this.tankGradDirty = true;
    }

    // Press a Gaussian dimple of the given depth and radius (grid cells) into
    // the surface at plan position (cx, cy) ∈ [0,1]². Displacement only — the
    // leapfrog pair's velocity is untouched, so the dimple relaxes outward as
    // rings.
    private void TankDrop(double cx, double cy, double radiusCells, double amp) {
      double gx = cx * (TankSize - 1), gy = cy * (TankSize - 1);
      int reach = (int)(3 * radiusCells);
      double s2 = 2 * radiusCells * radiusCells;
      int x0 = Math.Max(1, (int)gx - reach);
      int x1 = Math.Min(TankSize - 2, (int)gx + reach);
      int y0 = Math.Max(1, (int)gy - reach);
      int y1 = Math.Min(TankSize - 2, (int)gy + reach);
      for (int y = y0; y <= y1; y++) {
        for (int x = x0; x <= x1; x++) {
          double dx = x - gx, dy = y - gy;
          this.tankCur[y * TankSize + x] -=
            amp * Math.Exp(-(dx * dx + dy * dy) / s2);
        }
      }
      this.tankLapDirty = true;
      this.tankGradDirty = true;
    }

    // Loudness stirs the water: one small poke per sim step at a random point
    // in the plan disc, with amplitude ~ volume² so quiet passages barely
    // ripple and loud ones churn.
    private void TankChurn(double level) {
      if (level <= 0.01) {
        return;
      }
      double a, b;
      do {
        a = this.rand.NextDouble() - .5;
        b = this.rand.NextDouble() - .5;
      } while (a * a + b * b > 0.2);
      this.TankDrop(
        a + .5, b + .5, TankChurnRadius,
        TankChurnAmp * level * level * this.rand.NextDouble());
    }

    // Refresh the per-cell Laplacian (always) and central-difference gradient
    // (only when Refract will read it) after the surface changed. Border
    // cells copy their interior neighbor, consistent with the zero-gradient
    // walls.
    private void TankRefreshDerived(bool wantGrad) {
      bool needLap = this.tankLapDirty;
      bool needGrad = wantGrad && this.tankGradDirty;
      if (!needLap && !needGrad) {
        return;
      }
      double[] u = this.tankCur;
      for (int y = 1; y < TankSize - 1; y++) {
        int row = y * TankSize;
        for (int x = 1; x < TankSize - 1; x++) {
          int i = row + x;
          this.tankLap[i] = u[i - 1] + u[i + 1]
            + u[i - TankSize] + u[i + TankSize] - 4 * u[i];
          if (needGrad) {
            this.tankGradX[i] = (u[i + 1] - u[i - 1]) * 0.5;
            this.tankGradY[i] = (u[i + TankSize] - u[i - TankSize]) * 0.5;
          }
        }
      }
      CopyEdgesFromInterior(this.tankLap);
      if (needGrad) {
        CopyEdgesFromInterior(this.tankGradX);
        CopyEdgesFromInterior(this.tankGradY);
        this.tankGradDirty = false;
      }
      this.tankLapDirty = false;
    }

    private static void CopyEdgesFromInterior(double[] f) {
      for (int x = 0; x < TankSize; x++) {
        f[x] = f[x + TankSize];
        f[(TankSize - 1) * TankSize + x] = f[(TankSize - 2) * TankSize + x];
      }
      for (int y = 0; y < TankSize; y++) {
        f[y * TankSize] = f[y * TankSize + 1];
        f[y * TankSize + TankSize - 1] = f[y * TankSize + TankSize - 2];
      }
    }

    // Bilinear sample of a per-cell field at pixel i's baked grid position.
    private double TankSampleAt(double[] field, int i) {
      int c = this.tankCell[i];
      double wx = this.tankWeightX[i], wy = this.tankWeightY[i];
      double top = field[c] + (field[c + 1] - field[c]) * wx;
      double bottom = field[c + TankSize]
        + (field[c + TankSize + 1] - field[c + TankSize]) * wx;
      return top + (bottom - top) * wy;
    }
  }
}
