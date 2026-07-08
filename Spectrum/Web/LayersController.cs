using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spectrum.Base;

namespace Spectrum.Web {

  /**
   * The web control for the dome layer stack. Whole-stack last-write-wins (the
   * client always sends its full edited copy), so — unlike the modal dome
   * calibration — it needs no advisory lease: it simply replaces
   * config.domeLayerStack through the ControlGateway, exactly like a native GUI
   * write. The full stack is broadcast on the SSE feed (frame kind "layers") so
   * every client and the native UI converge.
   *
   * The layers array is in stack order: index 0 is the background (bottom),
   * the last entry is the front. blendMode is carried as its enum name.
   */
  public sealed class LayersController {

    // One layer as sent to / received from the client. `params` (C# @params) is
    // the per-layer value bag: param key -> value, matching the visualizer's and
    // blend's schema. Absent/empty means "all defaults".
    public sealed class LayerDto {
      public string visualizerKey { get; set; }
      public string blendMode { get; set; }
      public double opacity { get; set; }
      public bool enabled { get; set; }
      public Dictionary<string, double> @params { get; set; }
    }

    // One param descriptor as sent to the client so it can build editors
    // generically (mirrors Base/DomeLayerParam; type is the enum name).
    public sealed class ParamDto {
      public string key { get; set; }
      public string label { get; set; }
      public string type { get; set; }
      public double min { get; set; }
      public double max { get; set; }
      public double step { get; set; }
      public string[] options { get; set; }
      public double @default { get; set; }
      public bool compositorConsumed { get; set; }
    }

    public sealed class VisualizerOptionDto {
      public string key { get; set; }
      public string label { get; set; }
      // Visualizer-consumed param schema for this visualizer (may be empty).
      public IReadOnlyList<ParamDto> @params { get; set; }
    }

    // The full snapshot GET returns: the current stack plus the fixed pick-lists
    // (available visualizers and blend modes) the client renders its editors
    // from, plus the compositor-consumed param schema per blend-mode name.
    public sealed class LayersState {
      public IReadOnlyList<LayerDto> layers { get; set; }
      public IReadOnlyList<VisualizerOptionDto> visualizers { get; set; }
      public IReadOnlyList<string> blendModes { get; set; }
      public IReadOnlyDictionary<string, IReadOnlyList<ParamDto>> blendParams {
        get; set;
      }
    }

    // Guard against an unbounded stack from a buggy/malicious client. Far above
    // any sane number of layers.
    private const int MaxLayers = 16;

    private readonly ControlGateway gateway;
    private readonly Configuration config;
    private static readonly string[] blendModeNames =
      Enum.GetNames(typeof(DomeBlendMode));

    public LayersController(ControlGateway gateway, Configuration config) {
      this.gateway = gateway;
      this.config = config;
    }

    public LayersState State() {
      return new LayersState {
        layers = SerializeStack(this.config),
        visualizers = VisualizerOptions(),
        blendModes = blendModeNames,
        blendParams = BlendParamSchema(),
      };
    }

    // The current config stack as client DTOs, in stack order (index 0 =
    // background). Shared with ConfigEventStream so the SSE "layers" frame and the
    // GET response are identical in shape.
    public static List<LayerDto> SerializeStack(Configuration config) {
      var list = new List<LayerDto>();
      List<DomeLayerSettings> stack = config.domeLayerStack;
      if (stack != null) {
        foreach (DomeLayerSettings layer in stack) {
          if (layer == null) {
            continue;
          }
          list.Add(new LayerDto {
            visualizerKey = layer.VisualizerKey,
            blendMode = layer.BlendMode.ToString(),
            opacity = layer.Opacity,
            enabled = layer.Enabled,
            // Copy so the wire DTO never aliases the config's live bag.
            @params = layer.Params == null
              ? null
              : new Dictionary<string, double>(layer.Params),
          });
        }
      }
      return list;
    }

    private static List<VisualizerOptionDto> VisualizerOptions() {
      var options = new List<VisualizerOptionDto>();
      for (int i = 0; i < DomeLayerSettings.LayerKeys.Length; i++) {
        string key = DomeLayerSettings.LayerKeys[i];
        options.Add(new VisualizerOptionDto {
          key = key,
          label = DomeLayerSettings.LayerLabels[i],
          @params = ToDtos(DomeLayerSettings.ParamsFor(key)),
        });
      }
      return options;
    }

    // Compositor-consumed param schema keyed by blend-mode name, so the client
    // can look up the extra editors a blend contributes (e.g. Desaturate).
    private static Dictionary<string, IReadOnlyList<ParamDto>> BlendParamSchema() {
      var map = new Dictionary<string, IReadOnlyList<ParamDto>>();
      foreach (DomeBlendMode mode in
        (DomeBlendMode[])Enum.GetValues(typeof(DomeBlendMode))
      ) {
        map[mode.ToString()] =
          ToDtos(DomeLayerSettings.ParamsForBlend(mode));
      }
      return map;
    }

    private static List<ParamDto> ToDtos(IReadOnlyList<DomeLayerParam> schema) {
      return schema.Select(p => new ParamDto {
        key = p.Key,
        label = p.Label,
        type = p.Type.ToString(),
        min = p.Min,
        max = p.Max,
        step = p.Step,
        options = p.Options,
        @default = p.Default,
        compositorConsumed = p.CompositorConsumed,
      }).ToList();
    }

    // Validate the whole incoming stack, then replace config.domeLayerStack via
    // the gateway (snapshot swap on the UI thread). Rejects unknown keys,
    // duplicate visualizers (v1 disallows duplicates — each visualizer is a
    // singleton owning one buffer), out-of-range opacity, unknown blend modes, or
    // an over-long stack, without touching config.
    public async Task<(bool ok, string error)> ReplaceAsync(
      IReadOnlyList<LayerDto> layers
    ) {
      if (layers == null) {
        return (false, "body must be {\"layers\": [...]}");
      }
      if (layers.Count > MaxLayers) {
        return (false, "too many layers (max " + MaxLayers + ")");
      }
      var seen = new HashSet<string>();
      var newStack = new List<DomeLayerSettings>();
      foreach (LayerDto dto in layers) {
        if (dto == null || dto.visualizerKey == null) {
          return (false, "each layer needs a visualizerKey");
        }
        if (!DomeLayerSettings.IsLayerKey(dto.visualizerKey)) {
          return (false, "unknown visualizer key: " + dto.visualizerKey);
        }
        if (!seen.Add(dto.visualizerKey)) {
          return (false, "duplicate visualizer: " + dto.visualizerKey);
        }
        if (!Enum.TryParse(
          dto.blendMode ?? "", out DomeBlendMode mode
        ) || !Enum.IsDefined(typeof(DomeBlendMode), mode)) {
          return (false, "unknown blend mode: " + dto.blendMode);
        }
        double opacity = dto.opacity;
        if (double.IsNaN(opacity) || opacity < 0 || opacity > 1) {
          return (false, "opacity must be between 0 and 1");
        }
        newStack.Add(new DomeLayerSettings {
          VisualizerKey = dto.visualizerKey,
          BlendMode = mode,
          Opacity = opacity,
          Enabled = dto.enabled,
          Params = ValidateParams(dto.visualizerKey, mode, dto.@params),
        });
      }
      await this.gateway.InvokeAsync(() => this.config.domeLayerStack = newStack);
      return (true, null);
    }

    // Sanitize an incoming param bag against the layer's schema (visualizer +
    // blend): drop keys with no descriptor, clamp Double to [Min,Max], coerce
    // Bool to 0/1 and Enum to a valid index. Returns null for an empty result so
    // an absent bag persists as "all defaults". Never rejects — unknown keys are
    // silently dropped rather than 400ing, so a client on a newer/older schema
    // still applies the params it does understand.
    private static Dictionary<string, double> ValidateParams(
      string visualizerKey, DomeBlendMode mode, Dictionary<string, double> raw
    ) {
      if (raw == null || raw.Count == 0) {
        return null;
      }
      Dictionary<string, double> clean = null;
      foreach (DomeLayerParam descriptor in
        DomeLayerSettings.ParamsFor(visualizerKey)
          .Concat(DomeLayerSettings.ParamsForBlend(mode))
      ) {
        if (!raw.TryGetValue(descriptor.Key, out double value)) {
          continue;
        }
        if (clean == null) {
          clean = new Dictionary<string, double>();
        }
        clean[descriptor.Key] = ClampParam(descriptor, value);
      }
      return clean;
    }

    private static double ClampParam(DomeLayerParam p, double v) {
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
