using System.Collections.Generic;

namespace Spectrum.Base {

  /**
   * The shared, thread-agnostic core behind saving and recalling dome scenes.
   * Both surfaces call into it so the logic can't diverge: the native
   * DomeScenesController (already on the UI thread) calls it directly; the web
   * SceneController wraps each call in ApplicationStateDispatcher.InvokeAsync. Every method
   * therefore assumes it runs on the state-owner thread (UI/Dispatcher). It
   * reads immutable Configuration views and writes through ConfigurationEditor,
   * so PropertyChanged events land where every subscriber expects.
   *
   * A scene captures the whole layer stack plus the two cross-layer globals (see
   * DomeScene). Save and Apply both deep-copy — a saved scene never aliases the
   * live stack, and applying never aliases the stored scene — so a later edit to
   * one can't mutate the other. Apply routes the stored stack through the shared
   * StackValidator, so a scene authored against a slightly different schema is
   * normalized (params clamped, unknown keys dropped) the same way a web
   * PUT /api/layers is.
   *
   * Layer instance IDs are part of the scene snapshot and are deliberately
   * preserved by both copies. The runtime caches state by instance ID and
   * renderer kind, so recalling a scene resumes matching trails/playheads;
   * a missing/new ID or a changed renderer kind starts a fresh renderer.
   */
  public sealed class SceneService {

    // Guard rails, matching the MaxLayers style in StackValidator.
    public const int MaxScenes = 64;
    public const int MaxNameLength = 64;

    private readonly Configuration config;
    private readonly ConfigurationEditor editor;
    private readonly IDomeShowStateConfiguration showState;
    private readonly LayerCatalog layerCatalog;

    public SceneService(Configuration config, LayerCatalog layerCatalog) {
      this.config = config;
      this.editor = config as ConfigurationEditor ??
        throw new System.ArgumentException(
          "Scene configuration must support collection edits.",
          nameof(config));
      this.layerCatalog = layerCatalog ??
        throw new System.ArgumentNullException(nameof(layerCatalog));
      this.showState = config as IDomeShowStateConfiguration ??
        throw new System.ArgumentException(
          "Scene configuration must support atomic show-state updates.",
          nameof(config));
    }

    // The saved scene names, in stored order. Never null.
    public IReadOnlyList<string> Names() {
      var names = new List<string>();
      if (!this.config.domeScenes.IsDefaultOrEmpty) {
        foreach (DomeSceneView scene in this.config.domeScenes) {
          if (scene != null && scene.Name != null) {
            names.Add(scene.Name);
          }
        }
      }
      return names;
    }

    // Snapshot the current stack + globals under `name`, overwriting an existing
    // scene with the same name (case-insensitive). Returns (false, error) on a
    // bad name or when the cap is hit and the name is new; the caller (each UI)
    // is responsible for confirming an overwrite before calling.
    public (bool ok, string? error) Save(string? name) {
      name = name == null ? null : name.Trim();
      (bool ok, string? error) = ValidateName(name);
      if (!ok || name == null) {
        return (false, error);
      }
      var scene = new DomeScene {
        Name = name,
        Layers = DomeLayerView.ToSettings(this.config.domeLayerStack),
        GlobalFadeSpeed = this.config.domeGlobalFadeSpeed,
        GlobalHueSpeed = this.config.domeGlobalHueSpeed,
      };
      // Copy-on-write: build a fresh list so the snapshot swap fires
      // PropertyChanged and the operator/serialization threads never observe a
      // mid-mutation list.
      var next = new List<DomeScene>();
      bool replaced = false;
      List<DomeScene> current = DomeSceneView.ToScenes(this.config.domeScenes);
      if (current != null) {
        foreach (DomeScene existing in current) {
          if (existing == null) {
            continue;
          }
          if (NameEquals(existing.Name, name)) {
            next.Add(scene); // overwrite in place, preserving order
            replaced = true;
          } else {
            next.Add(existing);
          }
        }
      }
      if (!replaced) {
        if (next.Count >= MaxScenes) {
          return (false, "too many scenes (max " + MaxScenes + ")");
        }
        next.Add(scene);
      }
      this.editor.ReplaceDomeScenes(next);
      return (true, null);
    }

    // Recall the named scene: deep-copy its layers (preserving instance IDs),
    // run them through the shared validator, then publish the stack and both
    // globals as one immutable show-state generation.
    public (bool ok, string? error) Apply(string? name) {
      DomeSceneView? scene = Find(this.config.domeScenes, name);
      if (scene == null) {
        return (false, "no scene named " + name);
      }
      // Validate produces a fresh list of fresh DomeLayerSettings (with fresh
      // parameter bags) without mutating its input, so the published stack never
      // aliases the stored scene — no separate deep copy needed here.
      (List<DomeLayerSettings>? stack, string? error) =
        StackValidator.Validate(
          DomeLayerView.ToSettings(scene.Layers), this.layerCatalog);
      if (error != null) {
        return (false, error);
      }
      if (stack == null) {
        return (false, "scene validation returned no layer stack");
      }
      this.showState.ApplyDomeShowState(new DomeShowStateUpdate(
        stack,
        DomePaletteSnapshot.ToPalettes(this.config.domePalettes),
        scene.GlobalFadeSpeed,
        scene.GlobalHueSpeed,
        DomeSceneView.ToScenes(this.config.domeScenes)) {
          PalettesChanged = false,
          ScenesChanged = false,
        });
      return (true, null);
    }

    // Remove the named scene. A no-op (still ok) if it doesn't exist.
    public (bool ok, string? error) Delete(string? name) {
      List<DomeScene> current = DomeSceneView.ToScenes(this.config.domeScenes);
      if (current.Count == 0) {
        return (true, null);
      }
      var next = new List<DomeScene>();
      foreach (DomeScene existing in current) {
        if (existing != null && !NameEquals(existing.Name, name)) {
          next.Add(existing);
        }
      }
      if (next.Count != current.Count) {
        this.editor.ReplaceDomeScenes(next);
      }
      return (true, null);
    }

    // Rename a scene. Fails on a bad new name, an unknown old name, or a
    // collision with a different existing scene.
    public (bool ok, string? error) Rename(
      string? oldName, string? newName
    ) {
      newName = newName == null ? null : newName.Trim();
      (bool ok, string? error) = ValidateName(newName);
      if (!ok || newName == null) {
        return (false, error);
      }
      List<DomeScene> current = DomeSceneView.ToScenes(this.config.domeScenes);
      if (Find(this.config.domeScenes, oldName) == null) {
        return (false, "no scene named " + oldName);
      }
      if (!NameEquals(oldName, newName) &&
          Find(this.config.domeScenes, newName) != null) {
        return (false, "a scene named " + newName + " already exists");
      }
      var next = new List<DomeScene>();
      foreach (DomeScene existing in current) {
        if (existing == null) {
          continue;
        }
        if (NameEquals(existing.Name, oldName)) {
          next.Add(new DomeScene {
            Name = newName,
            Layers = existing.Layers,
            GlobalFadeSpeed = existing.GlobalFadeSpeed,
            GlobalHueSpeed = existing.GlobalHueSpeed,
          });
        } else {
          next.Add(existing);
        }
      }
      this.editor.ReplaceDomeScenes(next);
      return (true, null);
    }

    private static (bool ok, string? error) ValidateName(string? name) {
      if (string.IsNullOrEmpty(name)) {
        return (false, "scene name must not be empty");
      }
      if (name.Length > MaxNameLength) {
        return (false, "scene name too long (max " + MaxNameLength + ")");
      }
      return (true, null);
    }

    private static DomeSceneView? Find(
      System.Collections.Immutable.ImmutableArray<DomeSceneView> scenes,
      string? name
    ) {
      if (name == null) {
        return null;
      }
      foreach (DomeSceneView scene in scenes) {
        if (scene != null && NameEquals(scene.Name, name)) {
          return scene;
        }
      }
      return null;
    }

    private static bool NameEquals(string? a, string? b) =>
      string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);

  }
}
