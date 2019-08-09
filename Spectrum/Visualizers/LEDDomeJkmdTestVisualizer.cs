using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Spectrum {

  class LEDDomeJkmdTestVisualizer : Visualizer {

    private Configuration config;
    private AudioInput audio;
    private LEDDomeOutput dome;
    private int animationSize;
    private int centerOffset;

    private StrutLayout partLayout, indexLayout, sectionLayout;

    // For color-from-part mode, maps each part to a color
    private int[] partColors = new int[4];
    // For color-from-index mode, maps each index to a color
    private int[] indexColors = new int[6];
    // For color-from-part-and-spoke mode, maps each part/spoke to a color
    private int[] partAndSpokeColors = new int[5];
    // For color-from-random mode, maps each strut to a color
    private int[] randomStrutColors = new int[LEDDomeOutput.GetNumStruts()];
    private Random random = new Random();
    private bool wipeStrutsNextCycle = false;

    public LEDDomeJkmdTestVisualizer(
      Configuration config,
      AudioInput audio,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.audio = audio;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.animationSize = 0;
      this.centerOffset = 0;
      this.ringsLayout = this.BuildRingsLayout();
      //this.UpdateLayouts();
    }

    private StrutLayout ringsLayout;
    private StrutLayout BuildRingsLayout() {

      int[][] strutsByRing = new int[][] {
        new int [] { 0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19 },
        new int [] { 20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39 },
        new int [] { 40,41,42,43,44,45,46,47,48,49,50,51,52,53,54 },
        new int [] { 55,56,57,58,59,60,61,62,63,64 },
        new int [] { 65,66,67,68,69 }
      };

      StrutLayoutSegment[] segments =
        new StrutLayoutSegment[strutsByRing.Length];
      for (int i = 0; i < strutsByRing.Length; i++) {
        segments[i] = new StrutLayoutSegment(new HashSet<Strut>(
          strutsByRing[i].Select(
            strut => Strut.FromIndex(this.config, strut)
          )
        ));
      }
      return new StrutLayout(segments);
    }

    private void UpdateLayouts() {
      int[] points = new int[] { 22, 26, 30, 34, 38, 70 };
      for (int i = 0; i < 5; i++) {
        points[i] += this.centerOffset;
      }
      if (points[4] >= 40) {
        points[4] -= 20;
      }
      StrutLayout[] layouts = StrutLayoutFactory.ConcentricFromStartingPoints(
        this.config,
        new HashSet<int>(points),
        4
      );
      this.partLayout = layouts[0];
      this.indexLayout = layouts[1];
      this.sectionLayout = layouts[2];
    }

    public int Priority {
      get {
        return this.config.domeActiveVis == 999 ? 2 : 0;
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
          this.wipeStrutsNextCycle = true;
        }
        this.enabled = value;
      }
    }

    public Input[] GetInputs() {
      return new Input[] { this.audio };
    }

    public void Static() {
      for (int i = 0; i < LEDDomeOutput.GetNumStruts(); i++) {
        Strut strut = Strut.FromIndex(this.config, i);
        for (int j = 0; j < strut.Length; j++) {
          this.dome.SetPixel(i, j, random.Next(0x1000000));
        }
      }
    }
    public void StaticRings() {
      for (int i = 0; i < 5; i++) {
        StrutLayoutSegment segment =
          this.ringsLayout.GetSegment(i);


        foreach (Strut strut in segment.GetStruts()) {
          for (int j = 0; j < strut.Length; j++) {
            this.dome.SetPixel(strut.Index, j, random.Next(0x1000000));
          }
        }
      }
    }

    public void StaticRingsAnimated() {
      
      double progress = this.config.beatBroadcaster.ProgressThroughBeat(
        this.config.domeVolumeRotationSpeed
      );

      double level = this.audio.LevelForChannel(0);

      for (int i = 0; i < 5; i++) {
        StrutLayoutSegment segment =
          this.ringsLayout.GetSegment(i);

        double totalLength = segment.TotalLength;
        double totalPos = 0;
        foreach (Strut strut in segment.GetStruts()) {
          double frac = totalPos / totalLength;
          double dist = Math.Abs(progress - frac);
          dist = dist > 0.5 ? 1.0 - dist : dist;
          dist *= 2;
          double d = dist * dist * level * progress;
          int c = LEDColor.FromDoubles(d, d, d);

          for (int j = 0; j < strut.Length; j++, totalPos+=1) {
            this.dome.SetPixel(strut.Index, j, c);
          }
        }
      }
    }
    public void ParametricTest() {

      double progress = this.config.beatBroadcaster.ProgressThroughBeat(
        this.config.domeVolumeRotationSpeed
      );

      double level = this.audio.LevelForChannel(0);

      for (int i = 0; i < LEDDomeOutput.GetNumStruts(); i++) {
        Strut strut = Strut.FromIndex(this.config, i);
        var leds = LEDDomeOutput.GetNumLEDs(i);
        for (int j = 0; j < leds; j++) {
          var p = StrutLayoutFactory.GetProjectedLEDPointParametric(i, j);
          double r = 0;
          double g = 0;
          double b = 0;

          //radar effect
          double a = (p.Item3 + Math.PI) / (Math.PI * 2);
          r = progress - a;
          if (r < 0) r += 1;
          if (r > 1) r = 1;

          //pulse effect
          double dist = Math.Abs(progress - p.Item4);
          r = 1 - dist;
          if (r < 0.9) r = 0;

          //spiral effect
          double m = p.Item4 - a;
          if (m < 0) m += 1;
          double n = progress - m;
          if (n < 0) n += 1;
          r = 1 - n;

          this.dome.SetPixel(i, j, LEDColor.FromDoubles(r,g,b));
        }
      }
    }

    public void Visualize() {
      if (this.wipeStrutsNextCycle) {
        for (int i = 0; i < LEDDomeOutput.GetNumStruts(); i++) {
          Strut strut = Strut.FromIndex(this.config, i);
          for (int j = 0; j < strut.Length; j++) {
            this.dome.SetPixel(i, j, 0x000000);
          }
        }
        this.wipeStrutsNextCycle = false;
      }

      //this.StaticRingsAnimated();
      this.ParametricTest();

      //this.wipeStrutsNextCycle = true;
      this.dome.Flush();
    }

    private void UpdateAnimationSize(int newAnimationSize) {
      if (newAnimationSize == this.animationSize) {
        return;
      }
      if (newAnimationSize > this.animationSize) {
        for (int i = this.animationSize; i < newAnimationSize; i++) {
          foreach (Strut strut in this.partLayout.GetSegment(i).GetStruts()) {
            this.dome.ReserveStrut(strut.Index);
          }
        }
        this.animationSize = newAnimationSize;
        return;
      }
      for (int i = this.animationSize - 1; i >= newAnimationSize; i--) {
        foreach (Strut strut in this.partLayout.GetSegment(i).GetStruts()) {
          for (int j = 0; j < strut.Length; j++) {
            this.dome.SetPixel(strut.Index, j, 0x000000);
          }
          this.dome.ReleaseStrut(strut.Index);
        }
      }
      this.animationSize = newAnimationSize;
    }

    private void UpdateCenter() {
      int newCenterOffset = (int)(
        this.config.beatBroadcaster.ProgressThroughBeat(
          this.config.domeVolumeRotationSpeed
        ) * 4);
      if (newCenterOffset == this.centerOffset) {
        return;
      }
      this.centerOffset = newCenterOffset;
      // Force an update of reserved struts
      this.UpdateAnimationSize(0);
      this.UpdateLayouts();
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
        double gradientPos =
          strut.GetGradientPos(percentageLit, startLitRange, endLitRange, i);
        int color;
        if (gradientPos != -1.0) {
          color = this.ColorFromPart(strut.Index, gradientPos);
          //color = this.ColorFromIndex(strut.Index, gradientPos);
          //color = this.ColorFromRandom(strut.Index);
          //color = this.ColorFromPartAndSpoke(strut.Index, gradientPos);
        } else {
          color = 0x000000;
        }
        this.dome.SetPixel(strut.Index, i, color);
      }
    }

    private int ColorFromIndex(int strut, double pixelPos) {
      int colorIndex;
      if (this.indexLayout.SegmentIndexOfStrutIndex(strut) == 0) {
        colorIndex = 1;
      } else if (this.indexLayout.SegmentIndexOfStrutIndex(strut) == 1) {
        colorIndex = 2;
      } else if (this.indexLayout.SegmentIndexOfStrutIndex(strut) == 2) {
        colorIndex = 3;
      } else if (this.indexLayout.SegmentIndexOfStrutIndex(strut) == 3) {
        colorIndex = 4;
      } else if (this.indexLayout.SegmentIndexOfStrutIndex(strut) == 4) {
        colorIndex = 5;
      } else if (this.indexLayout.SegmentIndexOfStrutIndex(strut) == 5) {
        colorIndex = 0;
      } else {
        throw new Exception("unsupported value");
      }
      return this.dome.GetGradientColor(
        colorIndex,
        pixelPos,
        this.config.beatBroadcaster.ProgressThroughBeat(
          this.config.domeGradientSpeed
        ),
        true
      );
    }

    private int ColorFromPart(int strut, double pixelPos) {
      int colorIndex;
      if (this.partLayout.SegmentIndexOfStrutIndex(strut) == 0) {
        colorIndex = 1;
      } else if (this.partLayout.SegmentIndexOfStrutIndex(strut) == 1) {
        colorIndex = 2;
      } else if (this.partLayout.SegmentIndexOfStrutIndex(strut) == 2) {
        colorIndex = 3;
      } else if (this.partLayout.SegmentIndexOfStrutIndex(strut) == 3) {
        colorIndex = 0;
      } else {
        throw new Exception("unsupported value");
      }
      return this.dome.GetGradientColor(
        colorIndex,
        pixelPos,
        this.config.beatBroadcaster.ProgressThroughBeat(
          this.config.domeGradientSpeed
        ),
        true
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
      int brightnessByte = (int)(
        0xFF * this.config.domeMaxBrightness *
        this.config.domeBrightness
      );
      int color = 0;
      for (int i = 0; i < 3; i++) {
        color |= (int)(random.NextDouble() * brightnessByte) << (i * 8);
      }
      return color;
    }
  }

}
