using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;

namespace Spectrum.Visualizers {

  // Background made momentary: paints its whole buffer with a single flat color
  // (its "color" param, exactly like LEDDomeBackgroundVisualizer) but only on
  // the frame LayerTrigger fires, then fades the fill back out via
  // domeGlobalFadeSpeed — the same fill-then-Fade playhead Stamp uses. A
  // strobe-on-demand: sit it near the top of the stack under Over/Add for a
  // full-dome flash punctuating whatever plays below.
  //
  // Trigger sources are the full set (docs/triggers.md): Manual (native Fire
  // button) and a bound wand "button" are always live, and the "trigger" param
  // selects one autonomous source — Beat (default) or Audio. Center-free like
  // Background; it declares OrientationInput only so the Button source can read
  // wand state (AlwaysActive, so it never gates eligibility), and reads
  // audio.Volume opportunistically for the Audio source without declaring it an
  // input (same as Stamp).
  class LEDDomeFlashVisualizer : DomeLayerVisualizer {

    private readonly Configuration config;
    private readonly AudioInput audio;
    private readonly OrientationInput orientationInput;
    private readonly BeatBroadcaster beat;
    private readonly LEDDomeOutput dome;
    private readonly LEDDomeOutputBuffer buffer;
    private readonly LayerTrigger trigger;

    private readonly FrameClock frameClock = new FrameClock();

    public LEDDomeFlashVisualizer(
      Configuration config,
      AudioInput audio,
      OrientationInput orientationInput,
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
      this.trigger = new LayerTrigger(
        config, orientationInput, this.LayerKey, beat, audio);
    }

    public int Priority {
      get {
        return DomeLayerSettings.StackActivates(
          this.config.domeLayerStack, "flash"
        ) ? 2 : 0;
      }
    }

    public string LayerKey => "flash";
    public LEDDomeOutputBuffer LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.orientationInput });
    }

    public void Visualize() {
      double frameScale = this.frameClock.Tick();

      IList<DomeLayerSettings> stack = this.config.domeLayerStack;
      int color = (int)DomeLayerSettings.ParamValue(stack, this.LayerKey, "color");
      int triggerSource =
        (int)DomeLayerSettings.ParamValue(stack, this.LayerKey, "trigger");
      int button =
        (int)DomeLayerSettings.ParamValue(stack, this.LayerKey, "button");
      double interval =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "interval");
      double levelThreshold =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "level");

      // Decay the previous flash toward transparent so it reveals the layers
      // below as it dims (same global fade the other trigger layers use).
      double frameRetention = 1 - Math.Pow(5, -this.config.domeGlobalFadeSpeed);
      this.buffer.Fade(Math.Pow(frameRetention, frameScale), 0);

      // Fired() must run every frame regardless of playhead state, so an edge
      // occurring mid-fade is never missed (docs/triggers.md).
      bool fired =
        this.trigger.Fired(button, triggerSource, levelThreshold, interval);
      if (fired) {
        for (int i = 0; i < this.buffer.pixels.Length; i++) {
          this.buffer.pixels[i].color = color;
        }
      }
    }
  }
}
