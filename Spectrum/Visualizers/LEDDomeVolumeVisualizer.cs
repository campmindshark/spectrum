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

    // This determines the direction in which the LEDs light up for a strut
    private static bool[] reverseDirection = new bool[] 
    {true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, true, true, false, true, true, true, false, true, true, true, false, true, true, true, false, true, true, false, true, false, false, true, false, false, true, false, false, true, false, false, true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, true, false, true, true, false, false, false, false, true, false, true, true, false, false, false, false, true, false, true, true, false, false, false, false, true, false, true, true, false, false, false, false, true, false, true, true, false, false, false, false, false, false, false, false, true, false, false, false, false, false, false, true, false, false, false, false, false, false, true, false, false, false, false, false, false, true, false, false, false, false, false, false, true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true};

    // This determines which part of the volume a strut represents
    private static byte[] partRepresented = new byte[] 
    {2, 1, 2, 9, 2, 1, 2, 9, 2, 1, 2, 9, 2, 1, 2, 9, 2, 1, 2, 9, 2, 0, 0, 2, 2, 0, 0, 2, 2, 0, 0, 2, 2, 0, 0, 2, 2, 0, 0, 2, 2, 2, 9, 2, 2, 9, 2, 2, 9, 2, 2, 9, 2, 2, 9, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 1, 1, 1, 1, 1, 3, 2, 1, 0, 0, 1, 2, 3, 3, 2, 1, 0, 0, 1, 2, 3, 3, 2, 1, 0, 0, 1, 2, 3, 3, 2, 1, 0, 0, 1, 2, 3, 3, 2, 1, 0, 0, 1, 2, 3, 3, 2, 1, 0, 1, 2, 3, 3, 2, 1, 0, 1, 2, 3, 3, 2, 1, 0, 1, 2, 3, 3, 2, 1, 0, 1, 2, 3, 3, 2, 1, 0, 1, 2, 3, 9, 3, 2, 3, 9, 9, 3, 2, 3, 9, 9, 3, 2, 3, 9, 9, 3, 2, 3, 9, 9, 3, 2, 3, 9, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 0, 0, 0, 0, 0 };

    // The total number of parts the volume is broken into
    private static int numParts = 4;

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
        return 2;
      }
    }

    // We don't actually care about this
    public bool Enabled { get; set; } = false;

    public Input[] GetInputs() {
      return new Input[] { this.audio };
    }

    public void Visualize() {
      for (int i = 0; i < LEDDomeOutput.GetNumStruts(); i++) {
        if (partRepresented[i] >= numParts) {
          continue;
        }

        int numLEDs = LEDDomeOutput.GetNumLEDs(i);
        int numLEDsToLight;
        // Odd parts are perpendicular to the volume direction
        if (partRepresented[i] % 2 == 0) {
          double startOfRange = (double)partRepresented[i] / numParts;
          double endOfRange = ((double)partRepresented[i] + 2) / numParts;
          double scaled = (this.audio.Volume - startOfRange) /
            (endOfRange - startOfRange);
          numLEDsToLight = (int)(numLEDs * scaled);
        } else {
          double minVolume = (double)(partRepresented[i] / 2 + 1) /
            (numParts / 2);
          numLEDsToLight = this.audio.Volume >= minVolume ? numLEDs : 0;
        }

        int brightnessByte = (int)(0xFF * this.config.domeMaxBrightness);
        int activeColor = brightnessByte
          | brightnessByte << 8
          | brightnessByte << 16;

        for (int j = 0; j < numLEDs; j++) {
          int ledIndex = reverseDirection[i]
            ? numLEDs - j
            : j;
          int color = ledIndex < numLEDsToLight
            ? activeColor
            : 0x000000;
          this.dome.SetPixel(i, j, color);
        }
      }
      this.dome.Flush();
    }

  }

}