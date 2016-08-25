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

    // These names are completely arbitrary
    private static StrutLayout partLayout, indexLayout, spokeLayout, sectionLayout;
    static LEDDomeVolumeVisualizer() {
      int[][] strutsByPart = new int[][] {
        new int[] {
          73, 21, 113, 22, 74, 81, 25, 120, 26, 82, 29, 127, 30, 90, 89, 97, 33,
          134, 34, 98, 106, 38, 141, 37, 105, 185, 189, 188, 187, 186,
        },
        new int[] {
          1, 72, 112, 114, 75, 5, 80, 119, 121, 83, 9, 88, 126, 128, 91, 13, 96,
          133, 135, 99, 17, 104, 140, 142, 107, 65, 66, 67, 68, 69,
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
          109, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64,
        },
      };
      int[][] strutsByIndex = new int[][] {
        new int[] {
          0, 1, 2, 70, 71, 72, 73, 74, 75, 76, 77, 20, 21, 22, 23, 110, 111,
          112, 113, 114, 115, 116, 40, 41, 146, 147, 148,
        },
        new int[] {
          4, 5, 6, 78, 79, 80, 81, 82, 83, 84, 85, 24, 25, 26, 27, 117, 118,
          119, 120, 121, 122, 123, 43, 44, 151, 152, 153,
        },
        new int[] {
          8, 9, 10, 86, 87, 88, 89, 90, 91, 92, 93, 28, 29, 30, 31, 124, 125,
          126, 127, 128, 129, 130, 46, 47, 156, 157, 158,
        },
        new int[] {
          14, 13, 12, 101, 100, 99, 98, 97, 96, 95, 94, 35, 34, 33, 32, 137,
          136, 135, 134, 133, 132, 131, 50, 49, 163, 162, 161,
        },
        new int[] {
          16, 17, 18, 102, 103, 104, 105, 106, 107, 108, 109, 36, 37, 38, 39,
          138, 139, 140, 141, 142, 143, 144, 52, 53, 166, 167, 168,
        },
        new int[] {
          55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 170, 171, 172, 173, 174, 175,
          176, 177, 178, 179, 180, 181, 182, 183, 184, 65, 66, 67, 68, 69, 185,
          186, 187, 188, 189,
        },
      };
      int[][] strutsBySpoke = new int[][] {
        new int[] {
          73, 81, 89, 97, 105, 74, 82, 90, 98, 106, 0, 2, 4, 6, 8, 10, 12, 14,
          16, 18,
        },
        new int[] {
          21, 26, 29, 34, 37, 71, 20, 111, 122, 27, 84, 125, 28, 87, 100, 35,
          136, 103, 36, 139,
        },
        new int[] {
          22, 25, 30, 33, 38, 76, 23, 115, 118, 24, 79, 92, 31, 129, 132, 32,
          95, 108, 39, 143,
        },
        new int[] {
          113, 120, 127, 134, 141, 185, 186, 187, 188, 189, 147, 171, 152, 174,
          157, 177, 162, 180, 183, 167, 146, 148, 55, 56, 151, 153, 57, 58, 156,
          158, 59, 60, 161, 163, 61, 62, 166, 168, 63, 64, 40, 41, 43, 44, 46,
          47, 49, 50, 52, 53, 170, 172, 173, 175, 176, 178, 179, 181, 182, 184,
        },
      };
      int[][] strutsBySection = new int[][] {
        new int[] { 1, 72, 112, 114, 75 },
        new int[] { 5, 80, 119, 121, 83 },
        new int[] { 9, 88, 126, 128, 91 },
        new int[] { 13, 96, 133, 135, 99 },
        new int[] { 17, 104, 140, 142, 107 },
        new int[] { 65, 66, 67, 68, 69 },
        new int[] { 70, 110, 146, 148, 116, 77 },
        new int[] { 78, 117, 151, 153, 123, 85 },
        new int[] { 86, 124, 156, 158, 130, 93 },
        new int[] { 94, 131, 161, 163, 137, 101 },
        new int[] { 102, 138, 166, 168, 144, 109 },
        new int[] { 55, 56, 57, 58, 59, 60, 61, 62, 63, 64 },
      };
      HashSet<int> reversedStruts = new HashSet<int>() {
        71, 73, 74, 22, 81, 82, 26, 90, 30, 89, 97, 98, 34, 38, 106, 105, 185,
        189, 188, 187, 186, 0, 20, 41, 115, 23, 2, 4, 79, 24, 44, 122, 27, 6, 8,
        87, 28, 47, 129, 31, 10, 12, 95, 32, 50, 136, 35, 14, 16, 103, 36, 53,
        143, 39, 18, 183, 184, 170, 171, 172, 173, 174, 175, 176, 177, 178, 179,
        180, 181, 182,
      };
      partLayout = layoutFromArray(strutsByPart, reversedStruts);
      indexLayout = layoutFromArray(strutsByIndex, reversedStruts);
      spokeLayout = layoutFromArray(strutsBySpoke, reversedStruts);
      sectionLayout = layoutFromArray(strutsBySection, reversedStruts);
    }

    private static StrutLayout layoutFromArray(
      int[][] array,
      HashSet<int> reversedStruts
    ) {
      StrutLayoutSegment[] segments = new StrutLayoutSegment[array.Length];
      for (int i = 0; i < array.Length; i++) {
        segments[i] = new StrutLayoutSegment(new HashSet<Strut>(
          array[i].Select(
            strut => reversedStruts.Contains(strut)
              ? Strut.ReversedFromIndex(strut)
              : Strut.FromIndex(strut)
          )
        ));
      }
      return new StrutLayout(segments);
    }

    private Configuration config;
    private AudioInput audio;
    private LEDDomeOutput dome;
    private int animationSize;

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
      this.animationSize = 0;
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
      this.UpdateAnimationSize();

      int subdivisions = partLayout.NumSegments / 2;
      int totalParts = this.config.domeVolumeAnimationSize * 2;
      for (int part = 0; part < totalParts; part += 2) {
        var outwardSegment = partLayout.GetSegment(part);
        double startRange = (double)part / partLayout.NumSegments;
        double endRange = (double)(part + 2) / partLayout.NumSegments;
        double scaled = (this.audio.Volume - startRange) /
          (endRange - startRange);
        scaled = Math.Max(Math.Min(scaled, 1.0), 0.0);

        foreach (Strut strut in outwardSegment.GetStruts()) {
          this.UpdateStrut(strut, scaled, startRange, endRange);
        }

        for (int i = 0; i < 6; i++) {
          StrutLayoutSegment segment = sectionLayout.GetSegment(i + part * 3);
          double gradientStartPos = 0.0;
          double gradientStep = 1.0 / segment.GetStruts().Count;
          foreach (Strut strut in segment.GetStruts()) {
            double gradientEndPos = gradientStartPos + gradientStep;
            this.UpdateStrut(
              strut,
              scaled >= 1.0 ? 1.0 : 0.0,
              gradientStartPos,
              gradientEndPos
            );
            gradientStartPos = gradientEndPos;
          }
        }
      }
      this.dome.Flush();
    }

    private void UpdateAnimationSize() {
      int newAnimationSize = this.config.domeVolumeAnimationSize;
      if (newAnimationSize == this.animationSize) {
        return;
      }
      if (newAnimationSize > this.animationSize) {
        for (int i = this.animationSize * 2; i < newAnimationSize * 2; i++) {
          foreach (Strut strut in partLayout.GetSegment(i).GetStruts()) {
            this.dome.ReserveStrut(strut.Index);
          }
        }
        this.animationSize = newAnimationSize;
        return;
      }
      for (int i = this.animationSize * 2 - 1; i >= newAnimationSize * 2; i--) {
        foreach (Strut strut in partLayout.GetSegment(i).GetStruts()) {
          for (int j = 0; j < strut.Length; j++) {
            this.dome.SetPixel(strut.Index, j, 0x000000);
          }
          this.dome.ReleaseStrut(strut.Index);
        }
      }
      this.animationSize = newAnimationSize;
    }

    /**
     * percentageLit: what percentage of this strut should be lit?
     * startLitRange,endLitRange refer to the portion of the lit range this
     *   strut represents. if it's the first strut startLitRange is 0.0; f it's
     *   the last lit strut, then endLitRange is 1.0. keep in mind that the lit
     *   range is not the same as the whole range.
     */
    private void UpdateStrut(
      Strut strut,
      double percentageLit,
      double startLitRange,
      double endLitRange
    ) {
      double step = (endLitRange - startLitRange) / (strut.Length * percentageLit);
      for (int i = 0; i < strut.Length; i++) {
        int ledIndex = strut.Reversed ? strut.Length - i : i;
        double gradientPos = startLitRange + ledIndex * step;
        //int activeColor = this.ColorFromPart(strut.Index, gradientPos);
        //int activeColor = this.ColorFromIndex(strut.Index, gradientPos);
        //int activeColor = this.ColorFromRandom(strut.Index);
        int color;
        if (gradientPos <= 1.0) {
          color = this.ColorFromPartAndSpoke(strut.Index, gradientPos);
        } else {
          color = 0x000000;
        }
        this.dome.SetPixel(strut.Index, i, color);
      }
    }

    private int ColorFromIndex(int strut, double pixelPos) {
      int colorIndex;
      if (indexLayout.SegmentIndexOfStrutIndex(strut) == 0) {
        colorIndex = 1;
      } else if (indexLayout.SegmentIndexOfStrutIndex(strut) == 1) {
        colorIndex = 2;
      } else if (indexLayout.SegmentIndexOfStrutIndex(strut) == 2) {
        colorIndex = 3;
      } else if (indexLayout.SegmentIndexOfStrutIndex(strut) == 3) {
        colorIndex = 4;
      } else if (indexLayout.SegmentIndexOfStrutIndex(strut) == 4) {
        colorIndex = 5;
      } else if (indexLayout.SegmentIndexOfStrutIndex(strut) == 5) {
        colorIndex = 0;
      } else {
        throw new Exception("unsupported value");
      }
      return this.dome.GetGradientComputerColor(
        colorIndex,
        pixelPos,
        this.config.domeBeatBroadcaster.ProgressThroughMeasure
      );
    }

    private int ColorFromPart(int strut, double pixelPos) {
      int colorIndex;
      if (partLayout.SegmentIndexOfStrutIndex(strut) == 0) {
        colorIndex = 1;
      } else if (partLayout.SegmentIndexOfStrutIndex(strut) == 1) {
        colorIndex = 2;
      } else if (partLayout.SegmentIndexOfStrutIndex(strut) == 2) {
        colorIndex = 3;
      } else if (partLayout.SegmentIndexOfStrutIndex(strut) == 3) {
        colorIndex = 0;
      } else {
        throw new Exception("unsupported value");
      }
      return this.dome.GetGradientComputerColor(
        colorIndex,
        pixelPos,
        this.config.domeBeatBroadcaster.ProgressThroughMeasure
      );
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

    private int ColorFromPartAndSpoke(int strut, double pixelPos) {
      int colorIndex;
      if (partLayout.SegmentIndexOfStrutIndex(strut) == 1) {
        colorIndex = 1;
      } else if (partLayout.SegmentIndexOfStrutIndex(strut) == 3) {
        colorIndex = 2;
      } else if (spokeLayout.SegmentIndexOfStrutIndex(strut) == 0) {
        colorIndex = 3;
      } else if (spokeLayout.SegmentIndexOfStrutIndex(strut) == 1) {
        colorIndex = 4;
      } else if (spokeLayout.SegmentIndexOfStrutIndex(strut) == 2) {
        colorIndex = 5;
      } else if (spokeLayout.SegmentIndexOfStrutIndex(strut) == 3) {
        colorIndex = 0;
      } else {
        throw new Exception("unsupported value");
      }
      return this.dome.GetGradientComputerColor(
        colorIndex,
        pixelPos,
        this.config.domeBeatBroadcaster.ProgressThroughMeasure
      );
    }

  }

}