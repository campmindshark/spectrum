using System.Collections.Generic;

namespace Spectrum.Base {

  // A named snapshot of the dome look: the whole compositing stack (every
  // layer's visualizer, operation, opacity, mute, and parameter bags) plus the
  // two cross-layer global speeds. Recalling a scene reproduces a curated
  // combination of dome visualizers (e.g. the Quaternion Paintbrush look as its
  // constituent twinkle/ripple/stamp/metaball layers).
  //
  // An XML-serializable POCO persisted inside config.domeScenes. Layers reuses
  // DomeLayerSettings directly, so XSerializer already handles it — the same
  // shape it serializes inside config.domeLayerStack. Deliberately narrow: only
  // the state that defines a "look" is captured (see docs/scenes.md).
  //
  // Instances are treated as immutable once stored: SceneService always deep
  // copies on both save (so a scene never aliases the live stack) and apply (so
  // a later stack edit never mutates the stored scene). InstanceId is copied,
  // not regenerated: recalling a scene resumes runtime state for an existing
  // matching instance; a different ID or renderer kind creates fresh state.
  public class DomeScene {
    public string? Name { get; set; }

    // Deep copy of a domeLayerStack, index 0 = background (bottom), last = front.
    public List<DomeLayerSettings>? Layers { get; set; }

    // The two cross-layer globals captured in the snapshot (see
    // Configuration.domeGlobalFadeSpeed / domeGlobalHueSpeed).
    public double GlobalFadeSpeed { get; set; }
    public double GlobalHueSpeed { get; set; }

  }

}
