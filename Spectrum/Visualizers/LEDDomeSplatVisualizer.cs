using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Spectrum {

  class LEDDomeSplatVisualizer : Visualizer {

    private Configuration config;
    private AudioInput audio;
    private LEDDomeOutput dome;
    private LEDDomeOutputBuffer buffer;

    private double currentAngle;
    private double currentGradient;
    private double currentCenterAngle;
    private double lastProgress;

    public LEDDomeSplatVisualizer(
      Configuration config,
      AudioInput audio,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.audio = audio;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeOutputBuffer();
    }

    public int Priority {
      get {
        return this.config.domeActiveVis == 5 ? 2 : 0;
      }
    }

    public bool Enabled { get; set; }

    public Input[] GetInputs() {
      return new Input[] { this.audio };
    }

    void Render() {

      double level = this.audio.LevelForChannel(0);
      // Sqrt makes values larger and gives more resolution for lower values
      //double adjustedLevel = Clamp(Math.Sqrt(level), 0.1, 1);

      double progress = this.config.beatBroadcaster.ProgressThroughMeasure;

      buffer.Fade(0.9, 0);

      if (progress < this.lastProgress) {
        for (int i = 0; i < buffer.pixels.Length; i++) {
          var pixel = buffer.pixels[i];

          buffer.pixels[i].color = 0xffffff;
        }
      }

      this.dome.WriteBuffer(buffer);
      this.lastProgress = progress;
    }

    public void Visualize() {
      this.Render();

      this.dome.Flush();
    }

  }

}
