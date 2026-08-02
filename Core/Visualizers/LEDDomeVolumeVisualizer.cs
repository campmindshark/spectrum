using System;
using System.Collections.Generic;
using Spectrum.Base;
using Spectrum.LEDs;

namespace Spectrum {

  class LEDDomeVolumeVisualizer : DomeLayerVisualizer {

    private readonly LayerRendererRuntime runtime;
    private readonly IAudioLevelInput audio;
    private readonly BeatBroadcaster beat;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;
    private int animationSize;
    private int centerOffset;

    private StrutLayout partLayout, sectionLayout;

    // There are only 4 possible centerOffset values (0-3), and each maps to a
    // fixed set of layouts. Precompute all four in the constructor and swap by
    // index in UpdateLayouts, rather than rerunning the allocation-heavy
    // ConcentricFromStartingPoints graph traversal up to 4x/beat on the
    // operator thread.
    private readonly StrutLayout[][] layoutsByCenterOffset = new StrutLayout[4][];

    private bool wipeStrutsNextCycle = false;

    public LEDDomeVolumeVisualizer(
      LayerRendererRuntime runtime,
      IAudioLevelInput audio,
      BeatBroadcaster beat,
      DomeRenderContext dome
    ) {
      this.runtime = runtime;
      this.audio = audio;
      this.beat = beat;
      this.dome = dome;
      this.buffer = this.dome.MakeDomeFrame();
      this.animationSize = 0;
      this.centerOffset = 0;
      for (int offset = 0; offset < this.layoutsByCenterOffset.Length; offset++) {
        this.layoutsByCenterOffset[offset] = this.BuildLayouts(offset);
      }
      this.partLayout = this.layoutsByCenterOffset[0][0];
      this.sectionLayout = this.layoutsByCenterOffset[0][2];
    }

    private StrutLayout[] BuildLayouts(int centerOffset) {
      int[] points = new int[] { 22, 26, 30, 34, 38, 70 };
      for (int i = 0; i < 5; i++) {
        points[i] += centerOffset;
      }
      if (points[4] >= 40) {
        points[4] -= 20;
      }
      return StrutLayoutFactory.ConcentricFromStartingPoints(
        new HashSet<int>(points),
        4
      );
    }

    private void UpdateLayouts() {
      StrutLayout[] layouts = this.layoutsByCenterOffset[this.centerOffset];
      this.partLayout = layouts[0];
      this.sectionLayout = layouts[2];
    }

    public int Priority => 2;

    public string LayerKey => "volume";
    public DomeFrame LayerBuffer => this.buffer;

    // Which named palette this layer draws from, resolved once per frame in
    // Visualize() and read by ColorFromPart.
    private int selectedPalette = 0;

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

    private Input[]? inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.audio });
    }

    public void Visualize() {
      if (this.wipeStrutsNextCycle) {
        for (int i = 0; i < this.dome.StrutCount; i++) {
          Strut strut = Strut.FromIndex(i);
          for (int j = 0; j < strut.Length; j++) {
            // Erase (transparent), not opaque black: an unlit strut should
            // reveal lower layers when Volume is a foreground Over layer.
            this.buffer.ClearPixel(i, j);
          }
        }
        this.wipeStrutsNextCycle = false;
      }

      // This layer's own tuning, read from its compiled runtime snapshot.
      VolumeLayerOptions options =
        this.runtime.GetOptions<VolumeLayerOptions>();
      int newAnimationSize = options.AnimationSize;
      double rotationSpeed = options.RotationSpeed;
      double gradientSpeed = options.GradientSpeed;
      this.selectedPalette = options.Palette;

      this.UpdateCenter(rotationSpeed);
      this.UpdateAnimationSize(newAnimationSize);

      // The gradient focus position advances with the beat but is identical for
      // every pixel this frame, so compute it once here rather than calling
      // ProgressThroughBeat (a lock + DateTime.Now) per pixel inside ColorFromPart.
      double gradientFocus = this.beat.ProgressThroughBeat(
        gradientSpeed
      );

      int totalParts = newAnimationSize;
      int volumeSplitInto = 2 * ((totalParts - 1) / 2 + 1);
      for (int part = 0; part < totalParts; part += 2) {
        var outwardSegment = this.partLayout.GetSegment(part);
        double startRange = (double)part / volumeSplitInto;
        double endRange = (double)(part + 2) / volumeSplitInto;
        double level = this.audio.Volume;
        double scaled = (level - startRange) /
          (endRange - startRange);
        scaled = Math.Max(Math.Min(scaled, 1.0), 0.0);
        startRange = Math.Min(startRange / level, 1.0);
        endRange = Math.Min(endRange / level, 1.0);

        foreach (Strut strut in outwardSegment.GetStruts()) {
          this.UpdateStrut(strut, scaled, startRange, endRange, gradientFocus);
        }

        if (part + 1 == totalParts) {
          break;
        }

        for (int i = 0; i < 6; i++) {
          StrutLayoutSegment segment =
            this.sectionLayout.GetSegment(i + part * 3);
          double gradientStartPos = 0.0;
          double gradientStep = 1.0 / segment.GetStruts().Count;
          foreach (Strut strut in segment.GetStruts()) {
            double gradientEndPos = gradientStartPos + gradientStep;
            this.UpdateStrut(
              strut,
              scaled == 1.0 ? 1.0 : 0.0,
              gradientStartPos,
              gradientEndPos,
              gradientFocus
            );
            gradientStartPos = gradientEndPos;
          }
        }
      }
    }

    private void UpdateAnimationSize(int newAnimationSize) {
      if (newAnimationSize == this.animationSize) {
        return;
      }
      if (newAnimationSize > this.animationSize) {
        // Growing: the newly animated parts are painted by the Visualize loop
        // this frame, so there's nothing to do here but record the new size.
        this.animationSize = newAnimationSize;
        return;
      }
      // Shrinking: the parts we're dropping won't be repainted this frame, so
      // they must be blacked out explicitly (the buffer persists across frames).
      for (int i = this.animationSize - 1; i >= newAnimationSize; i--) {
        foreach (Strut strut in this.partLayout.GetSegment(i).GetStruts()) {
          for (int j = 0; j < strut.Length; j++) {
            // Dropped parts are erased (transparent), so a foreground Volume
            // layer reveals what's below instead of stamping black.
            this.buffer.ClearPixel(strut.Index, j);
          }
        }
      }
      this.animationSize = newAnimationSize;
    }

    private void UpdateCenter(double rotationSpeed) {
      int newCenterOffset = (int)(
        this.beat.ProgressThroughBeat(
          rotationSpeed
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
      double endLitRange,
      double gradientFocus
    ) {
      for (int i = 0; i < strut.Length; i++) {
        double gradientPos =
          strut.GetGradientPos(percentageLit, startLitRange, endLitRange, i);
        if (gradientPos != -1.0) {
          int color = this.ColorFromPart(strut.Index, gradientPos, gradientFocus);
          this.buffer.SetPixel(strut.Index, i, color);
        } else {
          // Unlit portion of a partially-lit strut: erase (transparent) so lower
          // layers show through under Over, instead of painting opaque black.
          this.buffer.ClearPixel(strut.Index, i);
        }
      }
    }

    private int ColorFromPart(int strut, double pixelPos, double gradientFocus) {
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
        gradientFocus,
        true,
        this.selectedPalette
      );
    }

  }

}
