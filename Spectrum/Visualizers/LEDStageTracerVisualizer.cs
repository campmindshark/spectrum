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

    private int tracerLEDIndex(int sideIndex) {
      double beatFactor = this.config.stageTracerSpeed / 3;
      double progress =
        this.config.beatBroadcaster.ProgressThroughBeat(beatFactor) * 3;
      int tracerLEDIndex;
      if (progress < 1.0) {
        tracerLEDIndex = (int)(
          progress * this.config.stageSideLengths[sideIndex * 3]
        );
      } else if (progress < 2.0) {
        tracerLEDIndex = (int)(
          this.config.stageSideLengths[sideIndex * 3] +
          (progress - 1.0) * this.config.stageSideLengths[sideIndex * 3 + 1]
        );
      } else {
        tracerLEDIndex = (int)(
          this.config.stageSideLengths[sideIndex * 3] +
          this.config.stageSideLengths[sideIndex * 3 + 1] +
          (progress - 2.0) * this.config.stageSideLengths[sideIndex * 3 + 2]
        );
      }
      return tracerLEDIndex;
    }

    public void Visualize() {
      if (this.stopwatch.ElapsedMilliseconds <= 10) {
        return;
      }
      this.stopwatch.Restart();

      int triangles = this.config.stageSideLengths.Length / 3;
      for (int i = 0; i < triangles; i++) {
        int tracerIndex = this.tracerLEDIndex(i);
        int triangleCounter = 0;
        for (int j = 0; j < 3; j++) {
          for (
            int k = 0;
            k < this.config.stageSideLengths[i * 3 + j];
            k++, triangleCounter++
          ) {
            int color = triangleCounter == tracerIndex
              ? this.stage.GetSingleComputerColor(0)
              : this.stage.GetSingleComputerColor(1);
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