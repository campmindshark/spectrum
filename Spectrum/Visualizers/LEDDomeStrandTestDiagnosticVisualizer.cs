using Spectrum.Base;
using Spectrum.LEDs;
using System.Diagnostics;

namespace Spectrum {

  class LEDDomeStrandTestDiagnosticVisualizer : Visualizer {

    private Configuration config;
    private LEDDomeOutput dome;
    private Stopwatch stopwatch;
    // 0: everything off, 1: everything on
    private int state = 1;

    public LEDDomeStrandTestDiagnosticVisualizer(
      Configuration config,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.stopwatch = new Stopwatch();
      this.stopwatch.Start();
    }

    public int Priority {
      get {
        return this.config.domeTestPattern == 3 ? 1000 : 0;
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
      if (this.stopwatch.ElapsedMilliseconds <= 250) {
        return;
      }
      this.stopwatch.Restart();
      this.state = (this.state + 1) % 2;

      if (this.state == 0) {
        for (int i = 0; i < LEDDomeOutput.GetNumStruts(); i++) {
          Strut strut = Strut.FromIndex(this.config, i);
          for (int j = 0; j < strut.Length; j++) {
            this.dome.SetPixel(i, j, 0x000000);
          }
        }
        this.dome.Flush();
        return;
      }

      byte brightnessByte = (byte)(
        0xFF * this.config.domeMaxBrightness *
        this.config.domeBrightness
      );
      int color = brightnessByte << 16
        | brightnessByte << 8
        | brightnessByte;

      for (int controlBox = 0; controlBox < 5; controlBox++) {
        for (int localIndex = 0; localIndex < 38; localIndex++) {
          var strutIndex = LEDDomeOutput.FindStrutIndex(controlBox, localIndex);
          Strut strut = Strut.FromIndex(this.config, strutIndex);
          if (this.state == 2) {
            for (int j = 1; j < strut.Length - 1; j++) {
              this.dome.SetPixel(strutIndex, j, 0x000000);
            }
            this.dome.SetPixel(strutIndex, 0, color);
            this.dome.SetPixel(strutIndex, strut.Length - 1, color);
          } else {
            for (int j = 0; j < strut.Length; j++) {
              this.dome.SetPixel(strutIndex, j, color);
            }
          }
        }
      }
      this.dome.Flush();
    }

  }

}