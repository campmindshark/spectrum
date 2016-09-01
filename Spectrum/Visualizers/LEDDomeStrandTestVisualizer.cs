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
    private int lastTeensy = 1;
    private int color = 0xFF0000;

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
        return 0;
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