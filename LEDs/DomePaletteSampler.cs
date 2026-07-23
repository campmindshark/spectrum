using System;
using Spectrum.Base;

namespace Spectrum.LEDs {

  // Operator-thread-owned palette lookup and frame cache. A frame captures one
  // accepted show-state generation and one brightness snapshot so every layer
  // samples the same values without repeating beat-clock and settings reads
  // for each of the dome's pixels.
  internal sealed class DomePaletteSampler {
    private readonly BeatBroadcaster beat;
    private readonly Func<DomeRenderGeneration> generationSource;
    private readonly Func<DomeRuntimeFrameSnapshot> runtimeSource;
    private DomeRenderGeneration? frameGeneration;
    private DomeRuntimeFrameSnapshot frameRuntime =
      DomeRuntimeFrameSnapshot.Empty;
    private bool frameCacheValid;
    private bool frameFlashedOff;
    private double frameBrightness;

    public DomePaletteSampler(
      BeatBroadcaster beat,
      Func<DomeRenderGeneration> generationSource,
      Func<DomeRuntimeFrameSnapshot> runtimeSource
    ) {
      this.beat = beat ?? throw new ArgumentNullException(nameof(beat));
      this.generationSource = generationSource ??
        throw new ArgumentNullException(nameof(generationSource));
      this.runtimeSource = runtimeSource ??
        throw new ArgumentNullException(nameof(runtimeSource));
    }

    public void BeginFrame(
      DomeRenderGeneration generation,
      DomeRuntimeFrameSnapshot runtime
    ) {
      this.frameGeneration = generation ??
        throw new ArgumentNullException(nameof(generation));
      this.frameRuntime = runtime ??
        throw new ArgumentNullException(nameof(runtime));
      this.frameCacheValid = false;
    }

    public void EndFrame() {
      this.frameCacheValid = false;
      this.frameGeneration = null;
    }

    public int GetSingleColor(int index, int paletteIndex = 0) {
      DomeRenderGeneration generation = this.EnsureFrameCache();
      if (this.frameFlashedOff) {
        return 0x000000;
      }
      DomePaletteSnapshot? palette =
        generation.ShowState.ResolvePalette(paletteIndex);
      return LEDColor.ScaleColor(
        palette == null ? 0x000000 : palette.GetSingleColor(index),
        this.frameBrightness);
    }

    public int GetGradientColor(
      int index,
      double pixelPos,
      double focusPos,
      bool wrap,
      int paletteIndex = 0
    ) {
      DomeRenderGeneration generation = this.EnsureFrameCache();
      if (this.frameFlashedOff) {
        return 0x000000;
      }
      DomePaletteSnapshot? palette =
        generation.ShowState.ResolvePalette(paletteIndex);
      if (
        palette == null ||
        index < 0 || index >= palette.Colors.Length ||
        palette.Colors[index] == null
      ) {
        return 0x000000;
      }
      DomeColorSnapshot? selected = palette.Colors[index];
      if (!selected.HasValue || !selected.Value.IsGradient) {
        return LEDColor.ScaleColor(
          palette.GetSingleColor(index),
          this.frameBrightness);
      }
      return LEDColor.ScaleColor(
        palette.GetGradientColor(index, pixelPos, focusPos, wrap),
        this.frameBrightness);
    }

    public int GetGradientBetweenColors(
      int minIndex,
      int maxIndex,
      double pixelPos,
      double focusPos,
      bool wrap,
      int paletteIndex = 0
    ) {
      if (double.IsNaN(pixelPos) || double.IsInfinity(pixelPos) ||
          pixelPos < 0 || pixelPos > 1) {
        throw new ArgumentException(
          "Pixel Position out of range: " + pixelPos.ToString());
      }
      if (minIndex < 0) {
        throw new ArgumentOutOfRangeException(
          nameof(minIndex), "Minimum color index cannot be negative.");
      }
      if (maxIndex <= minIndex) {
        throw new ArgumentException(
          "Maximum color index must be greater than minimum color index.",
          nameof(maxIndex));
      }
      if (paletteIndex < 0) {
        throw new ArgumentOutOfRangeException(
          nameof(paletteIndex), "Palette index cannot be negative.");
      }

      DomeRenderGeneration generation = this.EnsureFrameCache();
      if (this.frameFlashedOff) {
        return 0x000000;
      }
      int colorCount = maxIndex - minIndex;
      int segment = (int)(pixelPos * colorCount);
      if (segment >= colorCount) {
        segment = colorCount - 1;
      }
      int minColorIndex = minIndex + segment;
      int maxColorIndex = minColorIndex + 1;
      double scaledPixelPosition = pixelPos * colorCount - segment;
      if (scaledPixelPosition < 0) {
        scaledPixelPosition = 0;
      } else if (scaledPixelPosition > 1) {
        scaledPixelPosition = 1;
      }

      DomePaletteSnapshot? palette =
        generation.ShowState.ResolvePalette(paletteIndex);
      if (
        palette == null ||
        palette.Colors.Length <= minColorIndex ||
        palette.Colors[minColorIndex] == null
      ) {
        return 0x000000;
      }
      if (
        palette.Colors.Length <= maxColorIndex ||
        palette.Colors[maxColorIndex] == null
      ) {
        return 0x000000;
      }
      DomeColorSnapshot? firstColor = palette.Colors[minColorIndex];
      if (!firstColor.HasValue || !firstColor.Value.IsGradient) {
        return this.GetSingleColor(minColorIndex, paletteIndex);
      }

      // Read the unscaled endpoints directly and apply frame brightness once
      // after interpolation.
      var color = new DomeColorSnapshot(
        palette.GetSingleColor(minColorIndex),
        palette.GetSingleColor(maxColorIndex),
        true);
      return LEDColor.ScaleColor(
        color.GradientColor(
          scaledPixelPosition, focusPos, wrap),
        this.frameBrightness);
    }

    private DomeRenderGeneration EnsureFrameCache() {
      if (this.frameCacheValid) {
        return this.frameGeneration ?? DomeRenderGeneration.Empty;
      }
      this.frameFlashedOff = this.beat.CurrentlyFlashedOff;
      DomeRuntimeFrameSnapshot runtime =
        this.frameGeneration == null
          ? this.runtimeSource()
          : this.frameRuntime;
      this.frameBrightness =
        runtime.MaxBrightness * runtime.Brightness;
      this.frameGeneration ??= this.generationSource();
      this.frameCacheValid = true;
      return this.frameGeneration;
    }
  }
}
