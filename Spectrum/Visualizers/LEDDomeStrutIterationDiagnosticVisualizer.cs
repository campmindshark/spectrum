using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.MIDI;
using System.Diagnostics;

namespace Spectrum {

  /**
   * This Visualizer is used for testing the layout. It sets every strut to
   * blue, and then strut by strut illuminates every strut to the next color
   * in the order. Notably, it uses LEDDomeOutput.FindStrutIndex to determine
   * the order, which means it illuminates the struts in the order they are
   * plugged in.
   */
  class LEDDomeStrutIterationDiagnosticVisualizer : Visualizer {

    private Configuration config;
    private LEDDomeOutput dome;
    private Stopwatch stopwatch;
    private int lastIndex = 37;
    private int lastTeensy = 4;
    private int color = 0xFF0000;

    public LEDDomeStrutIterationDiagnosticVisualizer(
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
        return this.config.domeTestPattern == 2 ? 1000 : 0;
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
          this.lastIndex = 37;
          this.lastTeensy = 4;
          this.color = 0xFF0000;
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
      this.lastIndex++;
      byte brightnessByte = (byte)(
        0xFF * this.config.domeMaxBrightness *
        this.config.domeBrightness
      );
      int whiteColor = brightnessByte << 16
        | brightnessByte << 8
        | brightnessByte;
      if (this.lastIndex == 38) {
        this.lastIndex = 0;
        this.lastTeensy = (this.lastTeensy + 1) % 5;

        // Reset every LED to blue
        for (int i = 0; i < LEDDomeOutput.GetNumStruts(); i++) {
          Strut blueStrut = Strut.FromIndex(this.config, i);
          for (int j = 0; j < blueStrut.Length; j++) {
            this.dome.SetPixel(i, j, 0x0000FF);
          }
        }

        if (this.lastTeensy == 0) {
          if (this.color == 0xFF0000) {
            this.color = 0x00FF00;
          } else if (this.color == 0x00FF00) {
            this.color = 0x0000FF;
          } else if (this.color == 0x0000FF) {
            this.color = 0xFFFFFF;
          } else if (this.color == 0xFFFFFF) {
            this.color = 0xFF0000;
          }
        }
      }
      var strutIndex = LEDDomeOutput.FindStrutIndex(
        this.lastTeensy,
        this.lastIndex
      );
      Strut strut = Strut.FromIndex(this.config, strutIndex);
      for (int i = 0; i < strut.Length; i++) {
        this.dome.SetPixel(strutIndex, i, this.color & whiteColor);
      }
      this.dome.Flush();
    }

  }

}