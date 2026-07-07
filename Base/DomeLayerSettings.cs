using System;
using System.Collections.Generic;

namespace Spectrum.Base {

  // How a layer's pixels combine with the composite built from the layers below
  // it. See the blend math in LEDDomeOutputBuffer / the layers design doc.
  public enum DomeBlendMode { Over, Add, Screen, Lighten, Multiply }

  // One layer in the dome's compositing stack: which visualizer produces it,
  // how it blends, its opacity, and whether it's muted. An XML-serializable POCO
  // persisted inside config.domeLayerStack.
  //
  // Instances are treated as immutable once published to the operator thread:
  // UI/web writers always replace the whole domeLayerStack list (snapshot swap)
  // rather than mutating an existing settings object in place.
  public class DomeLayerSettings {
    // Stable string id of the layerable visualizer, e.g. "radial". See
    // DomeLayerVisualizer.LayerKey and LegacyVisKeys below.
    public string VisualizerKey { get; set; }
    public DomeBlendMode BlendMode { get; set; } = DomeBlendMode.Add;
    // 0..1, applied before the blend.
    public double Opacity { get; set; } = 1.0;
    // Mute without removing from the stack.
    public bool Enabled { get; set; } = true;

    // The legacy domeActiveVis int -> layer key mapping, kept for the write-only
    // domeActiveVis alias and for config migration. Index == the old magic int.
    public static readonly string[] LegacyVisKeys = new string[] {
      "volume", "radial", "race", "snakes", "quaternion-test",
      "quaternion-multi-test", "quaternion-paintbrush", "splat", "tv-static",
    };
    // Human-readable labels for the layer visualizer pickers, parallel to
    // LegacyVisKeys (same order). Shared by the native GUI and web UI.
    public static readonly string[] LegacyVisLabels = new string[] {
      "Volume (OG)", "Radial Effects", "Race", "Snakes", "Quaternion Test",
      "Quaternion Multi Test", "Quaternion Paintbrush", "Splat Effect",
      "TV Static",
    };

    public static string KeyForLegacyVis(int vis) {
      return vis >= 0 && vis < LegacyVisKeys.Length ? LegacyVisKeys[vis] : null;
    }

    public static string LabelForKey(string key) {
      int i = Array.IndexOf(LegacyVisKeys, key);
      return i >= 0 ? LegacyVisLabels[i] : key;
    }

    // Allocation-free scan used on the scheduling hot path (visualizer Priority
    // getters): true if the stack has an enabled entry naming `key`. Mirrors the
    // style of Operator.AllInputsEnabled. Safe to call on the operator thread
    // against a published (immutable) stack snapshot.
    public static bool StackActivates(IList<DomeLayerSettings> stack, string key) {
      if (stack == null) {
        return false;
      }
      for (int i = 0; i < stack.Count; i++) {
        DomeLayerSettings layer = stack[i];
        if (layer != null && layer.Enabled && layer.VisualizerKey == key) {
          return true;
        }
      }
      return false;
    }
  }

}
