using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;

namespace Spectrum.Visualizers {

  // The orientation-derived potential field, lifted out of Quaternion
  // Paintbrush as its own stack layer (docs/layers_inventory.md — the fourth
  // effect pulled out, and the last piece of the disassembly: metaball and
  // contour are extracted together rather than as two layers, per the
  // inventory doc's "Metaball + contour stay one layer" decision — contours
  // are level curves of the *same* potential field the metaball already
  // computes, so splitting them would recompute the field twice for no
  // benefit).
  //
  // Uses the orientation-derived palette (OrientationCenter.HueFromColorCenter),
  // sampled live per pixel every frame — there is no center-binding choice
  // here (unlike Ripple/Stamp): the field itself, not a single frozen point,
  // is what's drawn.
  class LEDDomeMetaballVisualizer : DomeLayerVisualizer {

    private readonly Configuration config;
    private readonly LayerRendererRuntime runtime;
    private readonly AudioInput audio;
    private readonly OrientationInput orientationInput;
    private readonly LEDDomeOutput dome;
    private readonly DomeFrame buffer;
    private readonly OrientationCenter center;
    private readonly LayerTrigger trigger;

    // Static per-pixel geometry, baked once (mirrors Paintbrush's mapping).
    private readonly ImmutableArray<Vector3> pixelPositions;

    private readonly FrameClock frameClock = new FrameClock();

    // Contour state, unchanged from the fused version: a slow phase that
    // pulses the level-curve bands over time, scaled by loudness.
    private double contourCounter = 0;

    // Burst envelope (docs/triggers.md): 1 right on a trigger fire, decaying
    // to 0 over BURST_DURATION_FRAMES nominal frames. Replaces the old
    // hard-coded per-device "bonus" that used to live in OrientationCenter —
    // that boosted one wand's contribution to the potential field for as
    // long as its button stayed held; this instead flashes the whole field's
    // threshold on an edge-triggered fire, independent of any one device.
    private const double BURST_DURATION_FRAMES = 10;
    private const double BURST_SIZE_BOOST = 4;
    private double burstEnvelope = 0;

    public LEDDomeMetaballVisualizer(
      Configuration config,
      LayerRendererRuntime runtime,
      AudioInput audio,
      OrientationInput orientationInput,
      OrientationCenter center,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.runtime = runtime;
      this.audio = audio;
      this.orientationInput = orientationInput;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeFrame();
      this.center = center;
      this.trigger = new LayerTrigger(
        config, orientationInput, runtime.InstanceId.Value);

      // Bake the static unit-sphere position of every pixel once.
      this.pixelPositions = this.buffer.BakePixelPositions();
    }

    public int Priority => 2;

    public string LayerKey => "metaball";
    public DomeFrame LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.orientationInput });
    }

    public void Visualize() {
      double frameScale = this.frameClock.Tick();
      double level = this.audio.Volume;

      MetaballLayerOptions options =
        this.runtime.GetOptions<MetaballLayerOptions>();
      double size = options.Size;
      bool showContours = options.ShowContours;
      int button = options.Button;

      double frameRetention = 1 - Math.Pow(5, -this.config.domeGlobalFadeSpeed);
      this.buffer.Fade(Math.Pow(frameRetention, frameScale), 0);

      // Fired() must run every frame regardless of burst state, so an edge
      // occurring mid-decay is never missed (docs/triggers.md "button
      // edge-detection subtlety").
      bool fired = this.trigger.Fired(button);
      if (fired) {
        this.burstEnvelope = 1;
      } else {
        this.burstEnvelope =
          Math.Max(0, this.burstEnvelope - frameScale / BURST_DURATION_FRAMES);
      }
      double burstSize = size + BURST_SIZE_BOOST * this.burstEnvelope;

      this.center.Update(level);
      UpdateContour(level, showContours, frameScale);
      RenderPixels(level, burstSize, showContours);
    }

    // Pulses the contour lines over time, scaled by loudness (unchanged from
    // the fused version); a no-op when contours are off, mirroring the fused
    // guard around config.orientationShowContours.
    private void UpdateContour(double level, bool showContours, double frameScale) {
      if (!showContours) {
        return;
      }
      this.contourCounter += 4 * level * frameScale;
      if (this.contourCounter >= 100) {
        this.contourCounter = 0;
      }
    }

    private void RenderPixels(double level, double size, bool showContours) {
      double thresholdFactor = (size / 4) + level + .01;
      double threshold = 2 / thresholdFactor;
      // Only the very top end of the volume range desaturates the metaball;
      // below DESAT_START it stays fully saturated.
      const double DESAT_START = .85;
      double desatAmount = Math.Clamp((level - DESAT_START) / (1 - DESAT_START), 0, 1);
      double metaballSaturation = 1 - .8 * desatAmount;

      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        Vector3 pixelPoint = this.pixelPositions[i];

        double potential = this.center.PotentialAt(pixelPoint, out Quaternion colorCenter);
        double metaballHue = OrientationCenter.HueFromColorCenter(colorCenter);

        // Published every pixel, every frame, regardless of whether this
        // pixel is drawn below — the field has a hue everywhere, and a
        // future hue-inherit blend mode should be able to read it without
        // depending on this layer's own visible coverage.
        this.buffer.pixels[i].hue = metaballHue;

        bool drawn = false;
        int best = 0;
        double bestValue = 0;

        // Crisp metaball: a hard cutoff at the threshold, always full value.
        if (potential - threshold > 0) {
          drawn = true;
          best = new Color(metaballHue, metaballSaturation, 1).ToInt();
          bestValue = 1;
        }

        // Contour - highlight level curves of the same potential field. The
        // log is only defined for potential > .5, and there is no contour to
        // draw below that (the field is near zero far from any wand's aim
        // point), so gate on it explicitly rather than relying on Math.Log
        // returning NaN and the NaN comparisons falling through.
        if (showContours && potential > .5) {
          double potentialContours = Math.Log(1000 * (potential - .5)) + this.contourCounter / 100;
          double contourBracket = Math.Truncate(potentialContours);
          double contourValue = potentialContours - contourBracket;
          if (contourValue < .2) {
            double value = .8 - Math.Clamp(1 - contourBracket / 10, 0, .8);
            if (!drawn || value > bestValue) {
              drawn = true;
              best = new Color(metaballHue, .4, value).ToInt();
              bestValue = value;
            }
          }
        }

        // Only write when an effect touched this pixel, so untouched pixels
        // keep the sub-integer precision their faded RGB accumulators carry.
        if (drawn) {
          this.buffer.pixels[i].color = best;
        }
      }
    }
  }
}
