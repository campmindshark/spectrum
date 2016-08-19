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

    private static AnimationLayout layout;
    static LEDDomeVolumeVisualizer() {
      int[][] strutsByPart = new int[][] {
        new int[] {
          73, 21, 113, 22, 74, 81, 25, 120, 26, 82, 29, 127, 30, 90, 89, 97, 33,
          134, 34, 98, 106, 38, 141, 37, 105, 185, 189, 188, 187, 186,
        },
        new int[] {
          1, 72, 112, 114, 75, 5, 80, 119, 121, 83, 126, 128, 91, 9, 88, 96,
          133, 135, 99, 13, 107, 17, 104, 140, 142, 69, 68, 67, 66, 65, 
        },
        new int[] {
          0, 71, 20, 111, 40, 147, 41, 115, 23, 76, 2, 4, 79, 24, 118, 43, 152,
          44, 122, 27, 84, 6, 8, 87, 28, 125, 46, 157, 47, 129, 31, 92, 10, 12,
          95, 32, 132, 49, 162, 50, 136, 35, 100, 14, 14, 16, 103, 36, 139, 52,
          167, 53, 143, 39, 108, 18, 183, 184, 170, 171, 172, 173, 174, 175,
          176, 177, 178, 179, 180, 181, 182,
        },
        new int[] {
          70, 110, 146, 148, 116, 77, 78, 117, 151, 153, 123, 85, 86, 124, 156,
          158, 130, 93, 94, 131, 161, 163, 137, 101, 102, 138, 166, 168, 144,
          109, 64, 55, 56, 57, 58, 59, 60, 61, 62, 63,
        },
      };
      HashSet<int> reversedStruts = new HashSet<int>() {
        71, 73, 74, 22, 81, 82, 26, 90, 30, 89, 97, 98, 34, 38, 106, 105, 185,
        189, 188, 187, 186, 0, 20, 41, 115, 23, 2, 4, 79, 24, 44, 122, 27, 6, 8,
        87, 28, 47, 129, 31, 10, 12, 95, 32, 50, 136, 35, 14, 16, 103, 36, 53,
        143, 39, 18, 183, 184, 170, 171, 172, 173, 174, 175, 176, 177, 178, 179,
        180, 181, 182,
      };
      AnimationLayoutSegment[] segments = new AnimationLayoutSegment[4];
      for (int i = 0; i < 4; i++) {
        segments[i] = new AnimationLayoutSegment(new HashSet<Strut>(
          strutsByPart[i].Select(
            index => reversedStruts.Contains(index)
              ? Strut.ReversedFromIndex(index)
              : Strut.FromIndex(index)
          )
        ));
      }
      layout = new AnimationLayout(segments);
    }

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
      int subdivisions = layout.NumSegments / 2;
      for (int part = 0; part < layout.NumSegments; part += 2) {
        var outwardSegment = layout.GetSegment(part);
        var surroundingSegment = layout.GetSegment(part + 1);
        double startOfRange = (double)part / layout.NumSegments;
        double endOfRange = (double)(part + 2) / layout.NumSegments;
        double scaled = (this.audio.Volume - startOfRange) /
          (endOfRange - startOfRange);
        foreach (Strut strut in outwardSegment.GetStruts()) {
          this.UpdateStrut(strut, (int)(strut.Length * scaled));
        }
        foreach (Strut strut in surroundingSegment.GetStruts()) {
          this.UpdateStrut(strut, scaled >= 1.0 ? strut.Length : 0);
        }
      }
      this.dome.Flush();
    }

    private void UpdateStrut(Strut strut, int numLEDsToLight) {
      for (int i = 0; i < strut.Length; i++) {
        //int activeColor = this.ColorFromIndex(strut.Index);
        //int activeColor = this.ColorFromPart(strut.Index);
        //int activeColor = this.ColorFromRandom(strut.Index);
        int activeColor = this.ColorFromPartAndSpoke(strut.Index);
        int ledIndex = strut.Reversed ? strut.Length - i : i;
        int color = ledIndex < numLEDsToLight
          ? GradientColor(
              (double)ledIndex / numLEDsToLight,
              1.0,
              activeColor,
              0x000000
            )
          : 0x000000;
        this.dome.SetPixel(strut.Index, i, color);
      }
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
      if (layout.SegmentIndexOfStrutIndex(strut) == 0) {
        return brightnessByte;
      } else if (layout.SegmentIndexOfStrutIndex(strut) == 1) {
        return brightnessByte << 8;
      } else if (layout.SegmentIndexOfStrutIndex(strut) == 2) {
        return brightnessByte << 16;
      } else if (layout.SegmentIndexOfStrutIndex(strut) == 3) {
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
      if (layout.SegmentIndexOfStrutIndex(strut) == 1) {
        return brightnessByte; // blue
      } else if (layout.SegmentIndexOfStrutIndex(strut) == 3) {
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