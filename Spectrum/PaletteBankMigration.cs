using System.Collections.Generic;
using Spectrum.Base;

namespace Spectrum {

  // Forward migration for the retired 8-bank color palette. The HUD used to
  // carry eight banks of eight gradient pairs (colorPalette slots 0-63) with a
  // colorPaletteIndex selector; the render path now reads bank 0 only (slots
  // 0-7) and named DomePalette presets replace the switcher (docs/HUD_overhaul.md).
  //
  // This consolidates an old config on load:
  //  1. If a non-zero bank was selected, promote its eight slots into slots 0-7
  //     so the look on screen is unchanged across the upgrade.
  //  2. Save every populated bank 1-7 as a "Bank 2".."Bank 8" DomePalette preset
  //     (skipping a name already present in domePalettes), so nothing is lost.
  //  3. Clear slots 8-63 and reset colorPaletteIndex to 0.
  // The next config save persists the presets and the emptied banks, so the
  // migration never runs meaningfully twice (self-retiring, like
  // LegacyLayerParamMigration). A config that already only uses bank 0 passes
  // through untouched.
  //
  // Runs from MainWindow.LoadConfig before the Operator exists, so mutating the
  // palette in place is safe — nothing has been published to the operator thread
  // yet, and no PropertyChanged is needed.
  static class PaletteBankMigration {

    private const int SlotsPerBank = 8;
    private const int BankCount = 8;
    private const int TotalSlots = SlotsPerBank * BankCount; // 64

    public static void Apply(Configuration config) {
      LEDColorPalette palette = config.colorPalette;
      if (palette == null) {
        return;
      }
      LEDColor[] colors = palette.colors;
      int activeBank = config.colorPaletteIndex;
      bool higherBanksPopulated = AnyPopulated(colors, SlotsPerBank, TotalSlots);

      // Already consolidated: bank 0 selected and no data parked in banks 1-7.
      if (activeBank == 0 && !higherBanksPopulated) {
        return;
      }

      // A non-zero bank was selected but the palette is empty: just reset the
      // selector so stray writes to it stay harmless.
      if (colors == null) {
        config.colorPaletteIndex = 0;
        return;
      }

      // Work on a fresh 64-slot array so a short source array widens cleanly.
      var next = new LEDColor[TotalSlots];
      for (int i = 0; i < TotalSlots && i < colors.Length; i++) {
        next[i] = colors[i];
      }

      // 1. Promote the selected bank's eight slots into slots 0-7.
      if (activeBank > 0 && activeBank < BankCount) {
        for (int i = 0; i < SlotsPerBank; i++) {
          int src = activeBank * SlotsPerBank + i;
          next[i] = src < colors.Length ? colors[src] : null;
        }
      }

      // 2. Save every populated bank 1-7 (read from the original slots) as a
      //    named preset.
      var presets = config.domePalettes != null
        ? new List<DomePalette>(config.domePalettes)
        : new List<DomePalette>();
      for (int bank = 1; bank < BankCount; bank++) {
        int start = bank * SlotsPerBank;
        if (!AnyPopulated(colors, start, start + SlotsPerBank)) {
          continue;
        }
        string name = "Bank " + (bank + 1);
        if (NameExists(presets, name)) {
          continue;
        }
        var slots = new LEDColor[SlotsPerBank];
        for (int i = 0; i < SlotsPerBank; i++) {
          int src = start + i;
          LEDColor color = src < colors.Length ? colors[src] : null;
          slots[i] = color == null ? null : new LEDColor(color);
        }
        presets.Add(new DomePalette { Name = name, Colors = slots });
      }

      // 3. Clear banks 1-7 and reset the selector.
      for (int i = SlotsPerBank; i < TotalSlots; i++) {
        next[i] = null;
      }

      palette.colors = next;
      config.domePalettes = presets;
      config.colorPaletteIndex = 0;
    }

    // True if any slot in [start, end) holds a non-null color.
    private static bool AnyPopulated(LEDColor[] colors, int start, int end) {
      if (colors == null) {
        return false;
      }
      for (int i = start; i < end && i < colors.Length; i++) {
        if (colors[i] != null) {
          return true;
        }
      }
      return false;
    }

    private static bool NameExists(List<DomePalette> presets, string name) {
      foreach (DomePalette preset in presets) {
        if (
          preset != null &&
          string.Equals(
            preset.Name, name, System.StringComparison.OrdinalIgnoreCase
          )
        ) {
          return true;
        }
      }
      return false;
    }
  }
}
