using System.Collections.Generic;

namespace Spectrum.Base {

  /**
   * The shared, thread-agnostic core behind saving and recalling dome scenes.
   * Both surfaces call into it so the logic can't diverge: the native
   * DomeScenesController (already on the UI thread) calls it directly; the web
   * SceneController wraps each call in ControlGateway.InvokeAsync. Every method
   * therefore assumes it runs on the serialization thread (UI/Dispatcher) — it
   * reads and writes Configuration properties directly, exactly like a native GUI
   * write, so the PropertyChanged events land where every subscriber expects.
   *
   * A scene captures the whole layer stack plus the two cross-layer globals (see
   * DomeScene). Save and Apply both deep-copy — a saved scene never aliases the
   * live stack, and applying never aliases the stored scene — so a later edit to
   * one can't mutate the other. Apply routes the stored stack through the shared
   * StackValidator, so a scene authored against a slightly different schema is
   * normalized (params clamped, unknown keys dropped) the same way a web
   * PUT /api/layers is.
   */
  public sealed class SceneService {

    // Guard rails, matching the MaxLayers style in StackValidator.
    public const int MaxScenes = 64;
    public const int MaxNameLength = 64;

    private readonly Configuration config;

    public SceneService(Configuration config) {
      this.config = config;
    }

    // The saved scene names, in stored order. Never null.
    public IReadOnlyList<string> Names() {
      var names = new List<string>();
      List<DomeScene> scenes = this.config.domeScenes;
      if (scenes != null) {
        foreach (DomeScene scene in scenes) {
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
    public (bool ok, string error) Save(string name) {
      name = name == null ? null : name.Trim();
      (bool ok, string error) = ValidateName(name);
      if (!ok) {
        return (false, error);
      }
      var scene = new DomeScene {
        Name = name,
        Layers = DeepCopyStack(this.config.domeLayerStack),
        GlobalFadeSpeed = this.config.domeGlobalFadeSpeed,
        GlobalHueSpeed = this.config.domeGlobalHueSpeed,
        Palette = PaletteService.Snapshot(this.config),
      };
      // Copy-on-write: build a fresh list so the snapshot swap fires
      // PropertyChanged and the operator/serialization threads never observe a
      // mid-mutation list.
      var next = new List<DomeScene>();
      bool replaced = false;
      List<DomeScene> current = this.config.domeScenes;
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
      this.config.domeScenes = next;
      return (true, null);
    }

    // Recall the named scene: deep-copy its layers, run them through the shared
    // validator, then set domeLayerStack + the two globals. Three config writes
    // => three PropertyChanged events (order irrelevant); every subscriber
    // (native layer rows, SSE frames, the operator) converges through the
    // existing whole-stack plumbing.
    public (bool ok, string error) Apply(string name) {
      DomeScene scene = Find(this.config.domeScenes, name);
      if (scene == null) {
        return (false, "no scene named " + name);
      }
      // Validate produces a fresh list of fresh DomeLayerSettings (with fresh
      // Params bags) without mutating its input, so the published stack never
      // aliases the stored scene — no separate deep copy needed here.
      (List<DomeLayerSettings> stack, string error) =
        StackValidator.Validate(scene.Layers);
      if (error != null) {
        return (false, error);
      }
      this.config.domeLayerStack = stack;
      this.config.domeGlobalFadeSpeed = scene.GlobalFadeSpeed;
      this.config.domeGlobalHueSpeed = scene.GlobalHueSpeed;
      // Restore the captured palette (one more Item[] notification). A null
      // palette — any scene saved before this field existed — leaves the live
      // palette alone.
      PaletteService.Restore(this.config, scene.Palette);
      return (true, null);
    }

    // Remove the named scene. A no-op (still ok) if it doesn't exist.
    public (bool ok, string error) Delete(string name) {
      List<DomeScene> current = this.config.domeScenes;
      if (current == null) {
        return (true, null);
      }
      var next = new List<DomeScene>();
      foreach (DomeScene existing in current) {
        if (existing != null && !NameEquals(existing.Name, name)) {
          next.Add(existing);
        }
      }
      if (next.Count != current.Count) {
        this.config.domeScenes = next;
      }
      return (true, null);
    }

    // Rename a scene. Fails on a bad new name, an unknown old name, or a
    // collision with a different existing scene.
    public (bool ok, string error) Rename(string oldName, string newName) {
      newName = newName == null ? null : newName.Trim();
      (bool ok, string error) = ValidateName(newName);
      if (!ok) {
        return (false, error);
      }
      List<DomeScene> current = this.config.domeScenes;
      if (Find(current, oldName) == null) {
        return (false, "no scene named " + oldName);
      }
      if (!NameEquals(oldName, newName) && Find(current, newName) != null) {
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
            Palette = existing.Palette,
          });
        } else {
          next.Add(existing);
        }
      }
      this.config.domeScenes = next;
      return (true, null);
    }

    private static (bool ok, string error) ValidateName(string name) {
      if (string.IsNullOrEmpty(name)) {
        return (false, "scene name must not be empty");
      }
      if (name.Length > MaxNameLength) {
        return (false, "scene name too long (max " + MaxNameLength + ")");
      }
      return (true, null);
    }

    private static DomeScene Find(List<DomeScene> scenes, string name) {
      if (scenes == null || name == null) {
        return null;
      }
      foreach (DomeScene scene in scenes) {
        if (scene != null && NameEquals(scene.Name, name)) {
          return scene;
        }
      }
      return null;
    }

    private static bool NameEquals(string a, string b) =>
      string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);

    // Deep-copy a layer stack, including a fresh Params dictionary per layer, so
    // neither the live stack nor a stored scene ever aliases the other. Mirrors
    // the copy in DomeLayersController.Publish / LayersController.SerializeStack.
    private static List<DomeLayerSettings> DeepCopyStack(
      List<DomeLayerSettings> stack
    ) {
      if (stack == null) {
        return null;
      }
      var copy = new List<DomeLayerSettings>(stack.Count);
      foreach (DomeLayerSettings layer in stack) {
        if (layer == null) {
          continue;
        }
        copy.Add(new DomeLayerSettings {
          VisualizerKey = layer.VisualizerKey,
          BlendMode = layer.BlendMode,
          Opacity = layer.Opacity,
          Enabled = layer.Enabled,
          Notes = layer.Notes,
          Params = layer.Params == null
            ? null
            : new Dictionary<string, double>(layer.Params),
        });
      }
      return copy;
    }
  }
}
