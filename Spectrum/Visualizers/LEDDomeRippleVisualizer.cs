using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Numerics;
using static Spectrum.MathUtil;

namespace Spectrum.Visualizers {

  // A color wave expanding from the orientation center, lifted out of
  // Quaternion Paintbrush as its own stack layer (docs/layers_inventory.md).
  // Alternates each time it fires between a 'static' ripple (the center is
  // sampled once, then frozen — the pinned end of the center-binding axis)
  // and a 'follower' ripple (the center is re-sampled every frame, so it
  // tracks the wand — the following end); see OrientationCenter for the
  // shared position/color-field machinery both ends read.
  //
  // Uses the orientation-derived palette, not config.colorPalette: each ring
  // pixel's hue is the metaball field's hue sampled live at that pixel, every
  // frame — even a pinned ring's *position* is frozen, its *color* is not
  // (the two are independent freezes; see the design doc). This matches what
  // the ring showed fused inside Paintbrush, where the same per-pixel
  // metaballHue drove both the metaball and the ripple.
  class LEDDomeRippleVisualizer : DomeLayerVisualizer {

    private const int RIPPLE_PERIOD = 1000;

    private readonly Configuration config;
    private readonly AudioInput audio;
    private readonly OrientationInput orientationInput;
    private readonly LEDDomeOutput dome;
    private readonly LEDDomeOutputBuffer buffer;
    private readonly OrientationCenter center;
    private readonly LayerTrigger trigger;

    // Static per-pixel geometry, baked once (mirrors Paintbrush's mapping).
    private readonly Vector3[] pixelPositions;

    private readonly FrameClock frameClock = new FrameClock();

    // Ripple playhead state. Firing is now driven by LayerTrigger
    // (docs/triggers.md) rather than the old internal cooldown timer.
    private int rippleType = 0; // 0 - 'static' ripple; 1 - 'follower' ripple
    private Quaternion rippleCenter = new Quaternion(0, 0, 0, 1);
    private double rippleCounter = 0;
    private bool rippleFiring = false;

    public LEDDomeRippleVisualizer(
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
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeOutputBuffer();
      this.center = center;
      this.trigger = new LayerTrigger(
        config, orientationInput, this.LayerKey, beat, audio);

      // Bake the static unit-sphere position of every pixel once.
      this.pixelPositions = this.buffer.BakePixelPositions();
    }

    public int Priority {
      get {
        return DomeLayerSettings.StackActivates(
          this.config.domeLayerStack, "ripple"
        ) ? 2 : 0;
      }
    }

    public string LayerKey => "ripple";
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
      double rippleStep =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "rippleStep");
      int triggerSource =
        (int)DomeLayerSettings.ParamValue(stack, this.LayerKey, "trigger");
      int button =
        (int)DomeLayerSettings.ParamValue(stack, this.LayerKey, "button");
      double levelThreshold =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "level");
      double interval =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "interval");

      double frameRetention = 1 - Math.Pow(5, -this.config.domeGlobalFadeSpeed);
      this.buffer.Fade(Math.Pow(frameRetention, frameScale), 0);

      // Fired() must run every frame regardless of playhead state, so an edge
      // occurring mid-ripple is never missed (docs/triggers.md).
      bool fired = this.trigger.Fired(button, triggerSource, levelThreshold, interval);

      this.center.Update(level);
      UpdateRipple(fired, rippleStep, frameScale);
      RenderPixels();
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

    private void RenderPixels() {
      if (!this.rippleFiring) {
        return;
      }

      double rippleRadius = this.rippleCounter / 300d;
      double rippleSaturation = Math.Clamp(1 - this.rippleCounter / 600d, 0, 1);
      double rippleValue = Math.Clamp(1 - this.rippleCounter / 800d, 0, 1);

      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        Vector3 pixelPoint = this.pixelPositions[i];
        double distance = Vector3.Distance(
          Vector3.Transform(pixelPoint, this.rippleCenter),
          OrientationCenter.Spot
        );
        if (CloseTo(distance, rippleRadius, .01)) {
          // The ring's *position* may be frozen (pinned) or tracking
          // (following) via rippleCenter above; its *color* always samples
          // the live orientation-derived field at this pixel — the same two
          // independent freezes the fused version exercised (see the class
          // doc and docs/layers_inventory.md "Center binding").
          double hue = OrientationCenter.HueFromColorCenter(
            this.center.ColorCenterAt(pixelPoint));
          this.buffer.pixels[i].color =
            new Color(hue, rippleSaturation, rippleValue).ToInt();
        }
      }
    }
  }
}
