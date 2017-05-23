using Spectrum.Base;
using Spectrum.LEDs;
using System.Diagnostics;

namespace Spectrum {

  class LEDStageFlashColorsDiagnosticVisualizer : Visualizer {

    private Configuration config;
    private LEDStageOutput stage;
    private Stopwatch stopwatch;
    // 0: everything off, 1: everything on, 2: only borders on, 3: everything on
    private int state = 3;

    public LEDStageFlashColorsDiagnosticVisualizer(
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
        return this.config.stageTestPattern == 1 ? 1000 : 0;
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
      if (this.stopwatch.ElapsedMilliseconds <= 1000) {
        return;
      }
      this.stopwatch.Restart();
      this.state = (this.state + 1) % 4;

      if (this.state == 0) {
        for (int i = 0; i < this.config.stageSideLengths.Length; i++) {
          for (int j = 0; j < this.config.stageSideLengths[i]; j++) {
            for (int k = 0; k < 3; k++) {
              this.stage.SetPixel(i, j, k, 0x000000);
            }
          }
        }
        this.stage.Flush();
        return;
      }

      byte brightnessByte = (byte)(
        0xFF * this.config.stageBrightness
      );
      int whiteColor = brightnessByte << 16
        | brightnessByte << 8
        | brightnessByte;
      int[] colors = {
        whiteColor & 0xFF0000,
        whiteColor & 0x00FF00,
        whiteColor & 0x0000FF,
        whiteColor & 0xFFFF00,
        whiteColor & 0xFF00FF,
      };

      int colorIndex = 0;
      for (int i = 0; i < this.config.stageSideLengths.Length; i++) {
        for (int k = 0; k < 3; k++) {
          var sideLength = this.config.stageSideLengths[i];
          if (this.state == 2) {
            for (int j = 1; j < sideLength - 1; j++) {
              this.stage.SetPixel(i, j, k, 0x000000);
            }
            this.stage.SetPixel(i, 0, k, colors[colorIndex]);
            this.stage.SetPixel(i, sideLength - 1, k, colors[colorIndex]);
          } else {
            for (int j = 0; j < sideLength; j++) {
              this.stage.SetPixel(i, j, k, colors[colorIndex]);
            }
          }
          colorIndex = (colorIndex + 1) % colors.Length;
        }
      }
      this.stage.Flush();
    }

  }

}