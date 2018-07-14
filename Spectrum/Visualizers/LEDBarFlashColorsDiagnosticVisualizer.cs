using Spectrum.Base;
using Spectrum.LEDs;
using System.Diagnostics;

namespace Spectrum {

  class LEDBarFlashColorsDiagnosticVisualizer : Visualizer {

    private Configuration config;
    private LEDBarOutput bar;
    private Stopwatch stopwatch;
    // 0: everything off, 1: everything on, 2: only borders on, 3: everything on
    private int state = 3;

    public LEDBarFlashColorsDiagnosticVisualizer(
      Configuration config,
      LEDBarOutput bar
    ) {
      this.config = config;
      this.bar = bar;
      this.bar.RegisterVisualizer(this);
      this.stopwatch = new Stopwatch();
      this.stopwatch.Start();
    }

    public int Priority {
      get {
        return this.config.barTestPattern == 1 ? 1000 : 0;
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
      if (this.stopwatch.ElapsedMilliseconds <= 1000) {
        return;
      }
      this.stopwatch.Restart();
      this.state = (this.state + 1) % 4;

      if (this.state == 0) {
        var totalInfinityPixels = 2 * this.config.barInfinityLength
          + 2 * this.config.barInfinityWidth;
        for (int i = 0; i < totalInfinityPixels; i++) {
          this.bar.SetPixel(false, i, 0x000000);
        }
        for (int i = 0; i < this.config.barRunnerLength; i++) {
          this.bar.SetPixel(true, i, 0x000000);
        }
        this.bar.Flush();
        return;
      }

      byte brightnessByte = (byte)(
        0xFF * this.config.barBrightness
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

      if (this.state == 2) {
        this.bar.SetPixel(false, 0, colors[0]);
        this.bar.SetPixel(false, this.config.barInfinityLength - 1, colors[0]);
        this.bar.SetPixel(
          false,
          this.config.barInfinityWidth + this.config.barInfinityLength,
          colors[2]
        );
        this.bar.SetPixel(
          false,
          this.config.barInfinityWidth + 2 * this.config.barInfinityLength - 1,
          colors[2]
        );
        for (int i = 1; i < this.config.barInfinityLength - 1; i++) {
          this.bar.SetPixel(false, i, 0x000000);
          this.bar.SetPixel(
            false,
            this.config.barInfinityWidth + this.config.barInfinityLength + i,
            0x000000
          );
        }
        this.bar.SetPixel(false, this.config.barInfinityLength, colors[1]);
        this.bar.SetPixel(
          false,
          this.config.barInfinityLength + this.config.barInfinityWidth - 1,
          colors[1]
        );
        this.bar.SetPixel(
          false,
          this.config.barInfinityWidth + 2 * this.config.barInfinityLength,
          colors[3]
        );
        this.bar.SetPixel(
          false,
          2 * this.config.barInfinityWidth + 2 * this.config.barInfinityLength - 1,
          colors[3]
        );
        for (int i = 1; i < this.config.barInfinityWidth - 1; i++) {
          this.bar.SetPixel(false, this.config.barInfinityLength + i, 0x000000);
          this.bar.SetPixel(
            false,
            this.config.barInfinityWidth + 2 * this.config.barInfinityLength + i,
            0x000000
          );
        }
        this.bar.SetPixel(true, 0, colors[4]);
        this.bar.SetPixel(true, this.config.barRunnerLength - 1, colors[4]);
        for (int i = 1; i < this.config.barRunnerLength - 1; i++) {
          this.bar.SetPixel(true, i, 0x000000);
        }
      } else {
        for (int i = 0; i < this.config.barInfinityLength; i++) {
          this.bar.SetPixel(false, i, colors[0]);
          this.bar.SetPixel(
            false,
            this.config.barInfinityWidth + this.config.barInfinityLength + i,
            colors[2]
          );
        }
        for (int i = 0; i < this.config.barInfinityWidth; i++) {
          this.bar.SetPixel(false, this.config.barInfinityLength + i, colors[1]);
          this.bar.SetPixel(
            false,
            this.config.barInfinityWidth + 2 * this.config.barInfinityLength + i,
            colors[3]
          );
        }
        for (int i = 0; i < this.config.barRunnerLength; i++) {
          this.bar.SetPixel(true, i, colors[4]);
        }
      }

      this.bar.Flush();
    }

  }

}