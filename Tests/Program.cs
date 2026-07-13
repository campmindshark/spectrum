using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Spectrum.Base;
using Spectrum.LEDs;
using XSerializer;

namespace Spectrum.LayerPipeline.Tests {

  internal static class Program {
    private static int failures;

    private static void Main() {
      Run("catalog metadata is unique", CatalogIsUnique);
      Run("duplicate renderer kinds get stable instance IDs", DuplicateKinds);
      Run("parameters compile into separate namespaces", ParameterNamespaces);
      Run("compiled renderer runtime updates in place", RuntimeUpdatesInPlace);
      Run("renderer runtime swaps immutable options", RuntimeOptionsSwap);
      Run("compiled plan freezes renderer inputs", PlanFreezesRendererInputs);
      Run("configuration publishes immutable layer snapshots",
        ConfigurationPublishesSnapshot);
      Run("compositor preserves stack order and bottom behavior", StackOrder);
      Run("scratch copies only mutable channels", ScratchCopiesChannelsOnly);
      Run("frame operations require shared topology", FramesRequireTopology);
      Run("operator creates independent duplicate renderers", DuplicateRenderers);
      Run("compiled plan schedules enabled instances", PlanSchedulesInstances);
      Run("zero opacity is a kernel identity", ZeroOpacityIdentity);
      Run("spatial effects declare neighbor reads", SpatialRequirements);
      Run("configuration with layers serializes", ConfigurationSerializes);
      if (failures != 0) {
        Environment.ExitCode = 1;
      }
    }

    private static void Run(string name, Action test) {
      try {
        test();
        Console.WriteLine("PASS " + name);
      } catch (Exception error) {
        failures++;
        Console.Error.WriteLine("FAIL " + name + ": " + error.Message);
      }
    }

    private static void CatalogIsUnique() {
      var ids = new HashSet<string>();
      foreach (LayerDefinition definition in LayerCatalog.Default.Definitions) {
        Assert(ids.Add(definition.Id), "duplicate " + definition.Id);
        var keys = new HashSet<string>();
        foreach (DomeLayerParam parameter in definition.Parameters) {
          Assert(keys.Add(parameter.Key), "duplicate parameter " + parameter.Key);
        }
      }
    }

    private static void DuplicateKinds() {
      var input = new[] {
        Layer("wave", "a"), Layer("wave", "b"),
      };
      (List<DomeLayerSettings> stack, string error) =
        new LayerStackService().Normalize(input);
      Assert(error == null, error);
      Assert(stack.Count == 2, "layers were rejected");
      Assert(stack[0].InstanceId != stack[1].InstanceId, "IDs collided");
    }

    private static void ParameterNamespaces() {
      DomeLayerSettings layer = Layer("wave", "wave-1");
      layer.BlendMode = DomeBlend.ChromaticFringe.Name;
      layer.Params = new Dictionary<string, double> {
        ["speed"] = 999,
        ["offset"] = 999,
        ["unknown"] = 1,
      };
      (LayerStackSnapshot snapshot, string error) =
        new LayerStackService().CreateSnapshot(new[] { layer });
      Assert(error == null, error);
      LayerSnapshot compiled = snapshot.Layers[0];
      Assert(compiled.RendererParameters.ContainsKey("speed"),
        "renderer option missing");
      Assert(!compiled.RendererParameters.ContainsKey("offset"),
        "operation option leaked into renderer namespace");
      Assert(compiled.OperationParameters.ContainsKey("offset"),
        "operation option missing");
      Assert(!compiled.OperationParameters.ContainsKey("unknown"),
        "unknown option survived");
    }

    private static void RuntimeUpdatesInPlace() {
      LayerSnapshot first = SnapshotWithParameter("runtime-1", "speed", 1);
      LayerRendererRuntime captured = null;
      var topology = OnePixelTopology();
      var catalog = new LayerCatalog(new[] {
        new LayerDefinition(
          "test", "Test",
          context => {
            captured = context.Runtime;
            return new FakeRenderer(
              "test", new DomeFrame(topology));
          },
          new[] {
            new DomeLayerParam {
              Key = "speed", Type = DomeLayerParamType.Double,
              Min = 0, Max = 10, Default = 0,
            },
          })
      });
      var compiler = new RenderPlanCompiler(catalog);
      RenderPlan initial = compiler.Compile(
        new LayerStackSnapshot(ImmutableArray.Create(first)),
        (IReadOnlyDictionary<Type, object>)null);
      LayerSnapshot second = SnapshotWithParameter("runtime-1", "speed", 2);
      RenderPlan updated = compiler.Compile(
        new LayerStackSnapshot(ImmutableArray.Create(second)),
        (IReadOnlyDictionary<Type, object>)null);

      Assert(ReferenceEquals(
        initial.Layers[0].Renderer, updated.Layers[0].Renderer),
        "renderer instance was recreated");
      Assert(captured.Parameter("speed") == 2,
        "renderer runtime kept stale parameters");
    }

    private static void RuntimeOptionsSwap() {
      DomeLayerSettings first = Layer("wave", "runtime-wave");
      first.Params = new Dictionary<string, double> { ["speed"] = .25 };
      (LayerStackSnapshot initial, string initialError) =
        new LayerStackService().CreateSnapshot(new[] { first });
      Assert(initialError == null, initialError);
      var runtime = new LayerRendererRuntime(initial.Layers[0]);
      Assert(runtime.Parameter("speed") == .25, "initial option missing");

      DomeLayerSettings second = Layer("wave", "runtime-wave");
      second.Params = new Dictionary<string, double> { ["speed"] = 1.25 };
      (LayerStackSnapshot changed, string changedError) =
        new LayerStackService().CreateSnapshot(new[] { second });
      Assert(changedError == null, changedError);
      runtime.Publish(changed.Layers[0]);
      Assert(runtime.Parameter("speed") == 1.25,
        "replacement option was not published");
      Assert(initial.Layers[0].RendererParameters["speed"].Value == .25,
        "the original snapshot was mutated");
    }

    private static void PlanFreezesRendererInputs() {
      LayerSnapshot snapshot = SnapshotWithParameter("input-1", "speed", 1);
      var first = new FakeInput();
      var second = new FakeInput();
      var declared = new Input[] { first, first, second };
      var topology = OnePixelTopology();
      var catalog = new LayerCatalog(new[] {
        new LayerDefinition(
          "test", "Test",
          context => new FakeRenderer(
            "test", new DomeFrame(topology), declared),
          new[] {
            new DomeLayerParam {
              Key = "speed", Type = DomeLayerParamType.Double,
              Min = 0, Max = 10, Default = 0,
            },
          })
      });
      RenderPlan plan = new RenderPlanCompiler(catalog).Compile(
        new LayerStackSnapshot(ImmutableArray.Create(snapshot)),
        (IReadOnlyDictionary<Type, object>)null);

      declared[0] = null;
      Assert(plan.Layers[0].RequiredInputs.Length == 2,
        "duplicate input requirements survived compilation");
      Assert(ReferenceEquals(plan.Layers[0].RequiredInputs[0], first) &&
        ReferenceEquals(plan.Layers[0].RequiredInputs[1], second),
        "the plan retained the renderer's mutable input array");
    }

    private static void ConfigurationPublishesSnapshot() {
      var config = new global::Spectrum.SpectrumConfiguration {
        domeLayerStack = new List<DomeLayerSettings> {
          Layer("background", null),
        },
      };
      var source = (ILayerStackSnapshotSource)config;
      LayerStackSnapshot published = source.DomeLayerStackSnapshot;
      DomeLayerSettings dto = config.domeLayerStack[0];

      Assert(!string.IsNullOrWhiteSpace(dto.InstanceId),
        "the publication boundary did not assign an instance ID");
      Assert(published.Layers[0].Id.Value == dto.InstanceId,
        "the DTO and immutable snapshot received different identities");
      dto.Enabled = false;
      Assert(published.Layers[0].Enabled,
        "the published snapshot retained a mutable DTO");
    }

    private static void StackOrder() {
      DomeTopology topology = OnePixelTopology();
      var bottomFrame = new DomeFrame(topology);
      bottomFrame.pixels[0].color = 0x200000;
      var topFrame = new DomeFrame(topology);
      topFrame.pixels[0].color = 0x002000;
      var bottom = new FakeRenderer("bottom", bottomFrame);
      var top = new FakeRenderer("top", topFrame);
      var empty = ImmutableDictionary<string, ParameterValue>.Empty;
      var plan = new RenderPlan(ImmutableArray.Create(
        Compiled(bottom, DomeBlend.Multiply, 1, empty),
        Compiled(top, DomeBlend.Add, 0.5, empty)));
      var compositor = new DomeCompositor(
        () => new DomeFrame(topology), elapsedSeconds: () => 0);
      compositor.Publish(plan);
      DomeFrame result = compositor.Compose();
      Assert(result.pixels[0].color == 0x201000,
        "unexpected composite 0x" + result.pixels[0].color.ToString("X6"));
    }

    private static void ScratchCopiesChannelsOnly() {
      var points = new[] {
        new DomeTopologyPixel(4, 0, .25, .75),
      };
      var topology = new DomeTopology(points);
      points[0] = new DomeTopologyPixel(9, 0, .9, .1);
      var source = new DomeFrame(topology);
      var target = new DomeFrame(topology);
      source.pixels[0].color = 0x123456;
      target.pixels[0].color = 0xFFFFFF;
      target.CopyFrom(source);
      Assert(target.pixels[0].color == 0x123456, "channels did not copy");
      Assert(ReferenceEquals(source.Topology, target.Topology),
        "frames did not share their topology");
      Assert(topology.PixelAt(0) == new DomeTopologyPixel(4, 0, .25, .75),
        "topology retained its mutable constructor array");
      Assert(typeof(LEDDomeOutputPixel).GetField("x") == null &&
        typeof(LEDDomeOutputPixel).GetField("strutIndex") == null,
        "frame pixels still contain topology metadata");
      Assert(source.BakePixelPositions().Equals(target.BakePixelPositions()),
        "frames did not share baked topology normals");
    }

    private static void FramesRequireTopology() {
      var first = new DomeFrame(OnePixelTopology());
      var second = new DomeFrame(OnePixelTopology());
      bool rejected = false;
      try {
        first.CopyFrom(second);
      } catch (ArgumentException) {
        rejected = true;
      }
      Assert(rejected, "mismatched logical frames were copied by index");
    }

    private static void DuplicateRenderers() {
      var config = new global::Spectrum.SpectrumConfiguration {
        domeLayerStack = new List<DomeLayerSettings> {
          Layer("background", "background-a"),
          Layer("background", "background-b"),
        },
      };
      var runtime = new global::Spectrum.Operator(config);
      int count = 0;
      foreach (Visualizer visualizer in runtime.DomeOutput.GetVisualizers()) {
        if (visualizer is DomeLayerVisualizer layer &&
            layer.LayerKey == "background") {
          count++;
        }
      }
      Assert(count == 2, "expected two renderer instances, got " + count);
    }

    private static void PlanSchedulesInstances() {
      DomeLayerSettings disabled = Layer("background", "background-off");
      disabled.Enabled = false;
      var config = new global::Spectrum.SpectrumConfiguration {
        domeLayerStack = new List<DomeLayerSettings> {
          disabled,
          Layer("background", "background-on"),
        },
      };
      var runtime = new global::Spectrum.Operator(config);
      RenderPlan plan = runtime.DomeOutput.RenderPlan;
      Assert(plan.Layers.Length == 1, "disabled layer entered the plan");
      Assert(plan.Layers[0].Snapshot.Id.Value == "background-on",
        "the enabled instance was not scheduled");
    }

    private static void ZeroOpacityIdentity() {
      DomeTopology topology = OnePixelTopology();
      foreach (DomeBlend operation in new[] {
        DomeBlend.Over, DomeBlend.Add, DomeBlend.Screen, DomeBlend.Lighten,
        DomeBlend.Multiply, DomeBlend.Desaturate, DomeBlend.Hue,
      }) {
        var dest = new DomeFrame(topology);
        dest.pixels[0].color = 0x123456;
        dest.pixels[0].hue = .25;
        var source = new DomeFrame(topology);
        source.pixels[0].color = 0xFEDCBA;
        source.pixels[0].hue = .75;
        operation.Execute(new DomeBlendContext(
          dest, source, null, EmptyCompositeOptions.Instance,
          0, 0, null));
        Assert(dest.pixels[0].color == 0x123456 &&
          dest.pixels[0].hue == .25,
          operation.Name + " changed the destination");
      }
    }

    private static void SpatialRequirements() {
      foreach (DomeBlend operation in new[] {
        DomeBlend.ChromaticFringe, DomeBlend.EdgeSpectrum, DomeBlend.Refract,
      }) {
        Assert((operation.Requirements &
          CompositeRequirements.ReadsDestinationNeighbors) != 0,
          operation.Name + " omitted neighbor requirement");
      }
      Assert((DomeBlend.Iridescence.Requirements &
        CompositeRequirements.ReadsDestinationNeighbors) == 0,
        "Iridescence requested unnecessary scratch");
    }

    private static void ConfigurationSerializes() {
      var config = new global::Spectrum.SpectrumConfiguration {
        audioDeviceID = "test-device",
        domeLayerStack = new List<DomeLayerSettings> {
          Layer("background", "serialize-background"),
        },
      };
      using var stream = new MemoryStream();
      new XmlSerializer<global::Spectrum.SpectrumConfiguration>().Serialize(
        stream, config);
      Assert(stream.Length > 0, "serializer produced no XML");
      stream.Position = 0;
      var restored =
        new XmlSerializer<global::Spectrum.SpectrumConfiguration>().Deserialize(
          stream);
      Assert(restored.audioDeviceID == "test-device", "round trip lost config");
      Assert(restored.domeLayerStack.Count == 1 &&
        restored.domeLayerStack[0].InstanceId == "serialize-background",
        "round trip lost layer identity");
    }

    private static CompiledLayer Compiled(
      ILayerRenderer renderer, DomeBlend operation, double opacity,
      ImmutableDictionary<string, ParameterValue> parameters
    ) {
      var snapshot = new LayerSnapshot(
        new LayerInstanceId(renderer.RendererId), renderer.RendererId,
        operation.Name, opacity, true, parameters, parameters, null);
      return new CompiledLayer(
        snapshot, renderer, ImmutableArray<Input>.Empty, operation,
        operation.CompileOptions(parameters));
    }

    private static DomeLayerSettings Layer(string key, string id) => new() {
      InstanceId = id,
      VisualizerKey = key,
      BlendMode = DomeBlend.Add.Name,
      Opacity = 1,
      Enabled = true,
    };

    private static LayerSnapshot SnapshotWithParameter(
      string id, string key, double value
    ) {
      var parameters = ImmutableDictionary<string, ParameterValue>.Empty.Add(
        key, new ParameterValue(DomeLayerParamType.Double, value));
      return new LayerSnapshot(
        new LayerInstanceId(id), "test", DomeBlend.Add.Name, 1, true,
        parameters, ImmutableDictionary<string, ParameterValue>.Empty, null);
    }

    private static DomeTopology OnePixelTopology() => new(new[] {
      new DomeTopologyPixel(0, 0, .5, .5),
    });

    private static void Assert(bool condition, string message) {
      if (!condition) {
        throw new InvalidOperationException(message);
      }
    }

    private sealed class FakeRenderer : ILayerRenderer {
      public string RendererId { get; }
      public DomeFrame Frame { get; }
      public bool IsAvailable => true;
      public IReadOnlyList<Input> RequiredInputs { get; }
      public FakeRenderer(
        string id, DomeFrame frame,
        IReadOnlyList<Input> requiredInputs = null
      ) {
        this.RendererId = id;
        this.Frame = frame;
        this.RequiredInputs = requiredInputs ?? Array.Empty<Input>();
      }
    }

    private sealed class FakeInput : Input {
      public bool Active { get; set; }
      public bool AlwaysActive => false;
      public bool Enabled => true;
      public void OperatorUpdate() { }
    }
  }
}
