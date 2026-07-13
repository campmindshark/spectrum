using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
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
        ILayerRendererOptions> compileOptions = null
    ) {
      if (snapshot == null) {
        throw new ArgumentNullException(nameof(snapshot));
      }
      this.compileOptions = compileOptions ??
        (values => new LayerRendererParameterOptions(values));
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

    // Compatibility for custom catalogs that have not registered a typed
    // options compiler. Built-in visualizers use GetOptions<T>() exclusively.
    public double Parameter(string key) {
      if (key == null) {
        throw new ArgumentNullException(nameof(key));
      }
      if (this.Snapshot.RendererParameters.TryGetValue(
          key, out ParameterValue value)) {
        return value.Value;
      }
      throw new InvalidOperationException(
        "Renderer " + this.RendererId + " has no parameter " + key + ".");
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

  public sealed class LayerRenderContext {
    private readonly IReadOnlyDictionary<Type, object> services;

    public LayerInstanceId InstanceId { get; }
    public LayerSnapshot Snapshot { get; }
    public LayerRendererRuntime Runtime { get; }

    public LayerRenderContext(
      LayerInstanceId instanceId, LayerSnapshot snapshot,
      IReadOnlyDictionary<Type, object> services = null,
      LayerRendererRuntime runtime = null
    ) {
      this.InstanceId = instanceId;
      this.Snapshot = snapshot;
      this.Runtime = runtime ?? new LayerRendererRuntime(snapshot);
      this.services = services ?? new Dictionary<Type, object>();
    }

    public T Get<T>() where T : class =>
      this.services.TryGetValue(typeof(T), out object value) ? (T)value : null;
  }

  public sealed record LayerDefinition(
    string Id,
    string DisplayName,
    Func<LayerRenderContext, ILayerRenderer> CreateRenderer,
    IReadOnlyList<DomeLayerParam> Parameters,
    Func<ImmutableDictionary<string, ParameterValue>,
      ILayerRendererOptions> CompileOptions = null
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

    public LayerCatalog WithFactories(
      IReadOnlyDictionary<string, Func<LayerRenderContext, ILayerRenderer>> factories
    ) => new LayerCatalog(this.definitions.Select(d => d with {
      CreateRenderer = factories != null && factories.TryGetValue(
        d.Id, out Func<LayerRenderContext, ILayerRenderer> factory)
          ? factory : d.CreateRenderer,
    }));

    private static LayerDefinition BuiltIn(
      string id, string label, IReadOnlyList<DomeLayerParam> parameters,
      Func<ImmutableDictionary<string, ParameterValue>,
        ILayerRendererOptions> compileOptions
    ) => new LayerDefinition(
      id, label, null, parameters, compileOptions);

    // Stable ordering is part of both picker contracts. The eight legacy IDs
    // stay first so retired domeActiveVis values retain their old meaning.
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
        "quaternion-test", "Quaternion Test",
        LayerParameterSchemas.NoParams, LayerRendererOptionsCompiler.Empty),
      BuiltIn(
        "quaternion-paintbrush", "Quaternion Paintbrush",
        LayerParameterSchemas.PaintbrushParams,
        LayerRendererOptionsCompiler.Paintbrush),
      BuiltIn(
        "splat", "Splat Effect", LayerParameterSchemas.SplatParams,
        LayerRendererOptionsCompiler.Palette),
      BuiltIn(
        "tv-static", "TV Static", LayerParameterSchemas.NoParams,
        LayerRendererOptionsCompiler.Empty),
      BuiltIn(
        "twinkle", "Twinkle", LayerParameterSchemas.TwinkleParams,
        LayerRendererOptionsCompiler.Twinkle),
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
        "background", "Background", LayerParameterSchemas.BackgroundParams,
        LayerRendererOptionsCompiler.Background),
      BuiltIn(
        "flash", "Flash", LayerParameterSchemas.FlashParams,
        LayerRendererOptionsCompiler.Flash),
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
        "noise-cloud", "Noise Cloud", LayerParameterSchemas.NoiseCloudParams,
        LayerRendererOptionsCompiler.NoiseCloud),
      BuiltIn(
        "caustics", "Caustics", LayerParameterSchemas.CausticsParams,
        LayerRendererOptionsCompiler.Caustics),
      BuiltIn(
        "sparkler", "Sparkler", LayerParameterSchemas.SparklerParams,
        LayerRendererOptionsCompiler.Sparkler),
      BuiltIn(
        "vortex", "Vortex", LayerParameterSchemas.VortexParams,
        LayerRendererOptionsCompiler.Vortex),
    });
  }

  public sealed class LayerStackService {
    private sealed class SnapshotHolder {
      public LayerStackSnapshot Snapshot { get; }
      public SnapshotHolder(LayerStackSnapshot snapshot) {
        this.Snapshot = snapshot;
      }
    }

    private static readonly ConditionalWeakTable<
      List<DomeLayerSettings>, SnapshotHolder> snapshotCache = new();
    private readonly LayerCatalog catalog;

    public LayerStackService(LayerCatalog catalog = null) {
      this.catalog = catalog ?? LayerCatalog.Default;
    }

    // All runtime consumers that receive the same copy-on-write DTO list share
    // one immutable compilation boundary. The weak key lets retired UI
    // snapshots be collected normally.
    public static LayerStackSnapshot SnapshotFor(
      IList<DomeLayerSettings> layers
    ) {
      if (layers == null) {
        return LayerStackSnapshot.Empty;
      }
      if (layers is List<DomeLayerSettings> list) {
        return snapshotCache.GetValue(list, source => {
          (LayerStackSnapshot snapshot, string error) =
            new LayerStackService().CreateSnapshot(source);
          return new SnapshotHolder(
            error == null ? snapshot : LayerStackSnapshot.Empty);
        }).Snapshot;
      }
      (LayerStackSnapshot uncached, string uncachedError) =
        new LayerStackService().CreateSnapshot(
          layers as IReadOnlyList<DomeLayerSettings> ?? layers.ToArray());
      return uncachedError == null ? uncached : LayerStackSnapshot.Empty;
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
        DomeBlend operation = DomeBlend.FromName(layer.BlendMode);
        snapshots.Add(new LayerSnapshot(
          new LayerInstanceId(layer.InstanceId),
          layer.VisualizerKey,
          operation.Name,
          layer.Opacity,
          layer.Enabled,
          CompileParameters(definition.Parameters, layer.Params),
          CompileParameters(operation.Params, layer.Params),
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
        DomeBlend operation = DomeBlend.FromName(layer.BlendMode);
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
          BlendMode = operation.Name,
          Opacity = layer.Opacity,
          Enabled = layer.Enabled,
          Notes = notes,
          Params = StackValidator.SanitizeParams(
            layer.VisualizerKey, operation, layer.Params),
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

  public sealed class RenderPlanCompiler {
    private readonly LayerCatalog catalog;
    // Renderer state belongs to (instance ID, renderer ID) and is retained for
    // this compiler's lifetime, even while an instance is absent from a plan.
    // Scene recall deliberately relies on that cache: a saved scene preserves
    // IDs, so recalling it resumes each matching renderer's trails and other
    // live state. Rebinding an ID to another renderer kind creates fresh state
    // and replaces the cached binding.
    private readonly Dictionary<
      LayerInstanceId, (
        string rendererId, ILayerRenderer renderer, LayerRendererRuntime runtime
      )> instances =
        new Dictionary<
          LayerInstanceId, (
            string rendererId, ILayerRenderer renderer,
            LayerRendererRuntime runtime
          )>();

    public RenderPlanCompiler(LayerCatalog catalog = null) {
      this.catalog = catalog ?? LayerCatalog.Default;
    }

    public RenderPlan Compile(
      LayerStackSnapshot snapshot,
      IReadOnlyDictionary<Type, object> services
    ) => this.Compile(snapshot, layer => {
      if (this.instances.TryGetValue(layer.Id, out var existing) &&
          existing.rendererId == layer.RendererId) {
        existing.runtime.Publish(layer);
        return existing.renderer;
      }
      LayerDefinition definition = this.catalog.Get(layer.RendererId);
      if (definition?.CreateRenderer == null) {
        return null;
      }
      var runtime = new LayerRendererRuntime(
        layer, definition.CompileOptions);
      var context = new LayerRenderContext(
        layer.Id, layer, services, runtime);
      ILayerRenderer created = definition.CreateRenderer(context);
      if (created != null) {
        this.instances[layer.Id] = (
          layer.RendererId, created, context.Runtime);
      }
      return created;
    });

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
        DomeBlend operation = DomeBlend.FromName(layer.OperationId);
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
