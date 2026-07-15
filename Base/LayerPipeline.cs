using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace Spectrum.Base {

  public readonly record struct LayerInstanceId(string Value) {
    public static LayerInstanceId NewId() =>
      new LayerInstanceId(Guid.NewGuid().ToString("N"));

    public override string ToString() => this.Value ?? string.Empty;
  }

  public readonly record struct ParameterValue(
    DomeLayerParamType Type, double Value
  ) {
    public bool AsBoolean => this.Value != 0;
    public int AsInteger => (int)Math.Round(this.Value);
  }

  public sealed record LayerSnapshot(
    LayerInstanceId Id,
    string RendererId,
    string OperationId,
    double Opacity,
    bool Enabled,
    ImmutableDictionary<string, ParameterValue> RendererParameters,
    ImmutableDictionary<string, ParameterValue> OperationParameters,
    string Notes
  );

  public sealed record LayerStackSnapshot(
    ImmutableArray<LayerSnapshot> Layers
  ) {
    public static LayerStackSnapshot Empty { get; } =
      new LayerStackSnapshot(ImmutableArray<LayerSnapshot>.Empty);
  }

  // Implemented by serializer-facing configuration objects that compile a
  // DTO stack at the same moment they publish its PropertyChanged event. The
  // operator can then cross the configuration boundary through this immutable
  // value instead of reading serializer DTOs on its render thread.
  public interface ILayerStackSnapshotSource {
    LayerStackSnapshot DomeLayerStackSnapshot { get; }
  }

  // Per-instance runtime view of the renderer options compiled into the
  // immutable layer snapshot. The Operator publishes a replacement snapshot
  // before scheduling a changed stack; visualizers therefore read validated
  // values directly without walking Configuration.domeLayerStack each frame.
  public sealed class LayerRendererRuntime {
    private sealed record RuntimeState(
      LayerSnapshot Snapshot, ILayerRendererOptions Options
    );

    private readonly Func<
      ImmutableDictionary<string, ParameterValue>,
      ILayerRendererOptions> compileOptions;
    private RuntimeState state;

    public LayerRendererRuntime(
      LayerSnapshot snapshot,
      Func<ImmutableDictionary<string, ParameterValue>,
        ILayerRendererOptions> compileOptions
    ) {
      if (snapshot == null) {
        throw new ArgumentNullException(nameof(snapshot));
      }
      this.compileOptions = compileOptions ??
        throw new ArgumentNullException(nameof(compileOptions));
      this.state = this.Compile(snapshot);
    }

    public LayerInstanceId InstanceId => this.Snapshot.Id;
    public string RendererId => this.Snapshot.RendererId;
    private RuntimeState State => Volatile.Read(ref this.state);
    public LayerSnapshot Snapshot => this.State.Snapshot;
    public ILayerRendererOptions Options => this.State.Options;

    public T GetOptions<T>() where T : class, ILayerRendererOptions {
      ILayerRendererOptions current = this.Options;
      if (current is T typed) {
        return typed;
      }
      throw new InvalidOperationException(
        "Renderer " + this.RendererId + " expected options " +
        typeof(T).Name + " but received " +
        (current?.GetType().Name ?? "null") + ".");
    }

    public void Publish(LayerSnapshot next) {
      if (next == null) {
        throw new ArgumentNullException(nameof(next));
      }
      LayerSnapshot current = this.Snapshot;
      if (current.Id != next.Id || current.RendererId != next.RendererId) {
        throw new InvalidOperationException(
          "A layer runtime cannot change instance or renderer identity.");
      }
      Volatile.Write(ref this.state, this.Compile(next));
    }

    private RuntimeState Compile(LayerSnapshot snapshot) {
      ILayerRendererOptions options = this.compileOptions(
        snapshot.RendererParameters);
      if (options == null) {
        throw new InvalidOperationException(
          "Renderer " + snapshot.RendererId +
          " compiled null runtime options.");
      }
      return new RuntimeState(snapshot, options);
    }
  }

  // Runtime renderers deliberately expose no persisted DTO or Configuration.
  // The compiler binds a renderer to a layer snapshot once, and the compositor
  // consumes only this narrow frame contract.
  public interface ILayerRenderer {
    string RendererId { get; }
    DomeFrame Frame { get; }
    bool IsAvailable { get; }
    IReadOnlyList<Input> RequiredInputs { get; }
  }

  public sealed record LayerDefinition(
    string Id,
    string DisplayName,
    Func<LayerRendererRuntime, ILayerRenderer> CreateRenderer,
    IReadOnlyList<DomeLayerParam> Parameters,
    Func<ImmutableDictionary<string, ParameterValue>,
      ILayerRendererOptions> CompileOptions
  );

  public sealed class LayerCatalog {
    private readonly ImmutableArray<LayerDefinition> definitions;
    private readonly ImmutableDictionary<string, LayerDefinition> byId;

    public IReadOnlyList<LayerDefinition> Definitions => this.definitions;

    public LayerCatalog(IEnumerable<LayerDefinition> definitions) {
      if (definitions == null) {
        throw new ArgumentNullException(nameof(definitions));
      }
      var list = definitions.ToImmutableArray();
      var map = ImmutableDictionary.CreateBuilder<string, LayerDefinition>(
        StringComparer.Ordinal);
      foreach (LayerDefinition definition in list) {
        if (definition == null || string.IsNullOrWhiteSpace(definition.Id)) {
          throw new InvalidOperationException("Every layer needs a stable ID.");
        }
        if (!map.TryAdd(definition.Id, definition)) {
          throw new InvalidOperationException(
            "Duplicate layer ID: " + definition.Id);
        }
        if (definition.Parameters == null) {
          throw new InvalidOperationException(
            "Layer " + definition.Id + " needs a parameter schema.");
        }
        if (definition.CompileOptions == null) {
          throw new InvalidOperationException(
            "Layer " + definition.Id + " needs an options compiler.");
        }
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (DomeLayerParam parameter in definition.Parameters) {
          if (parameter == null || string.IsNullOrWhiteSpace(parameter.Key)) {
            throw new InvalidOperationException(
              "Layer " + definition.Id + " has an invalid parameter key.");
          }
          if (!keys.Add(parameter.Key)) {
            throw new InvalidOperationException(
              "Layer " + definition.Id + " has duplicate parameter " +
              parameter.Key + ".");
          }
        }
      }
      this.definitions = list;
      this.byId = map.ToImmutable();
    }

    public bool TryGet(string id, out LayerDefinition definition) {
      if (id == null) {
        definition = null;
        return false;
      }
      return this.byId.TryGetValue(id, out definition);
    }

    public LayerDefinition Get(string id) =>
      this.TryGet(id, out LayerDefinition definition) ? definition : null;

    public IReadOnlyList<DomeLayerParam> ParametersFor(string id) =>
      this.TryGet(id, out LayerDefinition definition)
        ? definition.Parameters
        : Array.Empty<DomeLayerParam>();

    public LayerCatalog BindFactories(
      IReadOnlyDictionary<string, Func<LayerRendererRuntime, ILayerRenderer>> factories
    ) {
      if (factories == null) {
        throw new ArgumentNullException(nameof(factories));
      }
      foreach (string id in factories.Keys) {
        if (!this.byId.ContainsKey(id)) {
          throw new InvalidOperationException(
            "Renderer factory has no layer definition: " + id);
        }
      }
      return new LayerCatalog(this.definitions.Select(definition => {
        if (!factories.TryGetValue(
          definition.Id,
          out Func<LayerRendererRuntime, ILayerRenderer> factory
        )) {
          throw new InvalidOperationException(
            "No renderer factory registered for layer " + definition.Id);
        }
        return definition with { CreateRenderer = factory };
      }));
    }

    private static LayerDefinition BuiltIn(
      string id, string label, IReadOnlyList<DomeLayerParam> parameters,
      Func<ImmutableDictionary<string, ParameterValue>,
        ILayerRendererOptions> compileOptions
    ) => new LayerDefinition(
      id, label, null, parameters, compileOptions);

    // Stable ordering is part of both picker contracts.
    public static LayerCatalog Default { get; } = new LayerCatalog(new[] {
      BuiltIn(
        "volume", "Volume (OG)", LayerParameterSchemas.VolumeParams,
        LayerRendererOptionsCompiler.Volume),
      BuiltIn(
        "radial", "Radial Effects", LayerParameterSchemas.RadialParams,
        LayerRendererOptionsCompiler.Radial),
      BuiltIn(
        "race", "Race", LayerParameterSchemas.RaceParams,
        LayerRendererOptionsCompiler.Race),
      BuiltIn(
        "snakes", "Snakes", LayerParameterSchemas.SnakesParams,
        LayerRendererOptionsCompiler.Palette),
      BuiltIn(
        "splat", "Splat Effect", LayerParameterSchemas.SplatParams,
        LayerRendererOptionsCompiler.Palette),
      BuiltIn(
        "quaternion-test", "Quaternion Test",
        LayerParameterSchemas.NoParams, LayerRendererOptionsCompiler.Empty),
      BuiltIn(
        "quaternion-paintbrush", "Quaternion Paintbrush",
        LayerParameterSchemas.PaintbrushParams,
        LayerRendererOptionsCompiler.Paintbrush),
      BuiltIn(
        "tv-static", "TV Static", LayerParameterSchemas.NoParams,
        LayerRendererOptionsCompiler.Empty),
      BuiltIn(
        "twinkle", "Twinkle", LayerParameterSchemas.TwinkleParams,
        LayerRendererOptionsCompiler.Twinkle),
      BuiltIn(
        "flash", "Flash", LayerParameterSchemas.FlashParams,
        LayerRendererOptionsCompiler.Flash),
      BuiltIn(
        "background", "Background", LayerParameterSchemas.BackgroundParams,
        LayerRendererOptionsCompiler.Background),
      BuiltIn(
        "wave", "Wave", LayerParameterSchemas.WaveParams,
        LayerRendererOptionsCompiler.Wave),
      BuiltIn(
        "ripple", "Ripple", LayerParameterSchemas.RippleParams,
        LayerRendererOptionsCompiler.Ripple),
      BuiltIn(
        "stamp", "Stamp", LayerParameterSchemas.StampParams,
        LayerRendererOptionsCompiler.Stamp),
      BuiltIn(
        "metaball", "Metaball", LayerParameterSchemas.MetaballParams,
        LayerRendererOptionsCompiler.Metaball),
      BuiltIn(
        "point-cloud", "Point Cloud", LayerParameterSchemas.PointCloudParams,
        LayerRendererOptionsCompiler.PointCloud),
      BuiltIn(
        "gyroscope", "Gyroscope", LayerParameterSchemas.GyroscopeParams,
        LayerRendererOptionsCompiler.Gyroscope),
      BuiltIn(
        "shooting-star", "Shooting Star",
        LayerParameterSchemas.ShootingStarParams,
        LayerRendererOptionsCompiler.ShootingStar),
      BuiltIn(
        "sparkler", "Sparkler", LayerParameterSchemas.SparklerParams,
        LayerRendererOptionsCompiler.Sparkler),
      BuiltIn(
        "noise-cloud", "Noise Cloud", LayerParameterSchemas.NoiseCloudParams,
        LayerRendererOptionsCompiler.NoiseCloud),
      BuiltIn(
        "caustics", "Caustics", LayerParameterSchemas.CausticsParams,
        LayerRendererOptionsCompiler.Caustics),
      BuiltIn(
        "ripple-tank", "Ripple Tank", LayerParameterSchemas.RippleTankParams,
        LayerRendererOptionsCompiler.RippleTank),
      BuiltIn(
        "vortex", "Vortex", LayerParameterSchemas.VortexParams,
        LayerRendererOptionsCompiler.Vortex),
    });
  }

  public sealed class LayerStackService {
    private readonly LayerCatalog catalog;

    public LayerStackService(LayerCatalog catalog = null) {
      this.catalog = catalog ?? LayerCatalog.Default;
    }

    public (LayerStackSnapshot snapshot, string error) CreateSnapshot(
      IReadOnlyList<DomeLayerSettings> layers
    ) {
      (List<DomeLayerSettings> normalized, string error) =
        this.Normalize(layers);
      if (error != null) {
        return (null, error);
      }
      var snapshots = ImmutableArray.CreateBuilder<LayerSnapshot>(
        normalized.Count);
      foreach (DomeLayerSettings layer in normalized) {
        LayerDefinition definition = this.catalog.Get(layer.VisualizerKey);
        DomeBlend operation = DomeBlend.FromId(layer.BlendMode);
        snapshots.Add(new LayerSnapshot(
          new LayerInstanceId(layer.InstanceId),
          layer.VisualizerKey,
          operation.Id,
          layer.Opacity,
          layer.Enabled,
          CompileParameters(definition.Parameters, layer.RendererParams),
          CompileParameters(operation.Params, layer.OperationParams),
          layer.Notes));
      }
      return (new LayerStackSnapshot(snapshots.MoveToImmutable()), null);
    }

    public (List<DomeLayerSettings> stack, string error) Normalize(
      IReadOnlyList<DomeLayerSettings> layers
    ) {
      if (layers == null) {
        return (null, "layers must not be null");
      }
      if (layers.Count > StackValidator.MaxLayers) {
        return (null, "too many layers (max " + StackValidator.MaxLayers + ")");
      }
      var ids = new HashSet<string>(StringComparer.Ordinal);
      var normalized = new List<DomeLayerSettings>(layers.Count);
      foreach (DomeLayerSettings layer in layers) {
        if (layer == null || string.IsNullOrEmpty(layer.VisualizerKey)) {
          return (null, "each layer needs a visualizerKey");
        }
        if (!this.catalog.TryGet(layer.VisualizerKey, out _)) {
          return (null, "unknown visualizer key: " + layer.VisualizerKey);
        }
        DomeBlend operation = DomeBlend.FromId(layer.BlendMode);
        if (operation == null) {
          return (null, "unknown blend mode: " + layer.BlendMode);
        }
        if (double.IsNaN(layer.Opacity) ||
            layer.Opacity < 0 || layer.Opacity > 1) {
          return (null, "opacity must be between 0 and 1");
        }
        string id = string.IsNullOrWhiteSpace(layer.InstanceId)
          ? LayerInstanceId.NewId().Value : layer.InstanceId;
        if (!ids.Add(id)) {
          return (null, "duplicate layer instance id: " + id);
        }
        string notes = layer.Notes;
        if (notes != null && notes.Length > StackValidator.MaxNotesLength) {
          notes = notes.Substring(0, StackValidator.MaxNotesLength);
        }
        normalized.Add(new DomeLayerSettings {
          InstanceId = id,
          VisualizerKey = layer.VisualizerKey,
          BlendMode = operation.Id,
          Opacity = layer.Opacity,
          Enabled = layer.Enabled,
          Notes = notes,
          RendererParams = StackValidator.SanitizeRendererParams(
            layer.VisualizerKey, layer.RendererParams),
          OperationParams = StackValidator.SanitizeOperationParams(
            operation, layer.OperationParams),
        });
      }
      return (normalized, null);
    }

    private static ImmutableDictionary<string, ParameterValue> CompileParameters(
      IReadOnlyList<DomeLayerParam> schema,
      IReadOnlyDictionary<string, double> values
    ) {
      var result = ImmutableDictionary.CreateBuilder<string, ParameterValue>(
        StringComparer.Ordinal);
      foreach (DomeLayerParam parameter in schema) {
        double raw = parameter.Default;
        if (values != null && values.TryGetValue(parameter.Key, out double v)) {
          raw = v;
        }
        result[parameter.Key] = new ParameterValue(
          parameter.Type, StackValidator.ClampParam(parameter, raw));
      }
      return result.ToImmutable();
    }
  }

  public sealed record CompiledLayer(
    LayerSnapshot Snapshot,
    ILayerRenderer Renderer,
    ImmutableArray<Input> RequiredInputs,
    ICompositeOperation Operation,
    ICompositeOptions OperationOptions
  );

  public sealed record RenderPlan(ImmutableArray<CompiledLayer> Layers) {
    public static RenderPlan Empty { get; } =
      new RenderPlan(ImmutableArray<CompiledLayer>.Empty);
  }

  public sealed record LayerRendererBinding(
    ILayerRenderer Renderer,
    bool Created,
    ILayerRenderer ReplacedRenderer
  );

  // The sole owner of layer-renderer instances and their mutable runtime
  // options. Recompiling a stack resolves through this store, so matching
  // instance IDs retain renderer state while receiving a new immutable option
  // snapshot. The render-plan compiler itself remains stateless.
  public sealed class LayerRendererStore {
    private sealed record Entry(
      string RendererId,
      ILayerRenderer Renderer,
      LayerRendererRuntime Runtime
    );

    private readonly LayerCatalog catalog;
    private readonly Dictionary<LayerInstanceId, Entry> entries = new();

    public LayerRendererStore(LayerCatalog catalog) {
      this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public LayerRendererBinding Resolve(LayerSnapshot layer) {
      if (layer == null) {
        throw new ArgumentNullException(nameof(layer));
      }
      if (this.entries.TryGetValue(layer.Id, out Entry existing) &&
          existing.RendererId == layer.RendererId) {
        existing.Runtime.Publish(layer);
        return new LayerRendererBinding(existing.Renderer, false, null);
      }

      LayerDefinition definition = this.catalog.Get(layer.RendererId) ??
        throw new InvalidOperationException(
          "Unknown layer renderer: " + layer.RendererId);
      if (definition.CreateRenderer == null) {
        throw new InvalidOperationException(
          "No renderer factory registered for layer " + layer.RendererId);
      }

      var runtime = new LayerRendererRuntime(
        layer, definition.CompileOptions);
      ILayerRenderer renderer = definition.CreateRenderer(runtime);
      if (renderer == null) {
        throw new InvalidOperationException(
          "Renderer factory returned null for layer " + layer.RendererId);
      }
      ILayerRenderer replaced = existing?.Renderer;
      this.entries[layer.Id] = new Entry(
        layer.RendererId, renderer, runtime);
      return new LayerRendererBinding(renderer, true, replaced);
    }

    public ILayerRenderer Get(LayerSnapshot layer) {
      if (layer == null) {
        return null;
      }
      return this.entries.TryGetValue(layer.Id, out Entry entry) &&
        entry.RendererId == layer.RendererId
          ? entry.Renderer
          : null;
    }

    public IReadOnlyList<ILayerRenderer> Retain(
      IReadOnlySet<LayerInstanceId> retainedIds
    ) {
      if (retainedIds == null) {
        throw new ArgumentNullException(nameof(retainedIds));
      }
      var removed = new List<ILayerRenderer>();
      foreach (LayerInstanceId id in this.entries.Keys.ToArray()) {
        if (retainedIds.Contains(id)) {
          continue;
        }
        removed.Add(this.entries[id].Renderer);
        this.entries.Remove(id);
      }
      return removed;
    }
  }

  public sealed class RenderPlanCompiler {
    public RenderPlan Compile(
      LayerStackSnapshot snapshot,
      Func<LayerSnapshot, ILayerRenderer> rendererResolver
    ) {
      if (snapshot == null || snapshot.Layers.IsDefaultOrEmpty) {
        return RenderPlan.Empty;
      }
      var layers = ImmutableArray.CreateBuilder<CompiledLayer>(
        snapshot.Layers.Length);
      foreach (LayerSnapshot layer in snapshot.Layers) {
        if (!layer.Enabled) {
          continue;
        }
        ILayerRenderer renderer = rendererResolver(layer);
        DomeBlend operation = DomeBlend.FromId(layer.OperationId);
        if (renderer == null || operation == null) {
          continue;
        }
        layers.Add(new CompiledLayer(
          layer, renderer, CompileRequiredInputs(renderer), operation,
          operation.CompileOptions(layer.OperationParameters)));
      }
      // Disabled or unresolved entries are intentionally filtered, so Count
      // can be smaller than the builder's initial capacity.
      return new RenderPlan(layers.ToImmutable());
    }

    private static ImmutableArray<Input> CompileRequiredInputs(
      ILayerRenderer renderer
    ) {
      IReadOnlyList<Input> inputs = renderer.RequiredInputs;
      if (inputs == null || inputs.Count == 0) {
        return ImmutableArray<Input>.Empty;
      }
      var compiled = ImmutableArray.CreateBuilder<Input>(inputs.Count);
      var seen = new HashSet<Input>();
      for (int i = 0; i < inputs.Count; i++) {
        Input input = inputs[i];
        if (input == null) {
          throw new InvalidOperationException(
            "Renderer " + renderer.RendererId +
            " declared a null input requirement.");
        }
        if (seen.Add(input)) {
          compiled.Add(input);
        }
      }
      // Duplicate declarations are collapsed, so Count may be smaller than
      // the builder's initial capacity.
      return compiled.ToImmutable();
    }
  }
}
