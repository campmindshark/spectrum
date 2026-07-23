using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;
using static Spectrum.MathUtil;

namespace Spectrum.Visualizers {

  // A color wave expanding from the orientation center, lifted out of
  // Quaternion Paintbrush as its own stack layer.
  // Alternates each time it fires between a 'static' ripple (the center is
  // sampled once, then frozen — the pinned end of the center-binding axis)
  // and a 'follower' ripple (the center is re-sampled every frame, so it
  // tracks the wand — the following end); see OrientationCenter for the
  // shared position/color-field machinery both ends read.
  //
  // Uses the orientation-derived palette, not the configured dome palettes: each ring
  // pixel's hue is the metaball field's hue sampled live at that pixel, every
  // frame — even a pinned ring's *position* is frozen, its *color* is not
  // (the two are independent freezes; see the design doc). This matches what
  // the ring showed fused inside Paintbrush, where the same per-pixel
  // metaballHue drove both the metaball and the ripple.
  class LEDDomeRippleVisualizer : DomeLayerVisualizer {

    private const int RIPPLE_PERIOD = 1000;

    private readonly DomeLayerEnvironment environment;
    private readonly LayerRendererRuntime runtime;
    private readonly IAudioLevelInput audio;
    private readonly OrientationInput orientationInput;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;
    private readonly OrientationCenter center;
    private readonly LayerTrigger trigger;

    // Static per-pixel geometry, baked once (mirrors Paintbrush's mapping).
    private readonly ImmutableArray<Vector3> pixelPositions;

    private readonly FrameClock frameClock = new FrameClock();

    // Ripple playhead state. Firing is now driven by LayerTrigger rather than
    // the old internal cooldown timer.
    private int rippleType = 0; // 0 - 'static' ripple; 1 - 'follower' ripple
    private Quaternion rippleCenter = new Quaternion(0, 0, 0, 1);
    private double rippleCounter = 0;
    private bool rippleFiring = false;

    public LEDDomeRippleVisualizer(
      DomeLayerEnvironment environment,
      LayerRendererRuntime runtime,
      IAudioLevelInput audio,
      OrientationInput orientationInput,
      OrientationCenter center,
      BeatBroadcaster beat,
      DomeRenderContext dome
    ) {
      this.environment = environment;
      this.runtime = runtime;
      this.audio = audio;
      this.orientationInput = orientationInput;
      this.dome = dome;
      this.buffer = this.dome.MakeDomeFrame();
      this.center = center;
      this.trigger = new LayerTrigger(
        environment, orientationInput, runtime.InstanceId, beat, audio);

      // Bake the static unit-sphere position of every pixel once.
      this.pixelPositions = this.buffer.BakePixelPositions();
    }

    public int Priority => 2;

    public string LayerKey => "ripple";
    public DomeFrame LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.orientationInput });
    }

    public void Visualize() {
      double frameScale = this.frameClock.Tick();
      double level = this.audio.Volume;

      RippleLayerOptions options =
        this.runtime.GetOptions<RippleLayerOptions>();
      double rippleStep = options.RippleSpeed;
      double desaturation = options.Desaturation;
      int triggerSource = options.Trigger;
      int button = options.Button;
      double levelThreshold = options.Level;
      double interval = options.Interval;

      double frameRetention =
        1 - Math.Pow(5, -this.environment.GlobalFadeSpeed);
      this.buffer.Fade(Math.Pow(frameRetention, frameScale), 0);

      // Fired() must run every frame regardless of playhead state, so an edge
      // occurring mid-ripple is never missed.
      bool fired = this.trigger.Fired(button, triggerSource, levelThreshold, interval);

      this.center.Update(level);
      UpdateRipple(fired, rippleStep, frameScale);
      RenderPixels(desaturation);
    }

    // Ripple playhead: a fire (from LayerTrigger) starts one expansion,
    // alternating pinned/following center binding; it runs until RIPPLE_PERIOD
    // then stops. Fires arriving mid-ripple are ignored (the !rippleFiring
    // gate) so a full sweep always completes — a Beat trigger therefore relaunches
    // on the first beat after the current ripple ends, preserving the big-sweep
    // look rather than restarting to center every beat.
    private void UpdateRipple(bool fired, double rippleStep, double frameScale) {
      if (this.rippleCounter > RIPPLE_PERIOD) {
        this.rippleCounter = 0;
        this.rippleFiring = false;
      }

      if (!this.rippleFiring && fired) {
        this.rippleFiring = true;
        this.rippleCounter = 0;
        this.rippleType = (this.rippleType + 1) % 2;
        this.rippleCenter = this.center.CurrentCenter;
      }

      if (this.rippleFiring) {
        this.rippleCounter += rippleStep * frameScale;
        if (this.rippleType == 1) {
          this.rippleCenter = this.center.CurrentCenter; // follower tracks the wand
        }
      }
    }

    private void RenderPixels(double desaturation) {
      if (!this.rippleFiring) {
        return;
      }

      AngularRingBand rippleBand =
        OrientationRingGeometry.RippleBand(this.rippleCounter);
      double rippleSaturation =
        SaturationFor(this.rippleCounter, desaturation);
      double rippleValue = Math.Clamp(1 - this.rippleCounter / 800d, 0, 1);

      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        Vector3 pixelPoint = this.pixelPositions[i];
        Vector3 centeredPoint =
          Vector3.Transform(pixelPoint, this.rippleCenter);
        if (rippleBand.Contains(centeredPoint, OrientationCenter.Spot)) {
          // The ring's *position* may be frozen (pinned) or tracking
          // (following) via rippleCenter above; its *color* always samples
          // the live orientation-derived field at this pixel — the same two
          // independent freezes the fused version exercised (see the class
          // documentation).
          double hue = OrientationCenter.HueFromColorCenter(
            this.center.ColorCenterAt(pixelPoint));
          this.buffer.pixels[i].color =
            HsvToInt(hue, rippleSaturation, rippleValue);
        }
      }
    }

    internal static double SaturationFor(
      double rippleCounter, double desaturation
    ) => Math.Clamp(1 - rippleCounter / 600d, 0, 1) *
      (1 - Math.Clamp(desaturation, 0, 1));
  }
}
