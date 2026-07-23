using Spectrum.Base;
using Spectrum.LEDs;
using System;

namespace Spectrum.Visualizers {

  // Dense bright white dots that flicker on at random high on the dome and fade
  // out — the twinkle effect lifted out of the Quaternion Paintbrush as its own
  // stack layer. It is the minimal proof of the layer-disassembly approach:
  // center-free and input-free, it references no
  // orientation and no audio, so it composites over any backdrop under Add or
  // Lighten with nothing shared but the buffer.
  //
  // The timing (per-frame fade and the twinkle probability) is frame-rate
  // independent via the same frameScale machinery Paintbrush uses, so the
  // dots-per-second rate and fade speed match what they were inside Paintbrush
  // regardless of how fast the Operator loop ticks.
  class LEDDomeTwinkleVisualizer : DomeLayerVisualizer {

    private readonly DomeLayerEnvironment environment;
    private readonly LayerRendererRuntime runtime;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;
    private readonly Random rand;

    // Baked once: whether each pixel is high enough on the dome to twinkle
    // (unit-sphere z > .2), the same eligibility test Paintbrush used.
    private readonly bool[] twinkleEligible;

    // Wall-clock frame timing shared with the orientation layers (see
    // FrameClock); scales the fade and twinkle probability so both hold a
    // steady wall-clock rate regardless of the Operator loop speed.
    private readonly FrameClock frameClock = new FrameClock();

    public LEDDomeTwinkleVisualizer(
      DomeLayerEnvironment environment,
      LayerRendererRuntime runtime,
      DomeRenderContext dome
    ) {
      this.environment = environment;
      this.runtime = runtime;
      this.dome = dome;
      this.buffer = this.dome.MakeDomeFrame();
      this.rand = new Random();

      // Bake per-pixel twinkle eligibility once from the baked unit-sphere
      // positions: a pixel twinkles when it sits high enough on the dome
      // (z > .2), the same eligibility test Paintbrush used.
      var pixelPositions = this.buffer.BakePixelPositions();
      this.twinkleEligible = new bool[pixelPositions.Length];
      for (int i = 0; i < pixelPositions.Length; i++) {
        this.twinkleEligible[i] = pixelPositions[i].Z > .2;
      }
    }

    public int Priority => 2;

    public string LayerKey => "twinkle";
    public DomeFrame LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    // Center-free and input-free: the twinkle has no data source of its own.
    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { });
    }

    public void Visualize() {
      double frameScale = this.frameClock.Tick();

      // Fade the whole buffer so lit dots trail off, matching Paintbrush's
      // per-frame fade compounded over frameScale frames (see ApplyGlobalEffects
      // for the retention-factor derivation).
      double frameRetention =
        1 - Math.Pow(5, -this.environment.GlobalFadeSpeed);
      this.buffer.Fade(Math.Pow(frameRetention, frameScale), 0);

      // Twinkle is a per-pixel chance each frame; scale the probability by
      // frameScale to hold the twinkle rate (dots per second) steady. Density
      // is this layer's own param (formerly the global domeTwinkleDensity).
      double twinkleDensity =
        this.runtime.GetOptions<TwinkleLayerOptions>().Density * frameScale;
      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        if (this.twinkleEligible[i] && this.rand.NextDouble() < twinkleDensity) {
          this.buffer.pixels[i].color = 0xFFFFFF;
        }
      }
    }
  }
}
