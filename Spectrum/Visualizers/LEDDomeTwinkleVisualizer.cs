using Spectrum.Base;
using Spectrum.LEDs;
using System;

namespace Spectrum.Visualizers {

  // Dense bright white dots that flicker on at random high on the dome and fade
  // out — the twinkle effect lifted out of the Quaternion Paintbrush as its own
  // stack layer. It is the minimal proof of the layer-disassembly approach
  // (docs/layers_inventory.md): center-free and input-free, it references no
  // orientation and no audio, so it composites over any backdrop under Add or
  // Lighten with nothing shared but the buffer.
  //
  // The timing (per-frame fade and the twinkle probability) is frame-rate
  // independent via the same frameScale machinery Paintbrush uses, so the
  // dots-per-second rate and fade speed match what they were inside Paintbrush
  // regardless of how fast the Operator loop ticks.
  class LEDDomeTwinkleVisualizer : DomeLayerVisualizer {

    // Frame-rate independence, matching Paintbrush's ApplyGlobalEffects: every
    // per-frame advance is scaled by frameScale = elapsed / NOMINAL_FRAME so the
    // effect evolves at a consistent wall-clock speed. MAX_FRAME_SCALE caps the
    // catch-up after a stall (or the very first frame).
    private const double NOMINAL_FPS = 120;
    private const double NOMINAL_FRAME_SECONDS = 1 / NOMINAL_FPS;
    private const double MAX_FRAME_SCALE = 5;

    private readonly Configuration config;
    private readonly LEDDomeOutput dome;
    private readonly LEDDomeOutputBuffer buffer;
    private readonly Random rand;

    // Baked once: whether each pixel is high enough on the dome to twinkle
    // (unit-sphere z > .2), the same eligibility test Paintbrush used.
    private readonly bool[] twinkleEligible;

    // Wall-clock frame timing (see Paintbrush's UpdateFrameScale).
    private readonly System.Diagnostics.Stopwatch frameTimer =
      new System.Diagnostics.Stopwatch();
    private double frameScale = 1;

    public LEDDomeTwinkleVisualizer(
      Configuration config,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeOutputBuffer();
      this.rand = new Random();

      // Bake per-pixel twinkle eligibility once. The pixel mapping has x,y come
      // "out of" the top-left corner, so y is flipped (as in Paintbrush).
      this.twinkleEligible = new bool[buffer.pixels.Length];
      for (int i = 0; i < buffer.pixels.Length; i++) {
        var p = buffer.pixels[i];
        double x = 2 * p.x - 1;
        double y = 1 - 2 * p.y;
        double z = (x * x + y * y) > 1 ? 0 : Math.Sqrt(1 - x * x - y * y);
        this.twinkleEligible[i] = z > .2;
      }
    }

    public int Priority {
      get {
        return DomeLayerSettings.StackActivates(
          this.config.domeLayerStack, "twinkle"
        ) ? 2 : 0;
      }
    }

    public string LayerKey => "twinkle";
    public LEDDomeOutputBuffer LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    // Center-free and input-free: the twinkle has no data source of its own.
    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { });
    }

    public void Visualize() {
      UpdateFrameScale();

      // Fade the whole buffer so lit dots trail off, matching Paintbrush's
      // per-frame fade compounded over frameScale frames (see ApplyGlobalEffects
      // for the retention-factor derivation).
      double frameRetention = 1 - Math.Pow(5, -this.config.domeGlobalFadeSpeed);
      this.buffer.Fade(Math.Pow(frameRetention, this.frameScale), 0);

      // Twinkle is a per-pixel chance each frame; scale the probability by
      // frameScale to hold the twinkle rate (dots per second) steady. Density
      // is this layer's own param (formerly the global domeTwinkleDensity).
      double twinkleDensity = DomeLayerSettings.ParamValue(
        this.config.domeLayerStack, this.LayerKey, "density"
      ) * this.frameScale;
      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        if (this.twinkleEligible[i] && this.rand.NextDouble() < twinkleDensity) {
          this.buffer.pixels[i].color = 0xFFFFFF;
        }
      }
    }

    // Measures real time since the previous frame and converts it into
    // nominal-frame units (frameScale). Mirrors Paintbrush's UpdateFrameScale.
    private void UpdateFrameScale() {
      if (!this.frameTimer.IsRunning) {
        this.frameTimer.Restart();
        this.frameScale = 1;
        return;
      }
      double elapsedSeconds = this.frameTimer.Elapsed.TotalSeconds;
      this.frameTimer.Restart();
      this.frameScale = Clamp(
        elapsedSeconds / NOMINAL_FRAME_SECONDS, 0, MAX_FRAME_SCALE
      );
    }

    private static double Clamp(double x, double a, double b) {
      if (x < a) return a;
      if (x > b) return b;
      return x;
    }
  }
}
