using System.Collections.Generic;

namespace Spectrum.Base {

  // A named snapshot of the dome look: the whole compositing stack (every
  // layer's visualizer, blend, opacity, mute, and per-layer Params bag) plus the
  // two cross-layer global speeds. Recalling a scene reproduces a curated
  // combination of dome visualizers (e.g. the Quaternion Paintbrush look as its
  // constituent twinkle/ripple/stamp/metaball layers).
  //
  // An XML-serializable POCO persisted inside config.domeScenes. Layers reuses
  // DomeLayerSettings directly, so XSerializer already handles it — the same
  // shape it serializes inside config.domeLayerStack. Deliberately narrow: only
  // the state that defines a "look" is captured (see docs/scenes.md). It can be
  // widened later without a migration — an older file simply lacks the new
  // elements, which deserialize to their defaults.
  //
  // Instances are treated as immutable once stored: SceneService always deep
  // copies on both save (so a scene never aliases the live stack) and apply (so a
  // later stack edit never mutates the stored scene).
  public class DomeScene {
    public string Name { get; set; }

    // Deep copy of a domeLayerStack, index 0 = background (bottom), last = front.
    public List<DomeLayerSettings> Layers { get; set; }

    // The two cross-layer globals captured in the snapshot (see
    // Configuration.domeGlobalFadeSpeed / domeGlobalHueSpeed).
    public double GlobalFadeSpeed { get; set; }
    public double GlobalHueSpeed { get; set; }

    // Deep copy of the live palette (colorPalette slots 0-7) at save time; the
    // same eight-slot shape a DomePalette preset stores. Nullable: a scene saved
    // before scenes captured the palette deserializes to null, and applying such
    // a scene leaves the live palette untouched. Stored as values, not a preset
    // name, so recalling a scene works even if the preset it came from was later
    // edited or deleted.
    public LEDColor[] Palette { get; set; }
  }

}
