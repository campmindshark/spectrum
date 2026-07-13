using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Numerics;
using static Spectrum.MathUtil;

namespace Spectrum.Visualizers {

  // Paints the dome surface from the orientation of the dome "wands" (and an
  // idle screen-saver when no wand is moving). Each frame splits into two
  // phases: UpdateFrameState() advances the effect state machines and snapshots
  // the devices once, then RenderPixels() runs the single per-pixel loop.
  //
  // Performance notes:
  //  - Pixel positions are static (baked into the buffer at creation), so the
  //    unit-sphere coordinate for every pixel is precomputed once in the
  //    constructor instead of every frame.
  //  - Each device's currentRotation() (a quaternion inverse + multiply) is
  //    computed once per frame (in OrientationCenter, shared with any other
  //    orientation-driven layer), not once per pixel.
  //  - The per-pixel loop is allocation-free: every blend in it is "keep the
  //    brighter color" (the old BlendLightPaint), so we track the winning
  //    (hue,sat,val) in locals and pack to an int once, instead of allocating
  //    several Color objects per drawn pixel.
  class LEDDomeQuaternionPaintbrushVisualizer : DomeLayerVisualizer {

    // The "forward" direction calibration assigns to a wand. Opposite ends of
    // the hemisphere are identified, so we measure distance to both spot and
    // -spot (negSpot) and treat them as the same point. Mirrors
    // OrientationCenter.Spot/NegSpot (stamp/ripple geometry stays local here
    // rather than reading the helper's copy, to keep this file's per-pixel
    // loop self-contained).
    private static readonly Vector3 spot = OrientationCenter.Spot;
    private static readonly Vector3 negSpot = OrientationCenter.NegSpot;

    // Stamp tuning.
    private const int STAMP_INTERVAL = 1000;   // frames between possible stamps
    private const double STAMP_LEVEL = .3;     // loudness required to fire
    private const int STAMP_COOLDOWN = 10;     // render frames a stamp lasts
    private const int GRID_STAMP_CUTOFF = 7;   // grid stamp clears once below this

    // Ripple tuning.
    private const int RIPPLE_PERIOD = 1000;
    private const double RIPPLE_COOLDOWN_RESET = 100;

    private readonly Configuration config;
    private readonly LayerRendererRuntime runtime;
    private readonly AudioInput audio;
    private readonly OrientationInput orientationInput;
    private readonly BeatBroadcaster beat;
    private readonly LEDDomeOutput dome;
    private readonly LEDDomeOutputBuffer buffer;
    private readonly Random rand;

    // Device snapshotting, idle drift, spotlight resolution, and the
    // orientation-derived palette — shared with any other orientation-driven
    // layer (see OrientationCenter).
    private readonly OrientationCenter center;

    // Static per-pixel geometry, baked once: the unit-sphere position and
    // whether the pixel is high enough on the dome to twinkle (z > .2).
    private readonly Vector3[] pixelPositions;
    private readonly bool[] twinkleEligible;

    // Wall-clock frame timing, shared with the extracted orientation/twinkle
    // layers (see FrameClock). Tick() returns frameScale — nominal-frame units
    // since the previous Visualize() — which every per-frame state advance below
    // is multiplied by.
    private readonly FrameClock frameClock = new FrameClock();

    // Stamp state.
    private Quaternion stampCenter = new Quaternion(0, 0, 0, 1);
    private double counter = 0;
    private int cooldown = 7;
    private double lastProgress = 0;
    private bool stampFired = false;
    private int stampEffect = 0; // 1 - grid of rings; 2 - rhythm stamp

    // Ripple state.
    private int rippleType = 0; // 0 - 'static' ripple; 1 - 'follower' ripple
    private Quaternion rippleCenter = new Quaternion(0, 0, 0, 1);
    private double rippleCounter = 0;
    private bool rippleFiring = false;
    private double rippleCooldown = RIPPLE_COOLDOWN_RESET;

    public LEDDomeQuaternionPaintbrushVisualizer(
      Configuration config,
      LayerRendererRuntime runtime,
      AudioInput audio,
      OrientationInput orientationInput,
      OrientationCenter center,
      BeatBroadcaster beat,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.runtime = runtime;
      this.audio = audio;
      this.orientationInput = orientationInput;
      this.beat = beat;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeOutputBuffer();
      this.rand = new Random();
      this.center = center;

      // Bake the static unit-sphere position of every pixel once, plus whether
      // each pixel is high enough on the dome to twinkle (z > .2).
      this.pixelPositions = this.buffer.BakePixelPositions();
      this.twinkleEligible = new bool[this.pixelPositions.Length];
      for (int i = 0; i < this.pixelPositions.Length; i++) {
        this.twinkleEligible[i] = this.pixelPositions[i].Z > .2;
      }
    }

    public int Priority => 2;

    public string LayerKey => "quaternion-paintbrush";
    public LEDDomeOutputBuffer LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.orientationInput });
    }

    public void Visualize() {
      double frameScale = this.frameClock.Tick();
      double progress = this.beat.ProgressThroughMeasure;
      double level = this.audio.Volume;

      // This layer's own tuning, read from its compiled runtime snapshot
      // (fade/hue speed stay global — shared scene state, not layer params).
      double size = this.runtime.Parameter("size");
      double twinkle = this.runtime.Parameter("twinkleDensity");
      double rippleCDStep = this.runtime.Parameter("rippleCDStep");
      double rippleStep = this.runtime.Parameter("rippleStep");

      ApplyGlobalEffects(frameScale);
      this.center.Update(level);
      UpdateStamp(progress, level);
      UpdateRipple(rippleCDStep, rippleStep, frameScale);
      RenderPixels(level, size, twinkle, frameScale);

      lastProgress = progress;
    }

    // Whole-buffer fade. (Hue rotation is now applied globally by
    // LEDDomeOutput, which rotates every contributing layer's persisted
    // buffer once per composited frame — not per layer here.)
    private void ApplyGlobalEffects(double frameScale) {
      // Fade is multiplicative per frame: each frame keeps the fraction
      // retention = 1 - 5^-fadeSpeed of the previous brightness. To hold the
      // same fade per unit of wall-clock time at any frame rate, that whole
      // retention factor is compounded over frameScale frames as a power
      // (retention^frameScale), which reduces to the nominal value at
      // frameScale == 1. (Scaling the inner 5^ exponent instead would compound
      // 5^-fadeSpeed rather than the retention factor, a different quantity.)
      double frameRetention = 1 - Math.Pow(5, -this.config.domeGlobalFadeSpeed);
      buffer.Fade(Math.Pow(frameRetention, frameScale), 0);
      counter += frameScale;
    }

    // Stamp state machine: a shape that appears when something loud enough
    // happens, alternating between the grid and rhythm effects.
    private void UpdateStamp(double progress, double level) {
      // A beat has wrapped while we are still rendering a stamp.
      if (cooldown > 0 && lastProgress > progress) {
        cooldown--;
        if (cooldown <= 0) {
          stampFired = false;
        }
      }
      // Enough time has passed and something loud enough happened: fire.
      if (counter > STAMP_INTERVAL && level > STAMP_LEVEL) {
        stampFired = true;
        counter = 0;
        cooldown = STAMP_COOLDOWN;
        stampEffect = stampEffect == 1 ? 2 : 1; // alternate grid / rhythm
        stampCenter = this.center.CurrentCenter;
      }
    }

    // Ripple state machine: a color wave expanding from a center, optionally
    // following the wand. Both step rates are this layer's params.
    private void UpdateRipple(double rippleCDStep, double rippleStep, double frameScale) {
      if (rippleCounter > RIPPLE_PERIOD) {
        rippleCounter = 0;
        rippleFiring = false;
      }

      if (!rippleFiring) {
        rippleCooldown -= rippleCDStep * frameScale;
      }

      if (rippleCooldown < 0) {
        rippleFiring = true;
        rippleType = (rippleType + 1) % 2;
        rippleCenter = this.center.CurrentCenter;
        rippleCooldown = RIPPLE_COOLDOWN_RESET;
      }

      if (rippleFiring) {
        rippleCounter += rippleStep * frameScale;
        if (rippleType == 1) {
          rippleCenter = this.center.CurrentCenter; // follower ripple tracks the wand
        }
      }
    }

    // The single per-pixel loop. Composites twinkle, the metaball field, its
    // the ripple wave, and stamps. Everything but twinkle and stamps is a
    // "keep the brighter color" blend, so we accumulate the winning
    // (hue,sat,val) and pack to an int once per pixel.
    private void RenderPixels(double level, double size, double twinkle, double frameScale) {
      double thresholdFactor = (size / 4) + level + .01;
      double threshold = 2 / thresholdFactor;
      // High volumes desaturate the metaball.
      double metaballSaturation = Math.Clamp(1.3 / level - 1, .2, 1);
      // Twinkle is a per-pixel chance each frame, so scale the probability by
      // frameScale to hold the twinkle rate (dots per second) steady.
      double twinkleDensity = twinkle * frameScale;

      // Per-frame ripple geometry (constant across pixels).
      double rippleRadius = rippleCounter / 300d;
      double rippleSaturation = Math.Clamp(1 - rippleCounter / 600d, 0, 1);
      double rippleValue = Math.Clamp(1 - rippleCounter / 800d, 0, 1);

      // Per-frame stamp geometry.
      double stampHue = (1 + stampCenter.W) / 2;
      double ringDistance = 2.4 - Math.Clamp(1.8d / (4 - (cooldown / 2d)), 0, 2.4);
      double ringHalfWidth = .003 * cooldown * cooldown;

      for (int i = 0; i < buffer.pixels.Length; i++) {
        Vector3 pixelPoint = pixelPositions[i];

        int best = buffer.pixels[i].color;
        double bestValue = ValueFromInt(best);
        bool drawn = false;

        // Twinkle - dense bright dots at random, high on the dome only.
        if (twinkleEligible[i] && rand.NextDouble() < twinkleDensity) {
          best = 0xFFFFFF;
          bestValue = 1;
          drawn = true;
        }

        // Metaball - the orientation-derived potential field at this point
        // (see OrientationCenter.PotentialAt: sums each wand's contribution,
        // opposite ends of the hemisphere identified, or the idle dummy
        // pointer's single-device field).
        double potential = this.center.PotentialAt(pixelPoint, out Quaternion colorCenter);
        double metaballHue = OrientationCenter.HueFromColorCenter(colorCenter);

        // Crisp metaball: a hard cutoff at the threshold, always full value.
        if (potential - threshold > 0) {
          drawn = true;
          if (1 > bestValue) {
            best = new Color(metaballHue, metaballSaturation, 1).ToInt();
            bestValue = 1;
          }
        }

        // Ripple - a color wave that follows (or sits at) a center.
        if (CloseTo(Vector3.Distance(Vector3.Transform(pixelPoint, rippleCenter), spot), rippleRadius, .01)) {
          drawn = true;
          if (rippleValue > bestValue) {
            best = new Color(metaballHue, rippleSaturation, rippleValue).ToInt();
            bestValue = rippleValue;
          }
        }

        // Stamps - shapes that appear based on the wand facing. These overwrite
        // rather than blend.
        if (stampFired) {
          double stampDistance = Vector3.Distance(Vector3.Transform(pixelPoint, stampCenter), spot);
          if (stampEffect == 1) {
            // Evenly spaced grid of rings.
            if (stampDistance % .4 < .05) {
              best = new Color(stampHue, .2, 1).ToInt();
              bestValue = 1;
              drawn = true;
            }
          } else if (stampEffect == 2) {
            // Time-delayed band that contracts to the beat.
            if (Between(stampDistance, ringDistance - ringHalfWidth, ringDistance + ringHalfWidth)) {
              best = new Color(stampHue, .2, 1).ToInt();
              bestValue = 1;
              drawn = true;
            }
          }
        }

        // Only write when an effect touched this pixel, so untouched pixels keep
        // the sub-integer precision their faded RGB accumulators carry.
        if (drawn) {
          buffer.pixels[i].color = best;
        }
      }

      // Grid stamp is a single quick flash; clear it once the cooldown winds
      // down (the rhythm stamp persists for the full cooldown instead).
      if (cooldown < GRID_STAMP_CUTOFF && stampEffect == 1) {
        stampFired = false;
      }
    }
  }
}
