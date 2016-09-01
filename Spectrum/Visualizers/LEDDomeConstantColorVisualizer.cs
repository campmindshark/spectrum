using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.LEDs;
using System.Diagnostics;

namespace Spectrum {

  class LEDDomeConstantColorVisualizer : Visualizer {

    private Configuration config;
    private LEDDomeOutput dome;
    private int startStrut = 0;
    private Stopwatch stopwatch;

    public LEDDomeConstantColorVisualizer(
      Configuration config,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.stopwatch = Stopwatch.StartNew();
    }

    public int Priority {
      get {
        return 0;
      }
    }

    // We don't actually care about this
    private bool enabled = false;
    public bool Enabled {
      get {
        return this.enabled;
      }
      set {
        if (this.enabled != value) {
          this.startStrut += 3;
        }
        this.enabled = value;
      }
    }

    public Input[] GetInputs() {
      return new Input[] {};
    }

    public void Visualize() {
      int seconds = (int)(this.stopwatch.ElapsedMilliseconds / 1000);
      for (int i = 0; i < LEDDomeOutput.GetNumStruts(); i++) {
        Strut strut = Strut.FromIndex(this.config, i);
        for (int j = 0; j < strut.Length; j++) {
          int color = seconds % 2 == 0 ? 0x000022 : 0x000000;
          this.dome.SetPixel(i, j, color);
        }
      }

      //for (int i = 0; i < 4; i++) {
      //  int strutIndex = (i + 19) % 20;
      //  Strut strut = Strut.FromIndex(this.config, strutIndex);
      //  for (int j = 0; j < strut.Length; j++) {
      //    this.dome.SetPixel(strutIndex, j, 0x0000FF);
      //  }
      //}
      //for (int i = 4; i < 8; i++) {
      //  int strutIndex = (i + 19) % 20;
      //  Strut strut = Strut.FromIndex(this.config, strutIndex);
      //  for (int j = 0; j < strut.Length; j++) {
      //    this.dome.SetPixel(strutIndex, j, 0x00FF00);
      //  }
      //}
      //for (int i = 8; i < 12; i++) {
      //  int strutIndex = (i + 19) % 20;
      //  Strut strut = Strut.FromIndex(this.config, strutIndex);
      //  for (int j = 0; j < strut.Length; j++) {
      //    this.dome.SetPixel(strutIndex, j, 0xFF0000);
      //  }
      //}
      //for (int i = 12; i < 16; i++) {
      //  int strutIndex = (i + 19) % 20;
      //  Strut strut = Strut.FromIndex(this.config, strutIndex);
      //  for (int j = 0; j < strut.Length; j++) {
      //    this.dome.SetPixel(strutIndex, j, 0xFF00FF);
      //  }
      //}
      //for (int i = 16; i < 20; i++) {
      //  int strutIndex = (i + 19) % 20;
      //  Strut strut = Strut.FromIndex(this.config, strutIndex);
      //  for (int j = 0; j < strut.Length; j++) {
      //    this.dome.SetPixel(strutIndex, j, 0xFFFFFF);
      //  }
      //}

      //Strut strut1 = Strut.FromIndex(this.config, this.startStrut);
      //for (int j = 0; j < strut1.Length; j++) {
      //  this.dome.SetPixel(strut1.Index, j, 0x0000FF);
      //}
      //Strut strut2 = Strut.FromIndex(this.config, this.startStrut + 1);
      //for (int j = 0; j < strut2.Length; j++) {
      //  this.dome.SetPixel(strut2.Index, j, 0x00FF00);
      //}
      //Strut strut3 = Strut.FromIndex(this.config, this.startStrut + 2);
      //for (int j = 0; j < strut3.Length; j++) {
      //  this.dome.SetPixel(strut3.Index, j, 0xFF0000);
      //}
      //for (int i = 0; i < 5; i++) {
      //  var strutIndex = LEDDomeOutput.FindStrutIndex(0, i);
      //  Strut strut = Strut.FromIndex(this.config, strutIndex);
      //  for (int j = 0; j < strut.Length; j++) {
      //    this.dome.SetPixel(i, j, 0x0000FF);
      //  }
      //}
      //for (int i = 5; i < 10; i++) {
      //  var strutIndex = LEDDomeOutput.FindStrutIndex(0, i);
      //  Strut strut = Strut.FromIndex(this.config, strutIndex);
      //  for (int j = 0; j < strut.Length; j++) {
      //    this.dome.SetPixel(i, j, 0x00FF00);
      //  }
      //}
      //for (int i = 10; i < 15; i++) {
      //  var strutIndex = LEDDomeOutput.FindStrutIndex(0, i);
      //  Strut strut = Strut.FromIndex(this.config, strutIndex);
      //  for (int j = 0; j < strut.Length; j++) {
      //    this.dome.SetPixel(i, j, 0xFF0000);
      //  }
      //}
      this.dome.Flush();
    }

  }

}
