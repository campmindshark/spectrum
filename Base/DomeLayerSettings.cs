using System;
using System.Collections.Generic;
using System.Globalization;

namespace Spectrum.Base {


  // The value type of a per-layer parameter. Values live in the bag as double
  // regardless: Bool is 0/1, Enum is the index into DomeLayerParam.Options,
  // Color is a packed 0xRRGGBB int and Date is yyyyMMdd, both represented as
  // doubles so one parameter bag can store every supported type.
  public enum DomeLayerParamType { Double, Bool, Enum, Color, Date }

  // Static schema for one tunable on a layer (or on a blend mode). The bag on a
  // DomeLayerSettings stores only values keyed by DomeLayerParam.Key; everything
  // else here (range, label, default, which consumer reads it) is compile-time
  // metadata read identically by both UIs and the snapshot compiler.
  // See LayerCatalog for visualizer schemas and DomeBlend.Params for the
  // per-blend schemas.
  public sealed class DomeLayerParam {
    public string Key { get; set; } = string.Empty; // unique within the visualizer
    public string Label { get; set; } = string.Empty; // shown in both UIs
    public DomeLayerParamType Type { get; set; }
    public double Min { get; set; }              // Double sliders
    public double Max { get; set; }
    public double Step { get; set; }
    public string[]? Options { get; set; }       // Enum labels (index == value)
    public double Default { get; set; }
    // Date params may use Default = 0 to mean today's date in this zone. The
    // dynamic value is resolved before it reaches either UI or the renderer.
    public string? TimeZoneId { get; set; }
    // true => read by the compositor (CompositeBlend) once per frame, never by
    // the visualizer. false => read by the visualizer in Visualize().
    public bool CompositorConsumed { get; set; }
  }

  public static class DomeLayerDate {
    public const string PacificTimeZoneId = "Pacific Standard Time";
    private const string PacificIanaTimeZoneId = "America/Los_Angeles";

    public static double ResolveDefault(
      DomeLayerParam descriptor, DateTime? utcNow = null
    ) {
      if (descriptor.Type != DomeLayerParamType.Date ||
          descriptor.Default != 0) {
        return descriptor.Default;
      }
      return CurrentDate(
        utcNow ?? DateTime.UtcNow, descriptor.TimeZoneId);
    }

    public static int CurrentDate(DateTime utc, string? timeZoneId) {
      DateTime normalizedUtc = utc.Kind == DateTimeKind.Utc
        ? utc : utc.ToUniversalTime();
      DateTime local = TimeZoneInfo.ConvertTimeFromUtc(
        normalizedUtc, FindTimeZone(timeZoneId));
      return Encode(local);
    }

    public static int Encode(DateTime date) =>
      date.Year * 10000 + date.Month * 100 + date.Day;

    public static bool TryDecode(double value, out DateTime date) {
      date = default;
      if (double.IsNaN(value) || double.IsInfinity(value)) {
        return false;
      }
      double rounded = Math.Round(value);
      if (Math.Abs(value - rounded) > 1e-9 ||
          rounded < 10101 || rounded > 99991231) {
        return false;
      }
      int encoded = (int)rounded;
      int year = encoded / 10000;
      int month = encoded / 100 % 100;
      int day = encoded % 100;
      try {
        date = new DateTime(year, month, day, 0, 0, 0,
          DateTimeKind.Unspecified);
        return Encode(date) == encoded;
      } catch (ArgumentOutOfRangeException) {
        return false;
      }
    }

    public static bool TryParse(string? text, out double value) {
      value = 0;
      if (!DateTime.TryParseExact(
          text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
          DateTimeStyles.None, out DateTime date)) {
        return false;
      }
      value = Encode(date);
      return true;
    }

    public static string Format(double value) =>
      TryDecode(value, out DateTime date)
        ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : string.Empty;

    public static DateTime MidnightUtc(double value, string? timeZoneId) {
      if (!TryDecode(value, out DateTime date)) {
        throw new ArgumentOutOfRangeException(
          nameof(value), "Date values must use yyyyMMdd encoding.");
      }
      return TimeZoneInfo.ConvertTimeToUtc(
        DateTime.SpecifyKind(date, DateTimeKind.Unspecified),
        FindTimeZone(timeZoneId));
    }

    private static TimeZoneInfo FindTimeZone(string? timeZoneId) {
      string requested = string.IsNullOrWhiteSpace(timeZoneId)
        ? TimeZoneInfo.Utc.Id : timeZoneId;
      try {
        return TimeZoneInfo.FindSystemTimeZoneById(requested);
      } catch (TimeZoneNotFoundException) {
        string? fallback = requested == PacificTimeZoneId
          ? PacificIanaTimeZoneId
          : requested == PacificIanaTimeZoneId
            ? PacificTimeZoneId
            : null;
        if (fallback == null) {
          throw;
        }
        return TimeZoneInfo.FindSystemTimeZoneById(fallback);
      }
    }
  }

  // One layer in the dome's compositing stack: which visualizer produces it,
  // how it blends, its opacity, and whether it's muted. An XML-serializable POCO
  // persisted inside config.domeLayerStack.
  //
  // Instances are treated as immutable once published to the operator thread:
  // UI/web writers always replace the whole domeLayerStack list (snapshot swap)
  // rather than mutating an existing settings object in place.
  public class DomeLayerSettings {
    // Stable identity of this configured occurrence. LayerStackService assigns
    // one when a caller omits it, and every writer then persists it. Renderer
    // IDs identify kinds; instance IDs identify layers.
    public string? InstanceId { get; set; }
    // Stable string id of the layerable visualizer, e.g. "radial".
    public string? VisualizerKey { get; set; }
    // The DomeBlend.Id of how this layer combines with the composite below
    // it. A string (not the blend object) because it is the persisted stable ID.
    // Resolve with DomeBlend.FromId; consumers cache the result.
    public string BlendMode { get; set; } = DomeBlend.Default.Id;
    // 0..1, applied before the blend.
    public double Opacity { get; set; } = 1.0;
    // Mute without removing from the stack.
    public bool Enabled { get; set; } = true;

    // Free-text note the user leaves for themselves (e.g. "use this to make
    // things monochrome"). Null by default; never populated by defaults or
    // schema logic, purely user-authored. Carried through scenes like every
    // other field on this POCO (DomeScene.Layers reuses DomeLayerSettings).
    public string? Notes { get; set; }

    // Per-renderer and per-operation parameter overrides. Separate bags make
    // ownership explicit and allow a renderer and operation to use the same key
    // without colliding. Missing keys use the descriptor defaults.
    //
    // Null by default on purpose: XSerializer deserializes dictionary members by
    // Add-ing into the existing instance, so a non-null initializer would
    // double-up the persisted entries on load — the same null-by-default rule
    // domeLayerStack and domeCableMapping already follow.
    public Dictionary<string, double>? RendererParams { get; set; }
    public Dictionary<string, double>? OperationParams { get; set; }

    public static DomeLayerSettings? ForInstance(
      IList<DomeLayerSettings>? stack, string? instanceId
    ) {
      if (stack == null || instanceId == null) {
        return null;
      }
      for (int i = 0; i < stack.Count; i++) {
        DomeLayerSettings layer = stack[i];
        if (layer != null && layer.InstanceId == instanceId) {
          return layer;
        }
      }
      return null;
    }

  }

}
