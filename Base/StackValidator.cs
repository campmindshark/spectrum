using System;
using System.Collections.Generic;

namespace Spectrum.Base {

  /**
   * The single source of truth for validating and normalizing a dome layer
   * stack before it is published to config.domeLayerStack. Both writers route
   * through it so they can't diverge:
   *
   *   - the web PUT /api/layers (LayersController), after parsing the wire DTOs
   *     into DomeLayerSettings, and
   *   - scene apply (SceneService), against the deep-copied stored stack.
   *
   * Validate rejects an unknown/duplicate visualizer key, an undefined blend
   * mode, an out-of-range opacity, or an over-long stack (returning an error
   * string without touching config). Per-layer params are sanitized rather than
   * rejected — unknown keys are silently dropped and values clamped to their
   * descriptor range — so a stack authored against a slightly newer/older schema
   * still applies the params it does understand.
   *
   * Validate always returns a *fresh* list of *fresh* DomeLayerSettings with a
   * fresh Params dictionary per layer, so the caller can hand the result straight
   * to a snapshot swap without aliasing its input (the live stack on the web
   * path, the stored scene on the apply path).
   */
  public static class StackValidator {

    // Guard against an unbounded stack from a buggy/malicious client or a
    // corrupted config file. Far above any sane number of layers.
    public const int MaxLayers = 16;

    // Guard rail on the per-layer notes field, matching SceneService.MaxNameLength.
    public const int MaxNotesLength = 256;

    // Validate and deep-copy `layers` into a publishable stack. Returns
    // (stack, null) on success or (null, error) on the first rule violation.
    public static (List<DomeLayerSettings> stack, string error) Validate(
      IReadOnlyList<DomeLayerSettings> layers
    ) {
      if (layers == null) {
        return (null, "layers must not be null");
      }
      if (layers.Count > MaxLayers) {
        return (null, "too many layers (max " + MaxLayers + ")");
      }
      var seen = new HashSet<string>();
      var newStack = new List<DomeLayerSettings>(layers.Count);
      foreach (DomeLayerSettings layer in layers) {
        if (layer == null || layer.VisualizerKey == null) {
          return (null, "each layer needs a visualizerKey");
        }
        if (!DomeLayerSettings.IsLayerKey(layer.VisualizerKey)) {
          return (null, "unknown visualizer key: " + layer.VisualizerKey);
        }
        if (!seen.Add(layer.VisualizerKey)) {
          return (null, "duplicate visualizer: " + layer.VisualizerKey);
        }
        if (!Enum.IsDefined(typeof(DomeBlendMode), layer.BlendMode)) {
          return (null, "unknown blend mode: " + layer.BlendMode);
        }
        double opacity = layer.Opacity;
        if (double.IsNaN(opacity) || opacity < 0 || opacity > 1) {
          return (null, "opacity must be between 0 and 1");
        }
        string notes = layer.Notes;
        if (notes != null && notes.Length > MaxNotesLength) {
          notes = notes.Substring(0, MaxNotesLength);
        }
        newStack.Add(new DomeLayerSettings {
          VisualizerKey = layer.VisualizerKey,
          BlendMode = layer.BlendMode,
          Opacity = opacity,
          Enabled = layer.Enabled,
          Notes = notes,
          Params = SanitizeParams(layer.VisualizerKey, layer.BlendMode, layer.Params),
        });
      }
      return (newStack, null);
    }

    // Sanitize a param bag against the layer's schema (visualizer + blend): drop
    // keys with no descriptor, clamp Double to [Min,Max], coerce Bool to 0/1 and
    // Enum to a valid index. Returns null for an empty result so an absent bag
    // persists as "all defaults". Never rejects — unknown keys are silently
    // dropped so a client on a newer/older schema still applies what it
    // understands. Always allocates a fresh dictionary, never aliasing `raw`.
    public static Dictionary<string, double> SanitizeParams(
      string visualizerKey, DomeBlendMode mode, IReadOnlyDictionary<string, double> raw
    ) {
      if (raw == null || raw.Count == 0) {
        return null;
      }
      Dictionary<string, double> clean = null;
      foreach (DomeLayerParam descriptor in
        DomeLayerSettings.ParamsFor(visualizerKey)) {
        Accumulate(descriptor, raw, ref clean);
      }
      foreach (DomeLayerParam descriptor in
        DomeLayerSettings.ParamsForBlend(mode)) {
        Accumulate(descriptor, raw, ref clean);
      }
      return clean;
    }

    private static void Accumulate(
      DomeLayerParam descriptor, IReadOnlyDictionary<string, double> raw,
      ref Dictionary<string, double> clean
    ) {
      if (!raw.TryGetValue(descriptor.Key, out double value)) {
        return;
      }
      if (clean == null) {
        clean = new Dictionary<string, double>();
      }
      clean[descriptor.Key] = ClampParam(descriptor, value);
    }

    public static double ClampParam(DomeLayerParam p, double v) {
      switch (p.Type) {
        case DomeLayerParamType.Bool:
          return v != 0 ? 1 : 0;
        case DomeLayerParamType.Enum:
          int count = p.Options != null ? p.Options.Length : 0;
          int idx = (int)Math.Round(v);
          if (idx < 0) {
            idx = 0;
          }
          if (count > 0 && idx >= count) {
            idx = count - 1;
          }
          return idx;
        case DomeLayerParamType.Color:
          int packed = (int)Math.Round(v);
          if (packed < 0) {
            packed = 0;
          }
          if (packed > 0xFFFFFF) {
            packed = 0xFFFFFF;
          }
          return packed;
        default: // Double
          if (double.IsNaN(v)) {
            return p.Default;
          }
          if (v < p.Min) {
            return p.Min;
          }
          if (v > p.Max) {
            return p.Max;
          }
          return v;
      }
    }
  }
}
