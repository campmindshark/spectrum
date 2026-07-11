using System.Collections.Generic;

namespace Spectrum.Base {

  /**
   * The shared, thread-agnostic core behind saving and recalling named palette
   * presets, exactly parallel to SceneService. The native DomePalettesController
   * (already on the UI thread) calls it directly; a future web palette surface
   * would wrap each call in ControlGateway.InvokeAsync. Every method assumes it
   * runs on the serialization thread (UI/Dispatcher) — it reads and writes
   * Configuration properties directly, so the PropertyChanged events land where
   * every subscriber expects.
   *
   * A preset captures just the eight-slot live palette (colorPalette slots 0-7,
   * the relative indices 0-7 every visualizer already consumes). Save and Apply
   * both deep-copy — a saved preset never aliases the live slots, and applying
   * never aliases the stored preset — so a later edit to one can't mutate the
   * other. Apply writes all eight slots through LEDColorPalette.ReplaceColors, so
   * the whole change fires a single Item[] notification.
   */
  public sealed class PaletteService {

    // Slots in one palette bank; a preset snapshots exactly one bank.
    public const int LiveSlots = 8;
    // Palette banks in colorPalette (64 slots = BankCount * LiveSlots). Each
    // dome layer picks its bank via its "palette" param.
    public const int BankCount = 8;
    // Guard rails, matching the MaxScenes / MaxNameLength style in SceneService.
    public const int MaxPalettes = 64;
    public const int MaxNameLength = 64;

    private readonly Configuration config;

    public PaletteService(Configuration config) {
      this.config = config;
    }

    // The saved preset names, in stored order. Never null.
    public IReadOnlyList<string> Names() {
      var names = new List<string>();
      List<DomePalette> palettes = this.config.domePalettes;
      if (palettes != null) {
        foreach (DomePalette palette in palettes) {
          if (palette != null && palette.Name != null) {
            names.Add(palette.Name);
          }
        }
      }
      return names;
    }

    // Snapshot the current live palette (slots 0-7) under `name`, overwriting an
    // existing preset with the same name (case-insensitive). Returns
    // (false, error) on a bad name or when the cap is hit and the name is new;
    // the caller (each UI) confirms an overwrite before calling.
    public (bool ok, string error) Save(string name, int bank = 0) {
      name = name == null ? null : name.Trim();
      (bool ok, string error) = ValidateName(name);
      if (!ok) {
        return (false, error);
      }
      var palette = new DomePalette {
        Name = name,
        Colors = Snapshot(this.config, bank),
      };
      // Copy-on-write: build a fresh list so the swap fires PropertyChanged and
      // the operator/serialization threads never observe a mid-mutation list.
      var next = new List<DomePalette>();
      bool replaced = false;
      List<DomePalette> current = this.config.domePalettes;
      if (current != null) {
        foreach (DomePalette existing in current) {
          if (existing == null) {
            continue;
          }
          if (NameEquals(existing.Name, name)) {
            next.Add(palette); // overwrite in place, preserving order
            replaced = true;
          } else {
            next.Add(existing);
          }
        }
      }
      if (!replaced) {
        if (next.Count >= MaxPalettes) {
          return (false, "too many palettes (max " + MaxPalettes + ")");
        }
        next.Add(palette);
      }
      this.config.domePalettes = next;
      return (true, null);
    }

    // Recall the named preset: deep-copy its eight slots into the chosen bank's
    // slots (bank*8 .. bank*8+7) in a single Item[] notification (see Restore).
    public (bool ok, string error) Apply(string name, int bank = 0) {
      DomePalette palette = Find(this.config.domePalettes, name);
      if (palette == null) {
        return (false, "no palette named " + name);
      }
      Restore(this.config, palette.Colors, bank);
      return (true, null);
    }

    // Remove the named preset. A no-op (still ok) if it doesn't exist.
    public (bool ok, string error) Delete(string name) {
      List<DomePalette> current = this.config.domePalettes;
      if (current == null) {
        return (true, null);
      }
      var next = new List<DomePalette>();
      foreach (DomePalette existing in current) {
        if (existing != null && !NameEquals(existing.Name, name)) {
          next.Add(existing);
        }
      }
      if (next.Count != current.Count) {
        this.config.domePalettes = next;
      }
      return (true, null);
    }

    // Rename a preset. Fails on a bad new name, an unknown old name, or a
    // collision with a different existing preset.
    public (bool ok, string error) Rename(string oldName, string newName) {
      newName = newName == null ? null : newName.Trim();
      (bool ok, string error) = ValidateName(newName);
      if (!ok) {
        return (false, error);
      }
      List<DomePalette> current = this.config.domePalettes;
      if (Find(current, oldName) == null) {
        return (false, "no palette named " + oldName);
      }
      if (!NameEquals(oldName, newName) && Find(current, newName) != null) {
        return (false, "a palette named " + newName + " already exists");
      }
      var next = new List<DomePalette>();
      foreach (DomePalette existing in current) {
        if (existing == null) {
          continue;
        }
        if (NameEquals(existing.Name, oldName)) {
          next.Add(new DomePalette {
            Name = newName,
            Colors = existing.Colors,
          });
        } else {
          next.Add(existing);
        }
      }
      this.config.domePalettes = next;
      return (true, null);
    }

    // Deep-copy one bank's eight slots (bank*8 .. bank*8+7) into a fresh
    // eight-element array, so a caller storing the result (a scene or a named
    // preset) never aliases the live LEDColor instances. A missing/short palette
    // yields null slots. Shared with SceneService, whose scenes snapshot bank 0
    // the same way (default bank).
    public static LEDColor[] Snapshot(Configuration config, int bank = 0) {
      var slots = new LEDColor[LiveSlots];
      LEDColor[] live = config.colorPalette == null
        ? null
        : config.colorPalette.colors;
      int start = bank * LiveSlots;
      for (int i = 0; i < LiveSlots; i++) {
        int src = start + i;
        LEDColor color = live != null && src < live.Length ? live[src] : null;
        slots[i] = color == null ? null : new LEDColor(color);
      }
      return slots;
    }

    // Write the supplied slots back into one bank's slots (bank*8 .. bank*8+7) in
    // a single Item[] notification. ReplaceColors deep-copies, so the stored
    // snapshot is never aliased by the live slots. A null `slots` (e.g. a scene
    // saved before scenes captured the palette) leaves the palette untouched.
    public static void Restore(Configuration config, LEDColor[] slots, int bank = 0) {
      if (slots == null || config.colorPalette == null) {
        return;
      }
      config.colorPalette.ReplaceColors(bank * LiveSlots, slots);
    }

    private static (bool ok, string error) ValidateName(string name) {
      if (string.IsNullOrEmpty(name)) {
        return (false, "palette name must not be empty");
      }
      if (name.Length > MaxNameLength) {
        return (false, "palette name too long (max " + MaxNameLength + ")");
      }
      return (true, null);
    }

    private static DomePalette Find(List<DomePalette> palettes, string name) {
      if (palettes == null || name == null) {
        return null;
      }
      foreach (DomePalette palette in palettes) {
        if (palette != null && NameEquals(palette.Name, name)) {
          return palette;
        }
      }
      return null;
    }

    private static bool NameEquals(string a, string b) =>
      string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);
  }
}
