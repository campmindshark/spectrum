using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Diagnostics;

namespace Spectrum {

  class LEDStageTracerVisualizer : Visualizer {

    private readonly Configuration config;
    private readonly LEDStageOutput stage;
    private readonly Stopwatch stopwatch;

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
      return new Input[] { };
    }

    public void Visualize() {
      if (this.stopwatch.ElapsedMilliseconds <= 10) {
        return;
      }
      this.stopwatch.Restart();

      int triangles = this.config.stageSideLengths.Length / 3;
      for (int i = 0; i < triangles; i++) {
        int tracerIndex = LEDStageTracerVisualizer.TracerLEDIndex(
          this.config,
          i
        );
        int triangleCounter = 0;
        for (int j = 0; j < 3; j++) {
          for (
            int k = 0;
            k < this.config.stageSideLengths[i * 3 + j];
            k++, triangleCounter++
          ) {
            int color = triangleCounter == tracerIndex
              ? this.stage.GetSingleColor(0)
              : this.stage.GetSingleColor(1);
            for (int l = 0; l < 3; l++) {
              this.stage.SetPixel(i * 3 + j, k, l, color);
            }
          }
        }
      }
      this.stage.Flush();
    }

    public static int TracerLEDIndex(
      Configuration config,
      int triangleIndex
    ) {
      double beatFactor = config.stageTracerSpeed / 3;
      double progress =
        config.beatBroadcaster.ProgressThroughBeat(beatFactor) * 3;
      int tracerLEDIndex;
      if (progress < 1.0) {
        tracerLEDIndex = (int)(
          progress * config.stageSideLengths[triangleIndex * 3]
        );
      } else if (progress < 2.0) {
        tracerLEDIndex = (int)(
          config.stageSideLengths[triangleIndex * 3] +
          (progress - 1.0) * config.stageSideLengths[triangleIndex * 3 + 1]
        );
      } else {
        tracerLEDIndex = (int)(
          config.stageSideLengths[triangleIndex * 3] +
          config.stageSideLengths[triangleIndex * 3 + 1] +
          (progress - 2.0) * config.stageSideLengths[triangleIndex * 3 + 2]
        );
      }
      return tracerLEDIndex;
    }

  }

}