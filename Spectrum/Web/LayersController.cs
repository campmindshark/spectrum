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
   * the last entry is the front. blendMode is carried as its DomeBlend.Name.
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
      public string notes { get; set; }
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

    private readonly ControlGateway gateway;
    private readonly Configuration config;
    private static readonly string[] blendModeNames =
      DomeBlend.All.Select(b => b.Name).ToArray();

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
            blendMode = layer.BlendMode,
            opacity = layer.Opacity,
            enabled = layer.Enabled,
            notes = layer.Notes,
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
    // can look up the extra editors a blend contributes (the prism family).
    private static Dictionary<string, IReadOnlyList<ParamDto>> BlendParamSchema() {
      var map = new Dictionary<string, IReadOnlyList<ParamDto>>();
      foreach (DomeBlend blend in DomeBlend.All) {
        map[blend.Name] = ToDtos(blend.Params);
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

    // Parse the wire DTOs into DomeLayerSettings, hand the stack to the shared
    // StackValidator, then replace config.domeLayerStack via the gateway
    // (snapshot swap on the UI thread). The validator rejects unknown keys,
    // duplicate visualizers (v1 disallows duplicates — each visualizer is a
    // singleton owning one buffer), out-of-range opacity, unknown blend names,
    // or an over-long stack, without touching config; scene apply routes
    // through the same validator so the two paths can't diverge.
    public async Task<(bool ok, string error)> ReplaceAsync(
      IReadOnlyList<LayerDto> layers
    ) {
      if (layers == null) {
        return (false, "body must be {\"layers\": [...]}");
      }
      var parsed = new List<DomeLayerSettings>(layers.Count);
      foreach (LayerDto dto in layers) {
        if (dto == null || dto.visualizerKey == null) {
          return (false, "each layer needs a visualizerKey");
        }
        parsed.Add(new DomeLayerSettings {
          VisualizerKey = dto.visualizerKey,
          // The wire carries the blend by DomeBlend.Name, the same string the
          // settings persist; the validator rejects names the registry
          // doesn't know.
          BlendMode = dto.blendMode,
          Opacity = dto.opacity,
          Enabled = dto.enabled,
          Notes = dto.notes,
          Params = dto.@params,
        });
      }
      (List<DomeLayerSettings> newStack, string error) =
        StackValidator.Validate(parsed);
      if (error != null) {
        return (false, error);
      }
      await this.gateway.InvokeAsync(() => this.config.domeLayerStack = newStack);
      return (true, null);
    }

    // Manual fire (docs/triggers.md): bump the layer's monotonic fire counter so
    // a triggerable layer (Wave/Metaball OneShot, Ripple/Stamp) fires once. Keyed
    // by visualizerKey — the stack disallows duplicate visualizers, so the key
    // names exactly one layer. A whole-dictionary copy-and-swap through the
    // gateway, mirroring the native DomeLayersController.FireRow; firing is not a
    // stack edit, so it doesn't route through ReplaceAsync/the "layers" frame.
    // The counter (not a bool) is race-free across clients: each Fire just
    // increments, none resets a shared flag.
    public async Task<(bool ok, string error)> FireAsync(string visualizerKey) {
      if (string.IsNullOrEmpty(visualizerKey)) {
        return (false, "visualizerKey required");
      }
      if (Array.IndexOf(DomeLayerSettings.LayerKeys, visualizerKey) < 0) {
        return (false, "unknown visualizer: " + visualizerKey);
      }
      await this.gateway.InvokeAsync(() => {
        var counters = new Dictionary<string, int>(
          this.config.domeLayerFireCounters ?? new Dictionary<string, int>());
        counters.TryGetValue(visualizerKey, out int count);
        counters[visualizerKey] = count + 1;
        this.config.domeLayerFireCounters = counters;
      });
      return (true, null);
    }

    // Manual clear, exactly parallel to FireAsync but bumping the layer's
    // domeLayerClearCounters entry (mirrors DomeLayersController.ClearRow). A
    // layer that holds accumulated live state (Shooting Star) edge-detects the
    // bump and drops it; layers with no such state ignore it (harmless no-op).
    public async Task<(bool ok, string error)> ClearAsync(string visualizerKey) {
      if (string.IsNullOrEmpty(visualizerKey)) {
        return (false, "visualizerKey required");
      }
      if (Array.IndexOf(DomeLayerSettings.LayerKeys, visualizerKey) < 0) {
        return (false, "unknown visualizer: " + visualizerKey);
      }
      await this.gateway.InvokeAsync(() => {
        var counters = new Dictionary<string, int>(
          this.config.domeLayerClearCounters ?? new Dictionary<string, int>());
        counters.TryGetValue(visualizerKey, out int count);
        counters[visualizerKey] = count + 1;
        this.config.domeLayerClearCounters = counters;
      });
      return (true, null);
    }
  }
}
