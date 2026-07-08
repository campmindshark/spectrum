using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
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
    private readonly AudioInput audio;
    private readonly OrientationInput orientationInput;
    private readonly LEDDomeOutput dome;
    private readonly LEDDomeOutputBuffer buffer;
    private readonly OrientationCenter center;

    // Static per-pixel geometry, baked once (mirrors Paintbrush's mapping).
    private readonly Vector3[] pixelPositions;

    private readonly FrameClock frameClock = new FrameClock();

    // Contour state, unchanged from the fused version: a slow phase that
    // pulses the level-curve bands over time, scaled by loudness.
    private double contourCounter = 0;

    public LEDDomeMetaballVisualizer(
      Configuration config,
      AudioInput audio,
      OrientationInput orientationInput,
      OrientationCenter center,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.audio = audio;
      this.orientationInput = orientationInput;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeOutputBuffer();
      this.center = center;

      // Bake the static unit-sphere position of every pixel once.
      this.pixelPositions = this.buffer.BakePixelPositions();
    }

    public int Priority {
      get {
        return DomeLayerSettings.StackActivates(
          this.config.domeLayerStack, "metaball"
        ) ? 2 : 0;
      }
    }

    public string LayerKey => "metaball";
    public LEDDomeOutputBuffer LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.orientationInput });
    }

    public void Visualize() {
      double frameScale = this.frameClock.Tick();
      double level = this.audio.Volume;

      IList<DomeLayerSettings> stack = this.config.domeLayerStack;
      double size =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "size");
      bool showContours =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "contours") != 0;

      double frameRetention = 1 - Math.Pow(5, -this.config.domeGlobalFadeSpeed);
      this.buffer.Fade(Math.Pow(frameRetention, frameScale), 0);

      this.center.Update(level);
      UpdateContour(level, showContours, frameScale);
      RenderPixels(level, size, showContours);
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
      // High volumes desaturate the metaball.
      double metaballSaturation = Math.Clamp(1.3 / level - 1, .2, 1);

      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        Vector3 pixelPoint = this.pixelPositions[i];

        double potential = this.center.PotentialAt(pixelPoint, out Quaternion colorCenter);
        double metaballHue = OrientationCenter.HueFromColorCenter(colorCenter);

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
