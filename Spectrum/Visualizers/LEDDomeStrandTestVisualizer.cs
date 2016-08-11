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

  class LEDDomeStrandTestVisualizer : Visualizer {

    private Configuration config;
    private LEDDomeOutput dome;
    private Stopwatch stopwatch;
    private int lastIndex = 37;
    private int color = 0xFFFFFF;

    public LEDDomeStrandTestVisualizer(
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
        return 1;
      }
    }

    // We don't actually care about this
    public bool Enabled { get; set; } = false;

    public Input[] GetInputs() {
      return new Input[] {};
    }

    public void Visualize() {
      if (this.stopwatch.ElapsedMilliseconds <= 1000) {
        return;
      }
      this.stopwatch.Restart();
      this.lastIndex++;
      byte brightnessByte = (byte)(0xFF * this.config.domeMaxBrightness);
      int whiteColor = brightnessByte << 16
        | brightnessByte << 8
        | brightnessByte;
      if (this.lastIndex == 38) {
        this.lastIndex = 0;
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
      var strutIndex = LEDDomeOutput.FindStrutIndex(0, this.lastIndex);
      var numLEDs = LEDDomeOutput.GetNumLEDs(strutIndex);
      for (int i = 0; i < numLEDs; i++) {
        this.dome.SetPixel(strutIndex, i, this.color & whiteColor);
      }
      this.dome.Flush();
    }

  }

}