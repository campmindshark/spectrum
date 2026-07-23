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
    string? Notes
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
    internal sealed record RuntimeState(
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
      this.PublishPrepared(this.Prepare(next));
    }

    internal RuntimeState Prepare(LayerSnapshot next) {
      if (next == null) {
        throw new ArgumentNullException(nameof(next));
      }
      LayerSnapshot current = this.Snapshot;
      if (current.Id != next.Id || current.RendererId != next.RendererId) {
        throw new InvalidOperationException(
          "A layer runtime cannot change instance or renderer identity.");
      }
      return this.Compile(next);
    }

    internal void PublishPrepared(RuntimeState prepared) {
      if (prepared == null) {
        throw new ArgumentNullException(nameof(prepared));
      }
      if (prepared.Snapshot.Id != this.InstanceId ||
          prepared.Snapshot.RendererId != this.RendererId) {
        throw new InvalidOperationException(
          "Prepared renderer state belongs to a different runtime.");
      }
      Volatile.Write(ref this.state, prepared);
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

  public sealed record LayerActionDefinition(string Label, string ToolTip);

  public sealed record LayerDefinition(
    string Id,
    string DisplayName,
    Func<LayerRendererRuntime, ILayerRenderer> CreateRenderer,
    IReadOnlyList<DomeLayerParam> Parameters,
    Func<ImmutableDictionary<string, ParameterValue>,
      ILayerRendererOptions> CompileOptions,
    LayerActionDefinition? FireAction = null,
    LayerActionDefinition? ClearAction = null
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

    public bool TryGet(string? id, out LayerDefinition? definition) {
      if (id == null) {
        definition = null;
        return false;
      }
      return this.byId.TryGetValue(id, out definition);
    }

    public LayerDefinition? Get(string? id) =>
      this.TryGet(id, out LayerDefinition? definition) ? definition : null;

    public IReadOnlyList<DomeLayerParam> ParametersFor(string? id) {
      return this.TryGet(id, out LayerDefinition? definition) &&
          definition != null
        ? definition.Parameters
        : Array.Empty<DomeLayerParam>();
    }

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
          out Func<LayerRendererRuntime, ILayerRenderer>? factory
        )) {
          throw new InvalidOperationException(
            "No renderer factory registered for layer " + definition.Id);
        }
        return definition with { CreateRenderer = factory };
      }));
    }

  }

  public sealed class LayerStackService {
    private readonly LayerCatalog catalog;

    public LayerStackService(LayerCatalog catalog) {
      this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public (LayerStackSnapshot? snapshot, string? error) CreateSnapshot(
      IReadOnlyList<DomeLayerSettings>? layers
    ) {
      (List<DomeLayerSettings>? normalized, string? error) =
        this.Normalize(layers);
      if (error != null) {
        return (null, error);
      }
      if (normalized == null) {
        return (null, "layer normalization returned no stack");
      }
      var snapshots = ImmutableArray.CreateBuilder<LayerSnapshot>(
        normalized.Count);
      foreach (DomeLayerSettings layer in normalized) {
        string rendererId = layer.VisualizerKey ??
          throw new InvalidOperationException("Normalized layer has no renderer ID.");
        string instanceId = layer.InstanceId ??
          throw new InvalidOperationException("Normalized layer has no instance ID.");
        LayerDefinition definition = this.catalog.Get(rendererId) ??
          throw new InvalidOperationException("Normalized layer renderer is unknown.");
        DomeBlend operation = DomeBlend.FromId(layer.BlendMode) ??
          throw new InvalidOperationException("Normalized layer blend is unknown.");
        snapshots.Add(new LayerSnapshot(
          new LayerInstanceId(instanceId),
          rendererId,
          operation.Id,
          layer.Opacity,
          layer.Enabled,
          CompileParameters(definition.Parameters, layer.RendererParams),
          CompileParameters(operation.Params, layer.OperationParams),
          layer.Notes));
      }
      return (new LayerStackSnapshot(snapshots.MoveToImmutable()), null);
    }

    public (List<DomeLayerSettings>? stack, string? error) Normalize(
      IReadOnlyList<DomeLayerSettings>? layers
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
        DomeBlend? operation = DomeBlend.FromId(layer.BlendMode);
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
        string? notes = layer.Notes;
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
            this.catalog, layer.VisualizerKey, layer.RendererParams),
          OperationParams = StackValidator.SanitizeOperationParams(
            operation, layer.OperationParams),
        });
      }
      return (normalized, null);
    }

    private static ImmutableDictionary<string, ParameterValue> CompileParameters(
      IReadOnlyList<DomeLayerParam> schema,
      IReadOnlyDictionary<string, double>? values
    ) {
      var result = ImmutableDictionary.CreateBuilder<string, ParameterValue>(
        StringComparer.Ordinal);
      foreach (DomeLayerParam parameter in schema) {
        double raw = DomeLayerDate.ResolveDefault(parameter);
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
    ILayerRenderer? ReplacedRenderer
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
      if (this.entries.TryGetValue(layer.Id, out Entry? existing) &&
          existing.RendererId == layer.RendererId) {
        existing.Runtime.Publish(layer);
        return new LayerRendererBinding(existing.Renderer, false, null);
      }

      Entry created = this.CreateEntry(layer);
      ILayerRenderer? replaced = existing?.Renderer;
      this.entries[layer.Id] = created;
      return new LayerRendererBinding(created.Renderer, true, replaced);
    }

    private Entry CreateEntry(LayerSnapshot layer) {
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
      return new Entry(layer.RendererId, renderer, runtime);
    }

    /**
     * Builds one candidate renderer generation without mutating the live store
     * or publishing option updates to retained renderer instances. The owner
     * compiles its complete RenderPlan against Get, then calls Commit only
     * after every renderer and operation has succeeded.
     */
    public Transaction Prepare(LayerStackSnapshot snapshot) {
      if (snapshot == null) {
        throw new ArgumentNullException(nameof(snapshot));
      }
      var transaction = new Transaction(this);
      try {
        foreach (LayerSnapshot layer in snapshot.Layers) {
          transaction.Resolve(layer);
        }
        return transaction;
      } catch {
        transaction.Dispose();
        throw;
      }
    }

    public sealed class Transaction : IDisposable {
      private readonly LayerRendererStore owner;
      private readonly Dictionary<LayerInstanceId, Entry> resolved = new();
      private readonly List<LayerRendererBinding> bindings = new();
      private readonly List<(
        LayerRendererRuntime Runtime,
        LayerRendererRuntime.RuntimeState State)> updates = new();
      private readonly List<ILayerRenderer> created = new();
      private bool committed;
      private bool disposed;

      internal Transaction(LayerRendererStore owner) {
        this.owner = owner;
      }

      public IReadOnlyList<LayerRendererBinding> Bindings => this.bindings;

      internal void Resolve(LayerSnapshot layer) {
        if (this.owner.entries.TryGetValue(
            layer.Id, out Entry? existing) &&
            existing.RendererId == layer.RendererId) {
          LayerRendererRuntime.RuntimeState prepared =
            existing.Runtime.Prepare(layer);
          this.updates.Add((existing.Runtime, prepared));
          this.resolved.Add(layer.Id, existing);
          this.bindings.Add(new LayerRendererBinding(
            existing.Renderer, false, null));
          return;
        }

        Entry candidate = this.owner.CreateEntry(layer);
        this.resolved.Add(layer.Id, candidate);
        this.created.Add(candidate.Renderer);
        this.bindings.Add(new LayerRendererBinding(
          candidate.Renderer, true, existing?.Renderer));
      }

      public ILayerRenderer? Get(LayerSnapshot? layer) {
        if (layer == null) {
          return null;
        }
        return this.resolved.TryGetValue(layer.Id, out Entry? entry) &&
          entry.RendererId == layer.RendererId
            ? entry.Renderer
            : null;
      }

      public void Commit() {
        if (this.disposed) {
          throw new ObjectDisposedException(nameof(Transaction));
        }
        if (this.committed) {
          throw new InvalidOperationException(
            "Renderer transaction has already been committed.");
        }
        foreach (var update in this.updates) {
          update.Runtime.PublishPrepared(update.State);
        }
        foreach (KeyValuePair<LayerInstanceId, Entry> pair in this.resolved) {
          this.owner.entries[pair.Key] = pair.Value;
        }
        this.committed = true;
      }

      public void Dispose() {
        if (this.disposed) {
          return;
        }
        this.disposed = true;
        if (this.committed) {
          return;
        }
        foreach (ILayerRenderer renderer in this.created) {
          if (renderer is IDisposable disposable) {
            disposable.Dispose();
          }
        }
      }
    }

    public ILayerRenderer? Get(LayerSnapshot? layer) {
      if (layer == null) {
        return null;
      }
      return this.entries.TryGetValue(layer.Id, out Entry? entry) &&
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
      Func<LayerSnapshot, ILayerRenderer?> rendererResolver
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
        ILayerRenderer? renderer = rendererResolver(layer);
        DomeBlend? operation = DomeBlend.FromId(layer.OperationId);
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
