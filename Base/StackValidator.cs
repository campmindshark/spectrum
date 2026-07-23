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
   * Validate rejects an unknown visualizer key, duplicate instance ID,
   * undefined blend mode, an out-of-range opacity, or an over-long stack (returning an error
   * string without touching config). Per-layer params are sanitized rather than
   * rejected — unknown keys are silently dropped and values clamped to their
   * descriptor range — so a stack authored against a slightly newer/older schema
   * still applies the params it does understand.
   *
   * Validate always returns a *fresh* list of *fresh* DomeLayerSettings with a
   * fresh parameter dictionaries per layer, so the caller can hand the result straight
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
    public static (List<DomeLayerSettings>? stack, string? error) Validate(
      IReadOnlyList<DomeLayerSettings>? layers,
      LayerCatalog catalog
    ) {
      if (layers == null) {
        return (null, "layers must not be null");
      }
      if (layers.Count > MaxLayers) {
        return (null, "too many layers (max " + MaxLayers + ")");
      }
      return new LayerStackService(catalog).Normalize(layers);
    }

    // Sanitize a param bag against its owning schema: drop
    // keys with no descriptor, clamp Double to [Min,Max], coerce Bool to 0/1 and
    // Enum to a valid index. Returns null for an empty result so an absent bag
    // persists as "all defaults". Never rejects — unknown keys are silently
    // dropped so a client on a newer/older schema still applies what it
    // understands. Always allocates a fresh dictionary, never aliasing `raw`.
    public static Dictionary<string, double>? SanitizeRendererParams(
      LayerCatalog catalog,
      string? visualizerKey,
      IReadOnlyDictionary<string, double>? raw
    ) => Sanitize(
      catalog.ParametersFor(visualizerKey), raw);

    public static Dictionary<string, double>? SanitizeOperationParams(
      DomeBlend operation, IReadOnlyDictionary<string, double>? raw
    ) => Sanitize(operation.Params, raw);

    private static Dictionary<string, double>? Sanitize(
      IReadOnlyList<DomeLayerParam> schema,
      IReadOnlyDictionary<string, double>? raw
    ) {
      if (raw == null || raw.Count == 0) {
        return null;
      }
      Dictionary<string, double>? clean = null;
      foreach (DomeLayerParam descriptor in schema) {
        Accumulate(descriptor, raw, ref clean);
      }
      return clean;
    }

    private static void Accumulate(
      DomeLayerParam descriptor, IReadOnlyDictionary<string, double> raw,
      ref Dictionary<string, double>? clean
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
        case DomeLayerParamType.Date:
          return DomeLayerDate.TryDecode(v, out _)
            ? Math.Round(v)
            : DomeLayerDate.ResolveDefault(p);
        default: // Double
          if (double.IsNaN(v)) {
            return DomeLayerDate.ResolveDefault(p);
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
