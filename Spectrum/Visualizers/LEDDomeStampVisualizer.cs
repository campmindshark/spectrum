using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Numerics;
using static Spectrum.MathUtil;

namespace Spectrum.Visualizers {

  // A shape that flashes at the wand's facing when something loud enough
  // happens, lifted out of Quaternion Paintbrush as its own stack layer
  // (docs/layers_inventory.md — the third orientation-driven effect pulled
  // out, after Ripple). Alternates each time it fires between a grid of
  // rings and a rhythm band that contracts to the beat.
  //
  // Center binding (docs/layers_inventory.md "Center binding") is pinned: the
  // orientation center is sampled once at fire time (stampCenter) and frozen
  // for the life of the stamp, unlike Ripple's per-fire pinned/following
  // alternation. Color is likewise frozen at fire time (stampHue, from the
  // same frozen center) rather than resampling the live field per pixel —
  // the opposite choice from Ripple, which freezes position but keeps
  // sampling live color. Both are valid points on the same two-freeze axis
  // OrientationCenter documents.
  class LEDDomeStampVisualizer : DomeLayerVisualizer {

    private const int STAMP_COOLDOWN = 10;     // render frames a stamp lasts
    private const int GRID_STAMP_CUTOFF = 7;   // grid stamp clears once below this

    private readonly Configuration config;
    private readonly AudioInput audio;
    private readonly OrientationInput orientationInput;
    private readonly BeatBroadcaster beat;
    private readonly LEDDomeOutput dome;
    private readonly LEDDomeOutputBuffer buffer;
    private readonly OrientationCenter center;

    // Static per-pixel geometry, baked once (mirrors Paintbrush's mapping).
    private readonly Vector3[] pixelPositions;

    private readonly FrameClock frameClock = new FrameClock();

    // Stamp state, unchanged from the fused version.
    private Quaternion stampCenter = new Quaternion(0, 0, 0, 1);
    private double counter = 0;
    private int cooldown = 7;
    private double lastProgress = 0;
    private bool stampFired = false;
    private int stampEffect = 0; // 1 - grid of rings; 2 - rhythm stamp

    public LEDDomeStampVisualizer(
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
      this.beat = beat;
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
          this.config.domeLayerStack, "stamp"
        ) ? 2 : 0;
      }
    }

    public string LayerKey => "stamp";
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

      IList<DomeLayerSettings> stack = this.config.domeLayerStack;
      double interval =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "interval");
      double levelThreshold =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "level");

      double frameRetention = 1 - Math.Pow(5, -this.config.domeGlobalFadeSpeed);
      this.buffer.Fade(Math.Pow(frameRetention, frameScale), 0);

      this.center.Update(level);
      counter += frameScale;
      UpdateStamp(progress, level, interval, levelThreshold);
      RenderPixels();

      lastProgress = progress;
    }

    // Stamp state machine: fires when loud enough after an interval has
    // passed, alternating between the grid and rhythm effects; the cooldown
    // it fires with counts down once per beat measure, not per frame.
    private void UpdateStamp(
      double progress, double level, double interval, double levelThreshold
    ) {
      // A beat has wrapped while we are still rendering a stamp.
      if (this.cooldown > 0 && this.lastProgress > progress) {
        this.cooldown--;
        if (this.cooldown <= 0) {
          this.stampFired = false;
        }
      }
      // Enough time has passed and something loud enough happened: fire.
      if (this.counter > interval && level > levelThreshold) {
        this.stampFired = true;
        this.counter = 0;
        this.cooldown = STAMP_COOLDOWN;
        this.stampEffect = this.stampEffect == 1 ? 2 : 1; // alternate grid / rhythm
        this.stampCenter = this.center.CurrentCenter;
      }
    }

    private void RenderPixels() {
      if (!this.stampFired) {
        return;
      }

      // Both the geometry center and the color are frozen at fire time
      // (stampCenter), unlike Ripple's live color sampling.
      double stampHue = (1 + this.stampCenter.W) / 2;
      double ringDistance = 2.4 - Math.Clamp(1.8d / (4 - (this.cooldown / 2d)), 0, 2.4);
      double ringHalfWidth = .003 * this.cooldown * this.cooldown;

      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        Vector3 pixelPoint = this.pixelPositions[i];
        double stampDistance = Vector3.Distance(
          Vector3.Transform(pixelPoint, this.stampCenter), OrientationCenter.Spot
        );
        if (this.stampEffect == 1) {
          // Evenly spaced grid of rings.
          if (stampDistance % .4 < .05) {
            this.buffer.pixels[i].color = new Color(stampHue, .2, 1).ToInt();
          }
        } else if (this.stampEffect == 2) {
          // Time-delayed band that contracts to the beat.
          if (Between(stampDistance, ringDistance - ringHalfWidth, ringDistance + ringHalfWidth)) {
            this.buffer.pixels[i].color = new Color(stampHue, .2, 1).ToInt();
          }
        }
      }

      // Grid stamp is a single quick flash; clear it once the cooldown winds
      // down (the rhythm stamp persists for the full cooldown instead).
      if (this.cooldown < GRID_STAMP_CUTOFF && this.stampEffect == 1) {
        this.stampFired = false;
      }
    }
  }
}
