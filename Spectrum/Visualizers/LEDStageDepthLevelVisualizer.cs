using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Audio;
using System;
using System.ComponentModel;
using System.Diagnostics;

namespace Spectrum {

  class LEDStageDepthLevelVisualizer : Visualizer {

    private readonly Configuration config;
    private readonly AudioInput audio;
    private readonly LEDStageOutput stage;
    private bool[] sideParts;
    private readonly Stopwatch stopwatch;

    public LEDStageDepthLevelVisualizer(
      Configuration config,
      AudioInput audio,
      LEDStageOutput stage
    ) {
      this.config = config;
      this.audio = audio;
      this.stage = stage;
      this.stage.RegisterVisualizer(this);
      this.stopwatch = new Stopwatch();
      this.stopwatch.Start();
      this.config.PropertyChanged += ConfigUpdated;
    }

    private void ConfigUpdated(object sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == "stageSideLengths" && this.Enabled) {
        this.CalculateSideParts();
      }
    }

    private void CalculateSideParts() {
      var sideLengths = this.config.stageSideLengths;
      bool[] newSideParts = new bool[sideLengths.Length];
      for (int i = 0; i < sideLengths.Length; i++) {
        if ((i / 12) == 1) {
          newSideParts[i] = ((i / 3) % 4) == 2;
        } else {
          newSideParts[i] = ((i / 3) % 4) == 1;
        }
      }
      this.sideParts = newSideParts;
    }

    public int Priority {
      get {
        return 3;
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
        if (value) {
          this.CalculateSideParts();
        }
        this.enabled = value;
      }
    }

    public Input[] GetInputs() {
      return new Input[] {};
    }

    public void Visualize() {
      if (this.stopwatch.ElapsedMilliseconds <= 1000) {
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
        int maxTriangleCounter = this.config.stageSideLengths[i * 3] +
          this.config.stageSideLengths[i * 3 + 1] +
          this.config.stageSideLengths[i * 3 + 2];
        for (int j = 0; j < 3; j++) {
          for (
            int k = 0;
            k < this.config.stageSideLengths[i * 3 + j];
            k++, triangleCounter++
          ) {
            bool secondPart = this.sideParts[i * 3 + j] ^
              (this.config.beatBroadcaster.ProgressThroughBeat(0.25) > 0.5);
            int color = this.stage.GetGradientColor(
              secondPart ? 1 : 0,
              (double)triangleCounter / maxTriangleCounter,
              (double)tracerIndex / maxTriangleCounter,
              true
            );
            int dimmedColor = LEDColor.ScaleColor(
              color,
              this.audio.LevelForChannel(secondPart ? 2 : 1)
            );
            for (int l = 0; l < 3; l++) {
              this.stage.SetPixel(i * 3 + j, k, l, dimmedColor);
            }
          }
        }
      }
      this.stage.Flush();
    }

  }

}