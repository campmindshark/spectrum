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
   * config.domeLayerStack through the application-state dispatcher, exactly like a native GUI
   * write. The full stack is broadcast on the SSE feed (frame kind "layers") so
   * every client and the native UI converge.
   *
   * The layers array is in stack order: index 0 is the background (bottom),
   * the last entry is the front. blendMode is carried as its DomeBlend.Id.
   */
  public sealed class LayersController {

    // One layer as sent to / received from the client. Renderer and operation
    // parameters have distinct namespaces so equal keys can never collide.
    public sealed class LayerDto {
      public string instanceId { get; set; }
      public string visualizerKey { get; set; }
      public string blendMode { get; set; }
      public double opacity { get; set; }
      public bool enabled { get; set; }
      public string notes { get; set; }
      public Dictionary<string, double> rendererParams { get; set; }
      public Dictionary<string, double> operationParams { get; set; }
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
      public ActionDto fireAction { get; set; }
      public ActionDto clearAction { get; set; }
    }

    public sealed class ActionDto {
      public string label { get; set; }
      public string toolTip { get; set; }
    }

    public sealed class OperationOptionDto {
      public string id { get; set; }
      public string label { get; set; }
      public IReadOnlyList<ParamDto> @params { get; set; }
    }

    // The full snapshot GET returns: the current stack plus the fixed pick-lists
    // (available visualizers and compositing operations) the client renders its
    // editors from. Each option carries its stable id, display label, and schema.
    public sealed class LayersState {
      public IReadOnlyList<LayerDto> layers { get; set; }
      public IReadOnlyList<VisualizerOptionDto> visualizers { get; set; }
      public IReadOnlyList<OperationOptionDto> operations { get; set; }
    }

    private readonly ApplicationStateDispatcher gateway;
    private readonly Configuration config;
    private readonly ConfigurationEditor editor;
    public LayersController(
      ApplicationStateDispatcher gateway, Configuration config
    ) {
      this.gateway = gateway;
      this.config = config;
      this.editor = config as ConfigurationEditor ??
        throw new ArgumentException(
          "Layer configuration must support collection edits.",
          nameof(config));
    }

    internal LayersState State() {
      return new LayersState {
        layers = SerializeStack(this.config),
        visualizers = VisualizerOptions(this.config),
        operations = OperationOptions(this.config),
      };
    }

    public Task<LayersState> StateAsync() =>
      this.gateway.InvokeAsync(this.State);

    // The current config stack as client DTOs, in stack order (index 0 =
    // background). Shared with ConfigEventStream so the SSE "layers" frame and the
    // GET response are identical in shape.
    internal static List<LayerDto> SerializeStack(Configuration config) {
      var list = new List<LayerDto>();
      var stack = config.domeLayerStack;
      if (!stack.IsDefaultOrEmpty) {
        foreach (DomeLayerView layer in stack) {
          if (layer == null) {
            continue;
          }
          list.Add(new LayerDto {
            instanceId = layer.InstanceId,
            visualizerKey = layer.VisualizerKey,
            blendMode = layer.BlendMode,
            opacity = layer.Opacity,
            enabled = layer.Enabled,
            notes = layer.Notes,
            // Copy so the wire DTO never aliases the config's live bag.
            rendererParams = layer.RendererParams.IsEmpty
              ? null
              : new Dictionary<string, double>(layer.RendererParams),
            operationParams = layer.OperationParams.IsEmpty
              ? null
              : new Dictionary<string, double>(layer.OperationParams),
          });
        }
      }
      return list;
    }

    // Immutable show-state projection used by SSE. Unlike the serializer DTO
    // overload above, every field here comes from the same published
    // generation, so request-thread snapshots cannot mix a newly committed
    // stack with prior globals or palettes.
    public static List<LayerDto> SerializeStack(LayerStackSnapshot snapshot) {
      var list = new List<LayerDto>();
      if (snapshot?.Layers.IsDefaultOrEmpty != false) {
        return list;
      }
      foreach (LayerSnapshot layer in snapshot.Layers) {
        list.Add(new LayerDto {
          instanceId = layer.Id.Value,
          visualizerKey = layer.RendererId,
          blendMode = layer.OperationId,
          opacity = layer.Opacity,
          enabled = layer.Enabled,
          notes = layer.Notes,
          rendererParams = SnapshotParameters(layer.RendererParameters),
          operationParams = SnapshotParameters(layer.OperationParameters),
        });
      }
      return list;
    }

    private static Dictionary<string, double> SnapshotParameters(
      IReadOnlyDictionary<string, ParameterValue> parameters
    ) {
      var values = new Dictionary<string, double>();
      if (parameters == null) {
        return values;
      }
      foreach (KeyValuePair<string, ParameterValue> pair in parameters) {
        values[pair.Key] = pair.Value.Value;
      }
      return values;
    }

    private static List<VisualizerOptionDto> VisualizerOptions(
      Configuration config
    ) {
      var options = new List<VisualizerOptionDto>();
      foreach (LayerDefinition definition in
          BuiltInDomeLayerCatalog.Metadata.Definitions) {
        options.Add(new VisualizerOptionDto {
          key = definition.Id,
          label = definition.DisplayName,
          @params = ToDtos(definition.Parameters, config),
          fireAction = ToDto(definition.FireAction),
          clearAction = ToDto(definition.ClearAction),
        });
      }
      return options;
    }

    private static ActionDto ToDto(LayerActionDefinition action) =>
      action == null ? null : new ActionDto {
        label = action.Label,
        toolTip = action.ToolTip,
      };

    private static List<OperationOptionDto> OperationOptions(
      Configuration config
    ) {
      var options = new List<OperationOptionDto>();
      foreach (DomeBlend blend in DomeBlend.All) {
        options.Add(new OperationOptionDto {
          id = blend.Id,
          label = blend.DisplayName,
          @params = ToDtos(blend.Params, config),
        });
      }
      return options;
    }

    private static List<ParamDto> ToDtos(
      IReadOnlyList<DomeLayerParam> schema, Configuration config
    ) {
      return schema.Select(p => new ParamDto {
        key = p.Key,
        label = p.Label,
        type = p.Type.ToString(),
        min = p.Min,
        max = p.Max,
        step = p.Step,
        options = p.Key == PaletteService.LayerParameterKey && config != null
          ? PaletteService.Names(config).ToArray()
          : p.Options,
        @default = DomeLayerDate.ResolveDefault(p),
        compositorConsumed = p.CompositorConsumed,
      }).ToList();
    }

    // Parse the wire DTOs into DomeLayerSettings, hand the stack to the shared
    // StackValidator, then replace config.domeLayerStack via the gateway
    // (snapshot swap on the UI thread). The validator rejects unknown keys,
    // duplicate instance IDs, out-of-range opacity, unknown blend names,
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
          InstanceId = dto.instanceId,
          VisualizerKey = dto.visualizerKey,
          // The wire carries the blend by DomeBlend.Id, the same string the
          // settings persist; the validator rejects names the registry
          // doesn't know.
          BlendMode = dto.blendMode,
          Opacity = dto.opacity,
          Enabled = dto.enabled,
          Notes = dto.notes,
          RendererParams = dto.rendererParams,
          OperationParams = dto.operationParams,
        });
      }
      (List<DomeLayerSettings> newStack, string error) =
        StackValidator.Validate(parsed, BuiltInDomeLayerCatalog.Metadata);
      if (error != null) {
        return (false, error);
      }
      await this.gateway.InvokeAsync(
        () => this.editor.ReplaceDomeLayerStack(newStack));
      return (true, null);
    }

    // Manual fire bumps the layer's monotonic fire counter so
    // a triggerable layer (Wave/Metaball OneShot, Ripple/Stamp) fires once. Keyed
    // by stable instance ID. A whole-dictionary copy-and-swap through the
    // gateway, mirroring the native DomeLayersController.FireRow; firing is not a
    // stack edit, so it doesn't route through ReplaceAsync/the "layers" frame.
    // The counter (not a bool) is race-free across clients: each Fire just
    // increments, none resets a shared flag.
    public async Task<(bool ok, string error)> FireAsync(string instanceId) {
      (bool ok, string error) result = (false, "not run");
      await this.gateway.InvokeAsync(() => {
        (DomeLayerView layer, string error) = ResolveTarget(instanceId);
        if (error != null) {
          result = (false, error);
          return;
        }
        var counters = new Dictionary<string, int>(
          this.config.domeLayerFireCounters);
        counters.TryGetValue(layer.InstanceId, out int count);
        counters[layer.InstanceId] = count + 1;
        this.editor.ReplaceDomeLayerFireCounters(counters);
        result = (true, null);
      });
      return result;
    }

    // Manual clear, exactly parallel to FireAsync but bumping the layer's
    // domeLayerClearCounters entry (mirrors DomeLayersController.ClearRow). A
    // layer that holds accumulated live state (Shooting Star) edge-detects the
    // bump and drops it; layers with no such state ignore it (harmless no-op).
    public async Task<(bool ok, string error)> ClearAsync(string instanceId) {
      (bool ok, string error) result = (false, "not run");
      await this.gateway.InvokeAsync(() => {
        (DomeLayerView layer, string error) = ResolveTarget(instanceId);
        if (error != null) {
          result = (false, error);
          return;
        }
        var counters = new Dictionary<string, int>(
          this.config.domeLayerClearCounters);
        counters.TryGetValue(layer.InstanceId, out int count);
        counters[layer.InstanceId] = count + 1;
        this.editor.ReplaceDomeLayerClearCounters(counters);
        result = (true, null);
      });
      return result;
    }

    private (DomeLayerView layer, string error) ResolveTarget(string id) {
      foreach (DomeLayerView layer in this.config.domeLayerStack) {
        if (layer != null && layer.InstanceId == id) {
          return (layer, null);
        }
      }
      return (null, "unknown layer instance: " + id);
    }
  }
}
