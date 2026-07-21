using Spectrum.Base;
using Spectrum.LEDs;
using System;
using static Spectrum.MathUtil;

namespace Spectrum.Visualizers {

  // Sweeps a band across the dome. It emits a *mask*, not color: inside the band
  // a pixel is opaque white (alpha 1), outside it is fully transparent (alpha 0,
  // "reveal below"). On its own the layer is degenerate — an adjustment layer
  // needs a layer beneath it. Its point is to drive an adjustment blend (e.g.
  // Desaturate) so the band shows whatever is below it, processed: the band's
  // shape is the wave's job, the desaturation is the blend's (see the
  // two-consumer split in docs/layer_params_implementation.md).
  //
  // Per-layer params (visualizer-consumed, read from this instance's compiled
  // runtime): bandWidth, sweep speed, the sweep center (angle/distance,
  // same knobs as Radial Effects), and the band's color. The band always
  // sweeps by height (distance from the center point, flipped so 1 is at the
  // center/top). Edges are hard by design — no partial alpha.
  class LEDDomeWaveVisualizer : DomeLayerVisualizer {

    private readonly DomeLayerEnvironment environment;
    private readonly LayerRendererRuntime runtime;
    private readonly OrientationInput orientationInput;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;
    private readonly LayerTrigger trigger;

    // Sweep phase (the band center, 0..1) accumulated from wall-clock time so the
    // sweep speed is frame-rate independent.
    private readonly System.Diagnostics.Stopwatch frameTimer =
      new System.Diagnostics.Stopwatch();
    private double phase;

    // OneShot playback state (docs/triggers.md): whether the band is
    // currently mid-sweep. Loop mode never touches this.
    private bool playing;

    public LEDDomeWaveVisualizer(
      DomeLayerEnvironment environment,
      LayerRendererRuntime runtime,
      OrientationInput orientationInput,
      DomeRenderContext dome
    ) {
      this.environment = environment;
      this.runtime = runtime;
      this.orientationInput = orientationInput;
      this.dome = dome;
      this.buffer = this.dome.MakeDomeFrame();
      this.trigger = new LayerTrigger(
        environment, orientationInput, runtime.InstanceId);
    }

    public int Priority => 2;

    public string LayerKey => "wave";
    public DomeFrame LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    // OrientationInput is declared unconditionally (docs/triggers.md "Note on
    // Wave's inputs") so LayerTrigger's Button source can read wand state.
    // It's AlwaysActive and always Enabled, so this never gates eligibility
    // even when trigger != Button.
    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.orientationInput });
    }

    public void Visualize() {
      WaveLayerOptions options = this.runtime.GetOptions<WaveLayerOptions>();
      double bandWidth = options.BandWidth;
      double speed = options.Speed;
      double centerAngle = options.CenterAngle;
      double centerDistance = options.CenterDistance;
      int color = options.Color;
      bool oneShot = options.OneShot;
      // The "button" enum index is the wand actionFlag value directly: 0 =
      // Unbound (Manual-only), 1/2/3 = wand buttons. Manual firing is always
      // live regardless (LayerTrigger).
      int button = options.Button;

      double elapsed = 0;
      if (!this.frameTimer.IsRunning) {
        this.frameTimer.Restart();
      } else {
        elapsed = this.frameTimer.Elapsed.TotalSeconds;
        this.frameTimer.Restart();
      }

      // Fired() must run every frame regardless of playback state, so an
      // edge occurring mid-playthrough (or while OneShot sits idle) is never
      // missed (docs/triggers.md "button edge-detection subtlety").
      bool fired = this.trigger.Fired(button);

      if (oneShot) {
        if (fired) {
          this.playing = true;
          // Restart at phase 0, where the band sits entirely just past the rim
          // (see the targetDist construction below), so the sweep reads as a
          // wave arriving from outside the dome rather than a band flashing
          // into existence at the rim. It sweeps to phase 1, where the band has
          // passed entirely through the center.
          this.phase = 0;
        }
        if (this.playing) {
          this.phase += Math.Abs(speed) * elapsed;
          if (this.phase >= 1) {
            this.phase = 1;
            this.playing = false;
          }
        }
        if (!this.playing) {
          // Idle: reveal the layers below unchanged until re-fired.
          for (int i = 0; i < this.buffer.pixels.Length; i++) {
            this.buffer.pixels[i].Clear();
          }
          return;
        }
      } else {
        // Advance the band center by speed (cycles/second) * elapsed wall
        // time, wrapped into 0..1. Speed may be negative (sweep the other
        // way).
        this.phase = Frac(this.phase + speed * elapsed);
      }
      double center = this.phase;

      // Same center-offset construction as Radial: shift the unit-square
      // coordinates by a point at (centerAngle, centerDistance) before
      // recovering the per-pixel height, so the sweep can be re-centered off
      // the dome's origin.
      var centerOffset = StrutLayoutFactory.PolarToCartesian(
        centerAngle, centerDistance
      );

      // The distance to the dome's outer edge is only ~1 (the rim) when the
      // center sits at the dome's origin. Once it's offset toward the rim, the
      // far side of the dome sits progressively farther out (up to ~2 with the
      // center on the rim), so we can't assume the band starts its travel at
      // dist 1. Measure the actual maximum distance from the (possibly offset)
      // center across the current pixels, and base the sweep's travel on that.
      // Without this, an offset OneShot would start already inside the dome —
      // leaving the outer pixels never swept — and an offset looping sweep would
      // space its wrapped ring copies too tightly, lighting a phantom band on
      // the far side while the main band is on-screen.
      double maxDist = 0;
      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        DomeTopologyPixel point = this.buffer.Topology.PixelAt(i);
        double px = (point.X + centerOffset.Item1) * 2 - 1;
        double py = (point.Y + centerOffset.Item2) * 2 - 1;
        double dist = Math.Sqrt(px * px + py * py);
        if (dist > maxDist) {
          maxDist = dist;
        }
      }

      // maxDist is the measured farthest pixel, so nothing sits beyond it; the
      // extra edgeFudge margin keeps the off-screen ends (and the wrapped ring
      // copy that arrives as the sweep completes) strictly clear of that
      // farthest pixel rather than grazing it right at the end of the period.
      const double edgeFudge = 0.05;

      // The band's full travel is slightly wider than the dome: its center ring
      // (targetDist, in the same per-pixel distance units where 0 is the center
      // point) runs from maxDist + bandWidth + edgeFudge (the whole band just
      // past the outer edge, off-screen) at center 0 to -bandWidth (just past
      // the center point, off-screen) at center 1. Because bandWidth sits
      // *inside* this total width, the band is fully hidden at both ends — so a
      // OneShot enters cleanly from outside the dome and exits cleanly through
      // the center, and a looping sweep wraps seamlessly. This period also sets
      // the spacing of the wrapped ring copies (below): one
      // outer-edge-plus-band-plus-fudge apart, so every copy is off-screen (its
      // inner edge at dist maxDist + edgeFudge) whenever the main band is —
      // which stops a phantom ring from lighting near the center at trigger time
      // and stops edge pixels lighting as it ends.
      double period = maxDist + 2 * bandWidth + edgeFudge;
      double targetDist = (maxDist + bandWidth + edgeFudge) - center * period;

      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        ref var pixel = ref this.buffer.pixels[i];
        DomeTopologyPixel point = this.buffer.Topology.PixelAt(i);
        double px = (point.X + centerOffset.Item1) * 2 - 1;
        double py = (point.Y + centerOffset.Item2) * 2 - 1;
        // Raw, unbounded distance from the (possibly offset) center — same
        // quantity Radial's Pulse mapping uses. It is never clamped: once the
        // center is offset, dome pixels can legitimately sit past dist 1, and
        // collapsing them all to one boundary value is what previously made
        // a whole swath of the dome light up together at high centerDistance.
        double dist = Math.Sqrt(px * px + py * py);

        // Fold dist into one period around targetDist (as Pulse does), so the
        // math stays correct however far dist extends past 1 once the center is
        // offset. val is normalized to 0..1 across a full period, so the band
        // half-width bandWidth is compared in those same normalized units below.
        double val = MapWrap(dist, targetDist, targetDist + period, 0, 1);
        // Distance from the nearest ring, normalized: 0 right at the ring, 0.5
        // halfway between consecutive rings one period apart.
        double d = Math.Min(val, 1 - val);
        if (d < bandWidth / period) {
          // Opaque mask: the alpha (1) is what an adjustment blend reads; the
          // color is irrelevant to Desaturate but matters if the layer runs
          // under a color blend (e.g. Over), so it's tunable via the "color"
          // param (default white, matching the old hard-coded reveal-all).
          pixel.color = color;
        } else {
          // Transparent: reveal the layers below unchanged (alpha 0).
          pixel.Clear();
        }
      }
    }

    // Positive fractional part, so a wrapped phase/coordinate stays in [0,1).
    private static double Frac(double x) {
      double f = x - Math.Floor(x);
      return f;
    }
  }
}
