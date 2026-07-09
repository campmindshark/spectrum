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
  // Per-layer params (visualizer-consumed, read each frame from this layer's own
  // stack entry): bandWidth, sweep speed, and the sweep center (angle/distance,
  // same knobs as Radial Effects). The band always sweeps by height (distance
  // from the center point, flipped so 1 is at the center/top). Edges are hard
  // by design — no partial alpha.
  class LEDDomeWaveVisualizer : DomeLayerVisualizer {

    private readonly Configuration config;
    private readonly LEDDomeOutput dome;
    private readonly LEDDomeOutputBuffer buffer;

    // Sweep phase (the band center, 0..1) accumulated from wall-clock time so the
    // sweep speed is frame-rate independent.
    private readonly System.Diagnostics.Stopwatch frameTimer =
      new System.Diagnostics.Stopwatch();
    private double phase;

    public LEDDomeWaveVisualizer(
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
          this.config.domeLayerStack, "wave"
        ) ? 2 : 0;
      }
    }

    public string LayerKey => "wave";
    public LEDDomeOutputBuffer LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    // Input-free: the band is time-driven, no audio/orientation source.
    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { });
    }

    public void Visualize() {
      var stack = this.config.domeLayerStack;
      double bandWidth =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "bandWidth");
      double speed =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "speed");
      double centerAngle =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "centerAngle");
      double centerDistance =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "centerDistance");

      // Advance the band center by speed (cycles/second) * elapsed wall time,
      // wrapped into 0..1. Speed may be negative (sweep the other way).
      if (!this.frameTimer.IsRunning) {
        this.frameTimer.Restart();
      } else {
        double elapsed = this.frameTimer.Elapsed.TotalSeconds;
        this.frameTimer.Restart();
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

      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        ref var pixel = ref this.buffer.pixels[i];
        double px = (pixel.x + centerOffset.Item1) * 2 - 1;
        double py = (pixel.y + centerOffset.Item2) * 2 - 1;
        // Raw, unbounded distance from the (possibly offset) center — same
        // quantity Radial's Pulse mapping uses. It is never clamped: once the
        // center is offset, dome pixels can legitimately sit past dist 1, and
        // collapsing them all to one boundary value is what previously made
        // a whole swath of the dome light up together at high centerDistance.
        double dist = Math.Sqrt(px * px + py * py);

        // Target ring radius sweeps from the rim (1) to the center (0) as
        // center goes 0..1. MapWrap (as in Pulse) folds dist - target into a
        // single period via modular arithmetic, so it stays correct no matter
        // how far dist extends past 1 — no special-casing needed.
        double val = MapWrap(dist, 1 - center, 2 - center, 0, 1);
        // Distance from the nearest ring: 0 right at the ring, 0.5 halfway
        // between consecutive rings one period apart.
        double d = Math.Min(val, 1 - val);
        if (d < bandWidth) {
          // Opaque white mask: the alpha (1) is what an adjustment blend reads;
          // the color is irrelevant to Desaturate but matters if the layer runs
          // under a color blend, so make it a plain reveal-all white.
          pixel.color = 0xFFFFFF;
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
