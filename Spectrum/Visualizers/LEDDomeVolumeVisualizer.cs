using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Audio;
using System.Diagnostics;

namespace Spectrum {

  class LEDDomeVolumeVisualizer : Visualizer {

    // The 0th part is the inner struts of a pentagon, the 1th part is the
    // perimeter of the pentagon, and etc. in an outward direction
    private static byte[] partRepresented = new byte[] {
      2, 1, 2, 9, 2, 1, 2, 9, 2, 1, 2, 9, 2, 1, 2, 9, 2, 1, 2, 9, 2, 0, 0, 2,
      2, 0, 0, 2, 2, 0, 0, 2, 2, 0, 0, 2, 2, 0, 0, 2, 2, 2, 9, 2, 2, 9, 2, 2,
      9, 2, 2, 9, 2, 2, 9, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 1, 1, 1, 1, 1, 3, 2,
      1, 0, 0, 1, 2, 3, 3, 2, 1, 0, 0, 1, 2, 3, 3, 2, 1, 0, 0, 1, 2, 3, 3, 2,
      1, 0, 0, 1, 2, 3, 3, 2, 1, 0, 0, 1, 2, 3, 3, 2, 1, 0, 1, 2, 3, 3, 2, 1,
      0, 1, 2, 3, 3, 2, 1, 0, 1, 2, 3, 3, 2, 1, 0, 1, 2, 3, 3, 2, 1, 0, 1, 2,
      3, 9, 3, 2, 3, 9, 9, 3, 2, 3, 9, 9, 3, 2, 3, 9, 9, 3, 2, 3, 9, 9, 3, 2,
      3, 9, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 0, 0, 0, 0, 0
    };
    // This determines which of the six pentagons a strut is part of
    private static byte[] indexRepresented = new byte[] {
      0, 0, 0, 9, 1, 1, 1, 9, 2, 2, 2, 9, 3, 3, 3, 9, 4, 4, 4, 9, 0, 0, 0, 0,
      1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 0, 0, 9, 1, 1, 9, 2, 2,
      9, 3, 3, 9, 4, 4, 9, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 0, 0,
      0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3,
      3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1,
      1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4,
      4, 9, 0, 0, 0, 9, 9, 1, 1, 1, 9, 9, 2, 2, 2, 9, 9, 3, 3, 3, 9, 9, 4, 4,
      4, 9, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5
    };
    // Uhh this one is fairly complicated but it's mostly spoke-based?
    private static byte[] spokeRepresented = new byte[] {
      0, 9, 0, 9, 0, 9, 0, 9, 0, 9, 0, 9, 0, 9, 0, 9, 0, 9, 0, 9, 1, 1, 2, 2, 2,
      2, 1, 1, 1, 1, 2, 2, 2, 2, 1, 1, 1, 1, 2, 2, 3, 3, 9, 3, 3, 9, 3, 3, 9, 3,
      3, 9, 3, 3, 9, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 9, 9, 9, 9, 9, 9, 1, 9, 0, 0,
      9, 2, 9, 9, 2, 9, 0, 0, 9, 1, 9, 9, 1, 9, 0, 0, 9, 2, 9, 9, 2, 9, 0, 0, 9,
      1, 9, 9, 1, 9, 0, 0, 9, 2, 9, 9, 1, 9, 3, 9, 2, 9, 9, 2, 9, 3, 9, 1, 9, 9,
      1, 9, 3, 9, 2, 9, 9, 2, 9, 3, 9, 1, 9, 9, 1, 9, 3, 9, 2, 9, 9, 3, 3, 3, 9,
      9, 3, 3, 3, 9, 9, 3, 3, 3, 9, 9, 3, 3, 3, 9, 9, 3, 3, 3, 9, 3, 3, 3, 3, 3,
      3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3
    };
    // We want all the struts to light up in the direction away from the center
    // of each pentagon, so we reverse the direction of some struts
    private static bool[] reverseDirection = new bool[] {
      true, false, true, false, true, false, true, false, true, false, true,
      false, true, false, true, false, true, false, true, false, true, false,
      true, true, true, false, true, true, true, false, true, true, true, false,
      true, true, true, false, true, true, false, true, false, false, true,
      false, false, true, false, false, true, false, false, true, false, false,
      false, false, false, false, false, false, false, false, false, false,
      false, false, false, false, false, true, false, true, true, false, false,
      false, false, true, false, true, true, false, false, false, false, true,
      false, true, true, false, false, false, false, true, false, true, true,
      false, false, false, false, true, false, true, true, false, false, false,
      false, false, false, false, false, true, false, false, false, false,
      false, false, true, false, false, false, false, false, false, true, false,
      false, false, false, false, false, true, false, false, false, false,
      false, false, true, false, false, false, false, false, false, false,
      false, false, false, false, false, false, false, false, false, false,
      false, false, false, false, false, false, false, false, false, true, true,
      true, true, true, true, true, true, true, true, true, true, true, true,
      true, true, true, true, true, true
    };

    // The total number of parts the volume is broken into
    private static int numParts = 4;

    private Configuration config;
    private AudioInput audio;
    private LEDDomeOutput dome;

    // For color-from-part mode, maps each part to a color
    private int[] partColors = new int[4];
    // For color-from-index mode, maps each index to a color
    private int[] indexColors = new int[6];
    // For color-from-part-and-spoke mode, maps each part/spoke to a color
    private int[] partAndSpokeColors = new int[5];
    // For color-from-random mode, maps each strut to a color
    private int[] randomStrutColors = new int[190];
    private Random random = new Random();

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

        for (int j = 0; j < numLEDs; j++) {
          //int activeColor = this.ColorFromIndex(i);
          //int activeColor = this.ColorFromPart(i);
          //int activeColor = this.ColorFromRandom(i);
          int activeColor = this.ColorFromPartAndSpoke(i);

          int ledIndex = reverseDirection[i]
            ? numLEDs - j
            : j;
          int color = ledIndex < numLEDsToLight
            ? GradientColor(
                (double)ledIndex / numLEDsToLight,
                1.0,
                activeColor,
                0x000000
              )
            : 0x000000;
          this.dome.SetPixel(i, j, color);
        }
      }
      this.dome.Flush();
    }

    private int ColorFromIndex(int strut) {
      int brightnessByte = (int)(0xFF * this.config.domeMaxBrightness);
      if (indexRepresented[strut] == 0) {
        return brightnessByte; // blue
      } else if (indexRepresented[strut] == 1) {
        return brightnessByte << 8; // green
      } else if (indexRepresented[strut] == 2) {
        return brightnessByte << 16; // red
      } else if (indexRepresented[strut] == 3) {
        return brightnessByte
          | brightnessByte << 8; // teal
      } else if (indexRepresented[strut] == 4) {
        return brightnessByte
          | brightnessByte << 16; // purple
      } else if (indexRepresented[strut] == 5) {
        return brightnessByte
          | brightnessByte << 8
          | brightnessByte << 16; // white
      }
      throw new Exception("unsupported value");
    }

    private int ColorFromPart(int strut) {
      int brightnessByte = (int)(0xFF * this.config.domeMaxBrightness);
      if (partRepresented[strut] == 0) {
        return brightnessByte;
      } else if (partRepresented[strut] == 1) {
        return brightnessByte << 8;
      } else if (partRepresented[strut] == 2) {
        return brightnessByte << 16;
      } else if (partRepresented[strut] == 3) {
        return brightnessByte
          | brightnessByte << 8
          | brightnessByte << 16;
      }
      throw new Exception("unsupported value");
    }

    private int ColorFromRandom(int strut) {
      int color = this.randomStrutColors[strut];
      if (color == 0) {
        color = this.RandomColor();
        this.randomStrutColors[strut] = color;
      }
      return color;
    }

    private int RandomColor() {
      int brightnessByte = (int)(0xFF * this.config.domeMaxBrightness);
      int color = 0;
      for (int i = 0; i < 3; i++) {
        color |= (int)(random.NextDouble() * brightnessByte) << (i * 8);
      }
      return color;
    }

    private int ColorFromPartAndSpoke(int strut) {
      int brightnessByte = (int)(0xFF * this.config.domeMaxBrightness);
      if (partRepresented[strut] == 1) {
        return brightnessByte; // blue
      } else if (partRepresented[strut] == 3) {
        return brightnessByte << 8; // green
      }
      if (spokeRepresented[strut] == 0) {
        return brightnessByte << 16; // red
      } else if (spokeRepresented[strut] == 1) {
        return brightnessByte
          | brightnessByte << 8; // teal
      } else if (spokeRepresented[strut] == 2) {
        return brightnessByte
          | brightnessByte << 16; // purple
      } else if (spokeRepresented[strut] == 3) {
        return brightnessByte
          | brightnessByte << 8
          | brightnessByte << 16; // white
      }
      throw new Exception("unsupported value");
    }

    private static int GradientColor(
      double pixelPos,
      double focusPos,
      int colorA,
      int colorB
    ) {
      // Distance given that 1.0 wraps to 0.0
      double distance = Math.Min(
        Math.Abs(pixelPos - focusPos),
        Math.Abs(pixelPos + 1 - focusPos)
      );
      byte redA = (byte)(colorA >> 16);
      byte greenA = (byte)(colorA >> 8);
      byte blueA = (byte)colorA;
      byte redB = (byte)(colorB >> 16);
      byte greenB = (byte)(colorB >> 8);
      byte blueB = (byte)colorB;
      byte blendedRed = (byte)((distance * redA) + (1 - distance) * redB);
      byte blendedGreen = (byte)((distance * greenA) + (1 - distance) * greenB);
      byte blendedBlue = (byte)((distance * blueA) + (1 - distance) * blueB);
      return (blendedRed << 16) | (blendedGreen << 8) | blendedBlue;
    }

  }

}