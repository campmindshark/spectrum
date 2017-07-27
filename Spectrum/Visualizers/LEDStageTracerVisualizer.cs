using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Diagnostics;

namespace Spectrum {

  class LEDStageTracerVisualizer : Visualizer {

    private Configuration config;
    private LEDStageOutput stage;
    private Stopwatch stopwatch;

    public LEDStageTracerVisualizer(
      Configuration config,
      LEDStageOutput stage
    ) {
      this.config = config;
      this.stage = stage;
      this.stage.RegisterVisualizer(this);
      this.stopwatch = new Stopwatch();
      this.stopwatch.Start();
    }

    public int Priority {
      get {
        return 2;
      }
    }

    private bool enabled = false;
    public bool Enabled {
      get {
        return this.enabled;
      }
      set {
        if (value == this.enabled) {
          return;
        }
        this.enabled = value;
      }
    }

    public Input[] GetInputs() {
      return new Input[] {};
    }

    public void Visualize() {
      if (this.stopwatch.ElapsedMilliseconds <= 10) {
        return;
      }
      this.stopwatch.Restart();

      byte brightnessByte = (byte)(
        0xFF * this.config.stageBrightness
      );

      int triangles = this.config.stageSideLengths.Length / 3;
      for (int i = 0; i < triangles; i++) {
        int triangleLength = this.config.stageSideLengths[i * 3] +
          this.config.stageSideLengths[i * 3 + 1] +
          this.config.stageSideLengths[i * 3 + 2];
        int blackLEDIndex =
          (int)(this.config.beatBroadcaster.ProgressThroughBeat(0.33333333333333333333333333333) * triangleLength);
        int triangleCounter = 0;
        for (int j = 0; j < 3; j++) {
          for (
            int k = 0;
            k < this.config.stageSideLengths[i * 3 + j];
            k++, triangleCounter++
          ) {
            int color = triangleCounter == blackLEDIndex ? 0x000000 : 0xFF0000;
            for (int l = 0; l < 3; l++) {
              this.stage.SetPixel(i * 3 + j, k, l, color);
            }
          }
        }
      }
      this.stage.Flush();
    }

  }

}