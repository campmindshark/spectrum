using System;
using System.Collections.Generic;

namespace Spectrum.Base {

  // Shared mutations for the ordered list of named palettes. A layer's
  // "palette" parameter is an index into this list. Every edit replaces a
  // detached branch and publishes one immutable show-state generation.
  public sealed class PaletteService {
    public const int SlotCount = DomePalette.SlotCount;
    public const int MaxPalettes = 64;
    public const int MaxNameLength = 64;
    public const string LayerParameterKey = "palette";

    private readonly Configuration config;
    private readonly ConfigurationEditor editor;
    private readonly IDomeShowStateConfiguration showState;

    public PaletteService(Configuration config) {
      this.config = config;
      this.editor = config as ConfigurationEditor ??
        throw new ArgumentException(
          "Palette configuration must support collection edits.",
          nameof(config));
      this.showState = config as IDomeShowStateConfiguration ??
        throw new ArgumentException(
          "Palette configuration must support atomic show-state updates.",
          nameof(config));
    }

    public IReadOnlyList<string> Names() => Names(this.config);

    public static IReadOnlyList<string> Names(Configuration? config) {
      var names = new List<string>();
      if (config != null) {
        foreach (DomePaletteSnapshot palette in config.domePalettes) {
          if (palette != null && !string.IsNullOrWhiteSpace(palette.Name)) {
            names.Add(palette.Name);
          }
        }
      }
      if (names.Count == 0) {
        names.Add("Palette 1");
      }
      return names;
    }

    // Append a palette. `colors` is copied; null creates an empty palette.
    public (bool ok, string? error) Add(
      string? name, LEDColor?[]? colors = null
    ) {
      name = NormalizeName(name);
      (bool ok, string? error) = ValidateName(name);
      if (!ok || name == null) {
        return (false, error);
      }
      List<DomePalette> current = ValidPalettes(
        DomePaletteSnapshot.ToPalettes(this.config.domePalettes));
      if (FindIndex(current, name) >= 0) {
        return (false, "a palette named " + name + " already exists");
      }
      if (current.Count >= MaxPalettes) {
        return (false, "too many palettes (max " + MaxPalettes + ")");
      }
      current.Add(new DomePalette {
        Name = name,
        Colors = DomePalette.CopyColors(colors),
      });
      this.editor.ReplaceDomePalettes(current);
      return (true, null);
    }

    // Replace one live palette's color-array reference and notify subscribers.
    // Used by the web editor; native indexer edits notify through the same object.
    public (bool ok, string? error) ReplaceColors(
      string? name, LEDColor?[]? colors
    ) {
      List<DomePalette> current =
        DomePaletteSnapshot.ToPalettes(this.config.domePalettes);
      int index = FindIndex(current, name);
      if (index < 0) {
        return (false, "no palette named " + name);
      }
      current[index] = new DomePalette {
        Name = current[index].Name,
        Colors = DomePalette.CopyColors(colors),
      };
      this.editor.ReplaceDomePalettes(current);
      return (true, null);
    }

    public (bool ok, string? error) Rename(
      string? oldName, string? newName
    ) {
      newName = NormalizeName(newName);
      (bool ok, string? error) = ValidateName(newName);
      if (!ok || newName == null) {
        return (false, error);
      }
      List<DomePalette> current = ValidPalettes(
        DomePaletteSnapshot.ToPalettes(this.config.domePalettes));
      int index = FindIndex(current, oldName);
      if (index < 0) {
        return (false, "no palette named " + oldName);
      }
      int collision = FindIndex(current, newName);
      if (collision >= 0 && collision != index) {
        return (false, "a palette named " + newName + " already exists");
      }
      current[index] = new DomePalette {
        Name = newName,
        Colors = DomePalette.CopyColors(current[index].Colors),
      };
      this.editor.ReplaceDomePalettes(current);
      return (true, null);
    }

    // Delete a palette while preserving the identity selected by every live and
    // saved-scene layer. Higher indices shift down; references to the deleted
    // palette fall back to the first remaining palette.
    public (bool ok, string? error) Delete(string? name) {
      List<DomePalette> current = ValidPalettes(
        DomePaletteSnapshot.ToPalettes(this.config.domePalettes));
      int removed = FindIndex(current, name);
      if (removed < 0) {
        return (false, "no palette named " + name);
      }
      if (current.Count <= 1) {
        return (false, "at least one palette is required");
      }
      current.RemoveAt(removed);
      this.showState.ApplyDomeShowState(new DomeShowStateUpdate(
        RemapStack(DomeLayerView.ToSettings(
          this.config.domeLayerStack), removed) ??
          new List<DomeLayerSettings>(),
        current,
        this.config.domeGlobalFadeSpeed,
        this.config.domeGlobalHueSpeed,
        RemapScenes(DomeSceneView.ToScenes(
          this.config.domeScenes), removed)));
      return (true, null);
    }

    public static DomePalette? Resolve(Configuration? config, int index) {
      if (config == null || config.domePalettes.IsDefaultOrEmpty) {
        return null;
      }
      if (index < 0 || index >= config.domePalettes.Length ||
          config.domePalettes[index] == null) {
        index = 0;
      }
      return index < config.domePalettes.Length
        ? config.domePalettes[index].ToPalette()
        : null;
    }

    private static List<DomePalette> ValidPalettes(
      List<DomePalette>? source
    ) {
      var result = new List<DomePalette>();
      if (source != null) {
        foreach (DomePalette palette in source) {
          if (palette != null && !string.IsNullOrWhiteSpace(palette.Name)) {
            result.Add(palette);
          }
        }
      }
      return result;
    }

    private static List<DomeLayerSettings>? RemapStack(
      List<DomeLayerSettings>? source, int removed
    ) {
      if (source == null) {
        return null;
      }
      var result = new List<DomeLayerSettings>(source.Count);
      foreach (DomeLayerSettings layer in source) {
        if (layer != null) {
          result.Add(CopyLayerWithRemappedPalette(layer, removed));
        }
      }
      return result;
    }

    private static List<DomeScene> RemapScenes(
      List<DomeScene>? source, int removed
    ) {
      if (source == null) {
        return new List<DomeScene>();
      }
      var result = new List<DomeScene>(source.Count);
      foreach (DomeScene scene in source) {
        if (scene == null) {
          continue;
        }
        result.Add(new DomeScene {
          Name = scene.Name,
          Layers = RemapStack(scene.Layers, removed),
          GlobalFadeSpeed = scene.GlobalFadeSpeed,
          GlobalHueSpeed = scene.GlobalHueSpeed,
        });
      }
      return result;
    }

    private static DomeLayerSettings CopyLayerWithRemappedPalette(
      DomeLayerSettings layer, int removed
    ) {
      Dictionary<string, double>? renderer = RemapBag(
        layer.RendererParams, removed);
      Dictionary<string, double>? operation = RemapBag(
        layer.OperationParams, removed);
      return new DomeLayerSettings {
        InstanceId = layer.InstanceId,
        VisualizerKey = layer.VisualizerKey,
        BlendMode = layer.BlendMode,
        Opacity = layer.Opacity,
        Enabled = layer.Enabled,
        Notes = layer.Notes,
        RendererParams = renderer,
        OperationParams = operation,
      };
    }

    private static Dictionary<string, double>? RemapBag(
      Dictionary<string, double>? source, int removed
    ) {
      Dictionary<string, double>? copy = source == null
        ? null
        : new Dictionary<string, double>(source);
      if (copy != null &&
          copy.TryGetValue(LayerParameterKey, out double raw)) {
        int selected = (int)Math.Round(raw);
        copy[LayerParameterKey] = selected == removed
          ? 0
          : selected > removed ? selected - 1 : selected;
      }
      return copy;
    }

    private static int FindIndex(
      List<DomePalette> palettes, string? name
    ) {
      if (name == null) {
        return -1;
      }
      for (int i = 0; i < palettes.Count; i++) {
        if (palettes[i] != null && string.Equals(
              palettes[i].Name, name, StringComparison.OrdinalIgnoreCase)) {
          return i;
        }
      }
      return -1;
    }

    private static string? NormalizeName(string? name) => name?.Trim();

    private static (bool ok, string? error) ValidateName(string? name) {
      if (string.IsNullOrEmpty(name)) {
        return (false, "palette name must not be empty");
      }
      if (name.Length > MaxNameLength) {
        return (false, "palette name too long (max " + MaxNameLength + ")");
      }
      return (true, null);
    }
  }

}
