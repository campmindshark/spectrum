using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Spectrum.Base {

  // One immutable color slot compiled from the serializer-facing LEDColor DTO.
  // Render threads never retain LEDColor references because their public
  // properties are mutable for XML serialization.
  public readonly record struct DomeColorSnapshot(
    int Color1, int Color2, bool IsGradient
  ) {
    public int GradientColor(
      double pixelPos, double focusPos, bool wrap
    ) {
      if (!this.IsGradient) {
        return this.Color1;
      }
      double distance;
      if (wrap) {
        distance = Math.Min(
          Math.Abs(pixelPos - focusPos),
          1 - Math.Abs(pixelPos - focusPos)
        ) * 2.0;
      } else {
        distance = Math.Abs(pixelPos - focusPos);
      }
      byte redA = (byte)(this.Color1 >> 16);
      byte greenA = (byte)(this.Color1 >> 8);
      byte blueA = (byte)this.Color1;
      byte redB = (byte)(this.Color2 >> 16);
      byte greenB = (byte)(this.Color2 >> 8);
      byte blueB = (byte)this.Color2;
      byte blendedRed = (byte)((distance * redA) + (1 - distance) * redB);
      byte blendedGreen = (byte)((distance * greenA) + (1 - distance) * greenB);
      byte blendedBlue = (byte)((distance * blueA) + (1 - distance) * blueB);
      return (blendedRed << 16) | (blendedGreen << 8) | blendedBlue;
    }
  }

  public sealed record DomePaletteSnapshot(
    string? Name,
    ImmutableArray<DomeColorSnapshot?> Colors
  ) {
    public DomePalette ToPalette() {
      var colors = new LEDColor?[DomePalette.SlotCount];
      for (int i = 0; i < colors.Length; i++) {
        DomeColorSnapshot? color = i < this.Colors.Length
          ? this.Colors[i]
          : null;
        colors[i] = !color.HasValue
          ? null
          : color.Value.IsGradient
            ? new LEDColor(color.Value.Color1, color.Value.Color2)
            : new LEDColor(color.Value.Color1);
      }
      return new DomePalette { Name = this.Name, Colors = colors };
    }

    public static List<DomePalette> ToPalettes(
      ImmutableArray<DomePaletteSnapshot> palettes
    ) {
      var result = new List<DomePalette>(palettes.Length);
      foreach (DomePaletteSnapshot palette in palettes) {
        result.Add(palette.ToPalette());
      }
      return result;
    }

    public int GetSingleColor(int index) {
      DomeColorSnapshot? color = this.ColorAt(index);
      return color?.Color1 ?? 0x000000;
    }

    public int GetGradientColor(
      int index, double pixelPos, double focusPos, bool wrap
    ) {
      DomeColorSnapshot? color = this.ColorAt(index);
      return color?.GradientColor(pixelPos, focusPos, wrap) ?? 0x000000;
    }

    private DomeColorSnapshot? ColorAt(int index) =>
      index >= 0 && index < this.Colors.Length
        ? this.Colors[index]
        : null;
  }

  // The complete persisted generation that defines a rendered look. One
  // reference swap publishes its layer inputs, palettes, and global effects;
  // an operator frame captures that reference once and never mixes generations.
  public sealed record DomeShowStateSnapshot(
    long Generation,
    LayerStackSnapshot LayerStack,
    ImmutableArray<DomePaletteSnapshot> Palettes,
    double GlobalFadeSpeed,
    double GlobalHueSpeed
  ) {
    // PropertyChanged uses this synthetic name once after a complete show-state
    // commit. Runtime and SSE consumers listen to it instead of reconstructing
    // a transaction from several serializer-property notifications.
    public const string NotificationPropertyName = "DomeShowStateSnapshot";

    public static DomeShowStateSnapshot Empty { get; } =
      new DomeShowStateSnapshot(
        0,
        LayerStackSnapshot.Empty,
        ImmutableArray<DomePaletteSnapshot>.Empty,
        0,
        1);

    public DomePaletteSnapshot? ResolvePalette(int index) {
      if (this.Palettes.IsDefaultOrEmpty) {
        return null;
      }
      if (index < 0 || index >= this.Palettes.Length) {
        index = 0;
      }
      return this.Palettes[index];
    }

    public static ImmutableArray<DomePaletteSnapshot> CompilePalettes(
      IReadOnlyList<DomePalette?>? palettes
    ) {
      if (palettes == null || palettes.Count == 0) {
        return ImmutableArray<DomePaletteSnapshot>.Empty;
      }
      var compiled = ImmutableArray.CreateBuilder<DomePaletteSnapshot>(
        palettes.Count);
      for (int paletteIndex = 0;
          paletteIndex < palettes.Count;
          paletteIndex++) {
        DomePalette? palette = palettes[paletteIndex];
        if (palette == null) {
          compiled.Add(new DomePaletteSnapshot(
            null, ImmutableArray<DomeColorSnapshot?>.Empty));
          continue;
        }
        var colors = ImmutableArray.CreateBuilder<DomeColorSnapshot?>(
          DomePalette.SlotCount);
        for (int colorIndex = 0;
            colorIndex < DomePalette.SlotCount;
            colorIndex++) {
          LEDColor? color = palette.Colors != null &&
              colorIndex < palette.Colors.Length
            ? palette.Colors[colorIndex]
            : null;
          colors.Add(color == null
            ? null
            : new DomeColorSnapshot(
                color.Color1,
                color.IsGradient ? color.Color2 : color.Color1,
                color.IsGradient));
        }
        compiled.Add(new DomePaletteSnapshot(
          palette.Name, colors.MoveToImmutable()));
      }
      return compiled.MoveToImmutable();
    }
  }

  // A short-lived owner-thread command. The lists use detached document DTOs;
  // SpectrumConfiguration compiles the immutable render snapshot before
  // publishing any of them.
  public sealed record DomeShowStateUpdate(
    List<DomeLayerSettings> Layers,
    List<DomePalette> Palettes,
    double GlobalFadeSpeed,
    double GlobalHueSpeed,
    List<DomeScene> Scenes
  ) {
    public bool PalettesChanged { get; init; } = true;
    public bool ScenesChanged { get; init; } = true;
  }

  public interface IDomeShowStateConfiguration {
    DomeShowStateSnapshot DomeShowStateSnapshot { get; }
    void ApplyDomeShowState(DomeShowStateUpdate update);
  }

  // The render plan and every non-runtime value it consumes are accepted or
  // retained together. If candidate compilation fails, the output continues to
  // expose the prior object unchanged.
  public sealed record DomeRenderGeneration(
    RenderPlan Plan,
    DomeShowStateSnapshot ShowState
  ) {
    public static DomeRenderGeneration Empty { get; } =
      new DomeRenderGeneration(RenderPlan.Empty, DomeShowStateSnapshot.Empty);
  }
}
