using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
    private const int RHYTHM_COOLDOWN = 8;     // measures the rhythm band plays over (its cooldown starts here)

    private readonly DomeLayerEnvironment environment;
    private readonly LayerRendererRuntime runtime;
    private readonly AudioInput audio;
    private readonly OrientationInput orientationInput;
    private readonly BeatBroadcaster beat;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;
    private readonly OrientationCenter center;
    private readonly LayerTrigger trigger;

    // Static per-pixel geometry, baked once (mirrors Paintbrush's mapping).
    private readonly ImmutableArray<Vector3> pixelPositions;

    private readonly FrameClock frameClock = new FrameClock();

    // Stamp playhead state. Firing is now driven by LayerTrigger
    // (docs/triggers.md); the per-measure cooldown below is the playhead.
    private Quaternion stampCenter = new Quaternion(0, 0, 0, 1);
    private int cooldown = 7;
    private double lastProgress = 0;
    private bool stampFired = false;
    private int stampEffect = 0; // 1 - grid of rings; 2 - rhythm stamp

    public LEDDomeStampVisualizer(
      DomeLayerEnvironment environment,
      LayerRendererRuntime runtime,
      AudioInput audio,
      OrientationInput orientationInput,
      OrientationCenter center,
      BeatBroadcaster beat,
      DomeRenderContext dome
    ) {
      this.environment = environment;
      this.runtime = runtime;
      this.audio = audio;
      this.orientationInput = orientationInput;
      this.beat = beat;
      this.dome = dome;
      this.buffer = this.dome.MakeDomeFrame();
      this.center = center;
      this.trigger = new LayerTrigger(
        environment, orientationInput, runtime.InstanceId, beat, audio);

      // Bake the static unit-sphere position of every pixel once.
      this.pixelPositions = this.buffer.BakePixelPositions();
    }

    public int Priority => 2;

    public string LayerKey => "stamp";
    public DomeFrame LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.orientationInput });
    }

    public void Visualize() {
      double frameScale = this.frameClock.Tick();
      double progress = this.beat.ProgressThroughMeasure;
      double level = this.audio.Volume;

      StampLayerOptions options =
        this.runtime.GetOptions<StampLayerOptions>();
      int triggerSource = options.Trigger;
      int button = options.Button;
      double interval = options.Interval;
      double levelThreshold = options.Level;

      double frameRetention =
        1 - Math.Pow(5, -this.environment.GlobalFadeSpeed);
      this.buffer.Fade(Math.Pow(frameRetention, frameScale), 0);

      // Fired() must run every frame regardless of playhead state, so an edge
      // occurring mid-stamp is never missed (docs/triggers.md).
      bool fired = this.trigger.Fired(button, triggerSource, levelThreshold, interval);

      this.center.Update(level);
      UpdateStamp(fired, progress);
      RenderPixels();

      lastProgress = progress;
    }

    // Stamp playhead: a fire (from LayerTrigger) stamps the current shape,
    // alternating between the grid and rhythm effects; the cooldown it fires
    // with counts down once per beat measure, not per frame.
    private void UpdateStamp(bool fired, double progress) {
      // A beat has wrapped while we are still rendering a stamp.
      if (this.cooldown > 0 && this.lastProgress > progress) {
        this.cooldown--;
        if (this.cooldown <= 0) {
          this.stampFired = false;
        }
      }
      // The rhythm effect is a slow band that expands outward over its cooldown
      // (many measures), so while one is still animating it owns the layer:
      // ignore new fires until it completes, or a rapid re-fire (Beat, held
      // audio) would reset it every measure and it would never play out. The
      // grid effect is a momentary flash, so it may be replaced freely.
      bool rhythmPlaying = this.stampFired && this.stampEffect == 2;
      if (fired && !rhythmPlaying) {
        this.stampFired = true;
        this.stampEffect = this.stampEffect == 1 ? 2 : 1; // alternate grid / rhythm
        // The rhythm band's playhead starts at RHYTHM_COOLDOWN so it opens at
        // the facing on its very first frame; the grid flash uses the full
        // STAMP_COOLDOWN.
        this.cooldown = this.stampEffect == 2 ? RHYTHM_COOLDOWN : STAMP_COOLDOWN;
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
      // The band flies outward from the facing (distance 0) toward the rim as
      // the cooldown winds down, easing out — quick at first, settling as it
      // nears the edge. Quadratic in the cooldown normalized to RHYTHM_COOLDOWN,
      // so it opens at the facing on its first frame (cooldown == RHYTHM_COOLDOWN)
      // and never divides by zero; the [1, RHYTHM_COOLDOWN] cooldown keeps it
      // inside the [0, 1] normalized angular-distance range without a clamp.
      double ringDistance =
        1 - Math.Pow(this.cooldown / (double)RHYTHM_COOLDOWN, 2);
      AngularRingBand rhythmBand =
        OrientationRingGeometry.StampBand(ringDistance, this.cooldown);

      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        Vector3 pixelPoint = this.pixelPositions[i];
        Vector3 centeredPoint =
          Vector3.Transform(pixelPoint, this.stampCenter);
        double stampDot = DomeSurfaceGeometry.UnitSphereDot(
          centeredPoint, OrientationCenter.Spot);
        if (this.stampEffect == 1) {
          // Evenly spaced grid of rings in surface angle.
          if (OrientationRingGeometry.StampGridContains(stampDot)) {
            this.buffer.pixels[i].color = HsvToInt(stampHue, .2, 1);
          }
        } else if (this.stampEffect == 2) {
          // Band that expands from the facing to the rim, sharpening as it goes.
          if (rhythmBand.ContainsDot(stampDot)) {
            this.buffer.pixels[i].color = HsvToInt(stampHue, .2, 1);
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
