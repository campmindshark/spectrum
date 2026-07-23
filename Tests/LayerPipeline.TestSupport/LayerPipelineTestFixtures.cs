using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Visualizers;
using static Spectrum.LayerPipeline.Tests.TestAssertions;

namespace Spectrum.LayerPipeline.Tests {

  public static class LayerPipelineTestFixtures {
    public static CompiledLayer Compiled(
      ILayerRenderer renderer, ICompositeOperation operation, double opacity,
      ImmutableDictionary<string, ParameterValue> parameters
    ) {
      var snapshot = new LayerSnapshot(
        new LayerInstanceId(renderer.RendererId), renderer.RendererId,
        operation.Id, opacity, true, parameters, parameters, null);
      return new CompiledLayer(
        snapshot, renderer, ImmutableArray<Input>.Empty, operation,
        operation.CompileOptions(parameters));
    }

    public static RenderPlan Compile(
      RenderPlanCompiler compiler, LayerRendererStore store,
      LayerStackSnapshot snapshot
    ) => compiler.Compile(
      snapshot, layer => store.Resolve(layer).Renderer);

    public static T BuiltInOptions<T>(DomeLayerSettings layer)
      where T : class, ILayerRendererOptions {
      (LayerStackSnapshot? snapshot, string? error) =
        new LayerStackService(DomeLayerCatalog.Metadata).CreateSnapshot(new[] { layer });
      if (snapshot == null || error != null) {
        throw new InvalidOperationException(error);
      }
      LayerDefinition? definition = DomeLayerCatalog.Metadata.Get(
        layer.VisualizerKey);
      Assert(definition != null,
        "the built-in layer definition is missing");
      ILayerRendererOptions options = definition.CompileOptions(
        snapshot.Layers[0].RendererParameters);
      return options as T ?? throw new InvalidOperationException(
        "Unexpected options type " + options.GetType().Name + ".");
    }

    public static global::Spectrum.SpectrumConfiguration
      ConfigurationWithLayers(params DomeLayerSettings[] layers) {
      var config = new global::Spectrum.SpectrumConfiguration();
      config.ReplaceDomeLayerStack(layers);
      return config;
    }

    public static DomeLayerSettings Layer(string key, string? id) => new() {
      InstanceId = id,
      VisualizerKey = key,
      BlendMode = DomeBlend.Add.Id,
      Opacity = 1,
      Enabled = true,
    };

    public static LayerSnapshot SnapshotWithParameter(
      string id, string key, double value
    ) {
      var parameters = ImmutableDictionary<string, ParameterValue>.Empty.Add(
        key, new ParameterValue(DomeLayerParamType.Double, value));
      return new LayerSnapshot(
        new LayerInstanceId(id), "test", DomeBlend.Add.Id, 1, true,
        parameters, ImmutableDictionary<string, ParameterValue>.Empty, null);
    }

    public static LayerSnapshot SnapshotForRenderer(string id, string renderer) =>
      new LayerSnapshot(
        new LayerInstanceId(id), renderer, DomeBlend.Add.Id, 1, true,
        ImmutableDictionary<string, ParameterValue>.Empty,
        ImmutableDictionary<string, ParameterValue>.Empty, null);

    public static DomeTopology OnePixelTopology() => new(new[] {
      new DomeTopologyPixel(0, 0, .5, .5),
    });

    public static DomeTopology TwoPixelTopology() => new(new[] {
      new DomeTopologyPixel(0, 0, .45, .5),
      new DomeTopologyPixel(1, 0, .55, .5),
    });

    public static DomeTopology LinearTopology(int count) {
      var pixels = new DomeTopologyPixel[count];
      for (int i = 0; i < count; i++) {
        pixels[i] = new DomeTopologyPixel(0, i, .4 + i * .02, .5);
      }
      return new DomeTopology(pixels);
    }

    public static DomeTopology GridTopology(
      int width, int height, double spacing
    ) {
      var pixels = new DomeTopologyPixel[width * height];
      double left = 0.5 - (width - 1) * spacing / 2;
      double top = 0.5 - (height - 1) * spacing / 2;
      for (int y = 0; y < height; y++) {
        for (int x = 0; x < width; x++) {
          int i = y * width + x;
          pixels[i] = new DomeTopologyPixel(
            0, i, left + x * spacing, top + y * spacing);
        }
      }
      return new DomeTopology(pixels);
    }

    public static DomeTopology RingTopology(int count, double radius) {
      var pixels = new DomeTopologyPixel[count];
      for (int i = 0; i < count; i++) {
        double angle = 2 * Math.PI * i / count;
        double x = radius * Math.Cos(angle);
        double y = radius * Math.Sin(angle);
        pixels[i] = new DomeTopologyPixel(
          0, i, (x + 1) * .5, (1 - y) * .5);
      }
      return new DomeTopology(pixels);
    }

    public static DomeTopology ProjectedTopology(
      params (double X, double Y)[] points
    ) {
      var pixels = new DomeTopologyPixel[points.Length];
      for (int i = 0; i < points.Length; i++) {
        double topDownX = (points[i].X + 1) * .5;
        double topDownY = (1 - points[i].Y) * .5;
        pixels[i] = new DomeTopologyPixel(
          i, 0, topDownX, topDownY, topDownX, topDownY);
      }
      return new DomeTopology(pixels);
    }

    public static void SetPaletteColors(
      global::Spectrum.SpectrumConfiguration config,
      Func<int, int> colorAt
    ) {
      var colors = new LEDColor[DomePalette.SlotCount];
      for (int color = 0; color < colors.Length; color++) {
        colors[color] = new LEDColor(colorAt(color));
      }
      config.ReplaceDomePalettes(new List<DomePalette> {
        new DomePalette { Name = "Test", Colors = colors },
      });
    }

    public static void AssertColors(
      string name, DomeFrame frame, int[] expected
    ) {
      Assert(expected.Length == frame.pixels.Length,
        name + " has the wrong fixture length");
      for (int i = 0; i < expected.Length; i++) {
        Assert(frame.pixels[i].color == expected[i],
          name + " pixel " + i + " expected 0x" +
          expected[i].ToString("X6") + " but got 0x" +
          frame.pixels[i].color.ToString("X6"));
      }
    }

    public static void AssertClose(
      double expected, double actual, string message
    ) {
      Assert(Math.Abs(expected - actual) < 0.000000001,
        message + " expected " + expected + " but got " + actual);
    }

    public static DomeFrame RequireFrame(
      DomeFrame? frame, string context
    ) => frame ?? throw new InvalidOperationException(
      context + " produced no frame");

    public sealed class FakeRenderer : ILayerRenderer {
      public string RendererId { get; }
      public DomeFrame Frame { get; }
      public bool IsAvailable => true;
      public IReadOnlyList<Input> RequiredInputs { get; }
      public FakeRenderer(
        string id, DomeFrame frame,
        IReadOnlyList<Input>? requiredInputs = null
      ) {
        this.RendererId = id;
        this.Frame = frame;
        this.RequiredInputs = requiredInputs ?? Array.Empty<Input>();
      }
    }

    public sealed class DisposableFakeRenderer : ILayerRenderer, IDisposable {
      public string RendererId { get; }
      public DomeFrame Frame { get; }
      public bool IsAvailable => true;
      public IReadOnlyList<Input> RequiredInputs => Array.Empty<Input>();
      public bool Disposed { get; private set; }

      public DisposableFakeRenderer(string id, DomeTopology topology) {
        this.RendererId = id;
        this.Frame = new DomeFrame(topology);
      }

      public void Dispose() => this.Disposed = true;
    }

    public sealed class FakeInput : Input {
      public bool Active { get; set; }
      public bool AlwaysActive => false;
      public bool Enabled => true;
      public void OperatorUpdate() { }
    }

    public sealed class FakeAudioLevelInput : IAudioLevelInput {
      public bool Active { get; set; }
      public bool AlwaysActive => true;
      public bool Enabled => true;
      public float Volume => 0.25f;
      public void OperatorUpdate() { }
    }

    public sealed class FakeMidiControlInput : IMidiControlInput {
      public bool Active { get; set; }
      public bool AlwaysActive => true;
      public bool Enabled => true;
      public ObservableMidiLog MidiLog { get; } = new ObservableMidiLog();
      public long AppliedDeviceGeneration => 0;
      public Task DispatchBindingsAsync(MidiCommand command) =>
        Task.CompletedTask;
      public void OperatorUpdate() { }
    }

    public sealed class FakeSpectrumInputFactory : ISpectrumInputFactory {
      public FakeAudioLevelInput Audio { get; } =
        new FakeAudioLevelInput();
      public FakeMidiControlInput Midi { get; } =
        new FakeMidiControlInput();

      public IAudioLevelInput CreateAudioInput(
        Configuration config,
        BeatBroadcaster beat
      ) => this.Audio;

      public IMidiControlInput CreateMidiInput(
        Configuration config,
        BeatBroadcaster beat,
        ApplicationStateDispatcher stateDispatcher
      ) => this.Midi;
    }


    public sealed class InlineGateway : ApplicationStateDispatcher {
      public bool CheckAccess() => true;
      public void Post(Action mutation) => mutation();
      public Task InvokeAsync(Action mutation) {
        mutation();
        return Task.CompletedTask;
      }
      public Task<T> InvokeAsync<T>(Func<T> read) =>
        Task.FromResult(read());
    }

    public static T RunOnDedicatedThread<T>(Func<T> action) {
      var completion = new TaskCompletionSource<T>(
        TaskCreationOptions.RunContinuationsAsynchronously);
      var thread = new Thread(() => {
        try {
          completion.SetResult(action());
        } catch (Exception error) {
          completion.SetException(error);
        }
      }) {
        IsBackground = true,
      };
      thread.Start();
      Assert(thread.Join(TimeSpan.FromSeconds(3)),
        "dedicated test thread did not complete");
      return completion.Task.GetAwaiter().GetResult();
    }

    public sealed class QueuedStateDispatcher :
      ApplicationStateDispatcher {
      private readonly int ownerThreadId =
        Environment.CurrentManagedThreadId;
      private readonly Queue<Action> pending = new Queue<Action>();
      private readonly AutoResetEvent pendingQueued = new AutoResetEvent(false);

      public bool CheckAccess() =>
        Environment.CurrentManagedThreadId == this.ownerThreadId;

      public int PendingCount {
        get {
          lock (this.pending) {
            return this.pending.Count;
          }
        }
      }

      public void Post(Action mutation) {
        lock (this.pending) {
          this.pending.Enqueue(mutation);
        }
        this.pendingQueued.Set();
      }

      public bool WaitForPending(TimeSpan timeout) =>
        this.pendingQueued.WaitOne(timeout);

      public Task InvokeAsync(Action mutation) {
        if (this.CheckAccess()) {
          mutation();
          return Task.CompletedTask;
        }
        var completion = new TaskCompletionSource(
          TaskCreationOptions.RunContinuationsAsynchronously);
        this.Post(() => {
          try {
            mutation();
            completion.SetResult();
          } catch (Exception error) {
            completion.SetException(error);
          }
        });
        return completion.Task;
      }

      public Task<T> InvokeAsync<T>(Func<T> read) {
        if (this.CheckAccess()) {
          return Task.FromResult(read());
        }
        var completion = new TaskCompletionSource<T>(
          TaskCreationOptions.RunContinuationsAsynchronously);
        this.Post(() => {
          try {
            completion.SetResult(read());
          } catch (Exception error) {
            completion.SetException(error);
          }
        });
        return completion.Task;
      }

      public void Drain() {
        Assert(this.CheckAccess(),
          "state dispatcher drained from a non-owner thread");
        while (true) {
          Action mutation;
          lock (this.pending) {
            if (this.pending.Count == 0) {
              return;
            }
            mutation = this.pending.Dequeue();
          }
          mutation();
        }
      }
    }

    public sealed class FixedOrientation : OrientationAngleProvider {
      private readonly double angle;
      public int UpdateCount { get; private set; }

      public FixedOrientation(double angle) {
        this.angle = angle;
      }

      public bool TryGetAngle(out double angle) {
        angle = this.angle;
        return true;
      }

      public void Update() => this.UpdateCount++;
    }

    public sealed class SwapSnapshotOperation : ICompositeOperation {
      public string Id { get; }
      public CompositeRequirements Requirements =>
        CompositeRequirements.ReadsDestination |
        CompositeRequirements.ReadsDestinationNeighbors;
      public DomeFrame? SeenSnapshot { get; private set; }
      public int FirstSeen { get; private set; }
      public int SecondSeen { get; private set; }

      public SwapSnapshotOperation(string id) {
        this.Id = id;
      }

      public ICompositeOptions CompileOptions(
        ImmutableDictionary<string, ParameterValue> parameters
      ) => EmptyCompositeOptions.Instance;

      public void Execute(in DomeBlendContext context) {
        DomeFrame snapshot = context.Snapshot ??
          throw new InvalidOperationException(
            "spatial pass received no snapshot");
        this.SeenSnapshot = snapshot;
        this.FirstSeen = snapshot.pixels[0].color;
        this.SecondSeen = snapshot.pixels[1].color;
        context.Dest.pixels[0].color = this.SecondSeen;
        context.Dest.pixels[1].color = this.FirstSeen;
      }
    }

    public sealed record ScalarOptions(double Value)
      : ILayerRendererOptions;
  }
}
