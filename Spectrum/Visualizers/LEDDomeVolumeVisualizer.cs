using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Audio;

namespace Spectrum {

  class LEDDomeVolumeVisualizer : Visualizer {

    private Configuration config;
    private AudioInput audio;
    private LEDDomeOutput dome;

    public LEDDomeVolumeVisualizer(
      Configuration config,
      AudioInput audio,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.audio = audio;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
    }

    public int Priority {
      get {
        return 0;
      }
    }

    // We don't actually care about this
    public bool Enabled { get; set; } = false;

    public Input[] GetInputs() {
      return new Input[] { this.audio };
    }

    public void Visualize() {
      for (int i = 0; i < LEDDomeOutput.GetNumStruts(); i++) {
        int numLEDs = LEDDomeOutput.GetNumLEDs(i);
        int numLEDsToLight = (int)(this.audio.Volume * numLEDs);
        int brightnessByte = (int)(0xFF * this.config.domeMaxBrightness);
        int activeColor = brightnessByte
          | brightnessByte << 8
          | brightnessByte << 16;
        for (int j = 0; j < numLEDs; j++) {
          this.dome.SetPixel(i, j, numLEDsToLight > j ? activeColor : 0x000000);
        }
      }
      this.dome.Flush();
    }

  }

}