using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
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
      Run("typed renderer options preserve numeric casts",
        TypedOptionsPreserveNumericCasts);
      Run("compiled plan freezes renderer inputs", PlanFreezesRendererInputs);
      Run("configuration publishes immutable layer snapshots",
        ConfigurationPublishesSnapshot);
      Run("compositor executes every operation in stack order", StackOrder);
      Run("scratch copies only mutable channels", ScratchCopiesChannelsOnly);
      Run("frame operations require shared topology", FramesRequireTopology);
      Run("operator creates independent duplicate renderers", DuplicateRenderers);
      Run("layer renderers do not receive persisted configuration",
        LayerRenderersAvoidConfiguration);
      Run("vortex uses global fade for hue-bearing trails",
        VortexUsesGlobalFade);
      Run("compiled plan schedules enabled instances", PlanSchedulesInstances);
      Run("scene recall retains duplicate instance state", SceneRecallRetainsState);
      Run("duplicate commands require instance IDs", DuplicateCommandsNeedIds);
      Run("zero opacity is a kernel identity", ZeroOpacityIdentity);
      Run("paint and adjustment kernels match the regression matrix",
        KernelMatrix);
      Run("operation options are normalized before compilation",
        OperationOptionsAreNormalized);
      Run("spatial effects declare neighbor reads", SpatialRequirements);
      Run("spatial passes snapshot and reuse scratch", SpatialPassSnapshots);
      Run("masked adjustment frames are deterministic",
        MaskedAdjustmentFixtures);
      Run("prism frames are deterministic", PrismFixtures);
      Run("compositor replaces plans and holds on empty", PlanReplacement);
      Run("configuration with layers serializes", ConfigurationSerializes);
      OPCWireTests.Register(Run);
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
        Console.Error.WriteLine("FAIL " + name + ": " + error);
      }
    }

    private static void CatalogIsUnique() {
      var ids = new HashSet<string>();
      foreach (LayerDefinition definition in LayerCatalog.Default.Definitions) {
        Assert(ids.Add(definition.Id), "duplicate " + definition.Id);
        Assert(definition.CompileOptions != null,
          "missing options compiler for " + definition.Id);
        var keys = new HashSet<string>();
        foreach (DomeLayerParam parameter in definition.Parameters) {
          Assert(keys.Add(parameter.Key), "duplicate parameter " + parameter.Key);
        }
        (LayerStackSnapshot snapshot, string error) =
          new LayerStackService().CreateSnapshot(new[] {
            Layer(definition.Id, "options-" + definition.Id),
          });
        Assert(error == null, error);
        ILayerRendererOptions options = definition.CompileOptions(
          snapshot.Layers[0].RendererParameters);
        Assert(options != null, "null options for " + definition.Id);
        Assert(options is not LayerRendererParameterOptions,
          "untyped built-in options for " + definition.Id);
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
      LayerDefinition definition = LayerCatalog.Default.Get("wave");
      var runtime = new LayerRendererRuntime(
        initial.Layers[0], definition.CompileOptions);
      WaveLayerOptions original = runtime.GetOptions<WaveLayerOptions>();
      Assert(original.Speed == .25, "initial typed option missing");

      DomeLayerSettings second = Layer("wave", "runtime-wave");
      second.Params = new Dictionary<string, double> { ["speed"] = 1.25 };
      (LayerStackSnapshot changed, string changedError) =
        new LayerStackService().CreateSnapshot(new[] { second });
      Assert(changedError == null, changedError);
      runtime.Publish(changed.Layers[0]);
      WaveLayerOptions replacement = runtime.GetOptions<WaveLayerOptions>();
      Assert(replacement.Speed == 1.25,
        "replacement typed option was not published");
      Assert(original.Speed == .25,
        "the original typed options were mutated");
      Assert(initial.Layers[0].RendererParameters["speed"].Value == .25,
        "the original snapshot was mutated");
    }

    private static void TypedOptionsPreserveNumericCasts() {
      DomeLayerSettings volume = Layer("volume", "typed-volume");
      volume.Params = new Dictionary<string, double> {
        ["animationSize"] = 3.75,
      };
      Assert(BuiltInOptions<VolumeLayerOptions>(volume).AnimationSize == 3,
        "volume animation size no longer truncates");

      DomeLayerSettings points = Layer("point-cloud", "typed-points");
      points.Params = new Dictionary<string, double> { ["count"] = 47.75 };
      Assert(BuiltInOptions<PointCloudLayerOptions>(points).Count == 47,
        "point count no longer truncates");

      DomeLayerSettings noise = Layer("noise-cloud", "typed-noise");
      noise.Params = new Dictionary<string, double> { ["octaves"] = 2.75 };
      Assert(BuiltInOptions<NoiseCloudLayerOptions>(noise).Octaves == 2,
        "noise octave count no longer truncates");
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
      Assert(result.pixels[0].color == 0x001000,
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

    private static void LayerRenderersAvoidConfiguration() {
      Type layerType = typeof(DomeLayerVisualizer);
      Type configurationType = typeof(Configuration);
      foreach (Type type in
          typeof(global::Spectrum.Operator).Assembly.GetTypes()) {
        if (type.IsInterface || !layerType.IsAssignableFrom(type)) {
          continue;
        }
        foreach (var constructor in type.GetConstructors(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic)) {
          foreach (var parameter in constructor.GetParameters()) {
            Assert(parameter.ParameterType != configurationType,
              type.Name + " still receives persisted Configuration");
          }
        }
      }
    }

    private static void VortexUsesGlobalFade() {
      var config = new global::Spectrum.SpectrumConfiguration {
        domeGlobalFadeSpeed = 3,
        domeGlobalHueSpeed = 0,
        domeLayerStack = new List<DomeLayerSettings> {
          Layer("vortex", "vortex-trail"),
        },
      };
      var runtime = new global::Spectrum.Operator(config);
      DomeLayerVisualizer vortex = null;
      foreach (Visualizer visualizer in runtime.DomeOutput.GetVisualizers()) {
        if (visualizer is DomeLayerVisualizer layer &&
            layer.LayerKey == "vortex") {
          vortex = layer;
          break;
        }
      }
      Assert(vortex != null, "vortex renderer was not created");

      Visualizer renderer = (Visualizer)vortex;
      renderer.Visualize();

      // Use the weakest field sample and give its history a distinctive hue,
      // mirroring the output-wide post-frame hue rotation. A long global fade
      // must preserve it when the next, weaker current sample is rendered.
      DomeFrame frame = vortex.LayerBuffer;
      int trailIndex = 0;
      for (int i = 1; i < frame.pixels.Length; i++) {
        if (frame.pixels[i].a < frame.pixels[trailIndex].a) {
          trailIndex = i;
        }
      }
      frame.pixels[trailIndex].color = 0x00FF00;
      renderer.Visualize();
      Assert(frame.pixels[trailIndex].r == 0 &&
          frame.pixels[trailIndex].g > 0 &&
          frame.pixels[trailIndex].b == 0,
        "global fade did not retain the hue-bearing vortex trail");

      // Fade speed zero has zero retention, so the artificial history must be
      // removed and replaced only by this frame's brown-tinted field sample.
      config.domeGlobalFadeSpeed = 0;
      renderer.Visualize();
      Assert(!(frame.pixels[trailIndex].r == 0 &&
          frame.pixels[trailIndex].g > 0 &&
          frame.pixels[trailIndex].b == 0),
        "zero global fade retained stale vortex history");
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

    private static void SceneRecallRetainsState() {
      var config = new global::Spectrum.SpectrumConfiguration {
        domeLayerStack = new List<DomeLayerSettings> {
          Layer("wave", "scene-wave-a"),
          Layer("wave", "scene-wave-b"),
        },
      };
      var scenes = new SceneService(config);
      (bool saved, string saveError) = scenes.Save("duplicates");
      Assert(saved, saveError);

      int created = 0;
      DomeTopology topology = OnePixelTopology();
      var catalog = new LayerCatalog(new[] {
        new LayerDefinition(
          "wave", "Wave",
          context => {
            created++;
            return new FakeRenderer("wave", new DomeFrame(topology));
          },
          Array.Empty<DomeLayerParam>())
      });
      var compiler = new RenderPlanCompiler(catalog);
      LayerStackSnapshot initial =
        ((ILayerStackSnapshotSource)config).DomeLayerStackSnapshot;
      RenderPlan first = compiler.Compile(
        initial, (IReadOnlyDictionary<Type, object>)null);
      ILayerRenderer rendererA = first.Layers[0].Renderer;
      ILayerRenderer rendererB = first.Layers[1].Renderer;
      Assert(!ReferenceEquals(rendererA, rendererB),
        "duplicate instances shared renderer state");
      rendererA.Frame.pixels[0].color = 0x110000;
      rendererB.Frame.pixels[0].color = 0x002200;

      var reorderedSnapshot = new LayerStackSnapshot(ImmutableArray.Create(
        initial.Layers[1], initial.Layers[0]));
      RenderPlan reordered = compiler.Compile(
        reorderedSnapshot, (IReadOnlyDictionary<Type, object>)null);
      Assert(ReferenceEquals(reordered.Layers[0].Renderer, rendererB) &&
        ReferenceEquals(reordered.Layers[1].Renderer, rendererA),
        "reordering changed instance identity");

      config.domeLayerStack = new List<DomeLayerSettings> {
        Layer("background", "temporary-background"),
      };
      compiler.Compile(
        ((ILayerStackSnapshotSource)config).DomeLayerStackSnapshot,
        (IReadOnlyDictionary<Type, object>)null);

      (bool applied, string applyError) = scenes.Apply("duplicates");
      Assert(applied, applyError);
      RenderPlan recalled = compiler.Compile(
        ((ILayerStackSnapshotSource)config).DomeLayerStackSnapshot,
        (IReadOnlyDictionary<Type, object>)null);

      Assert(config.domeLayerStack[0].InstanceId == "scene-wave-a" &&
        config.domeLayerStack[1].InstanceId == "scene-wave-b",
        "scene recall regenerated instance IDs");
      Assert(ReferenceEquals(recalled.Layers[0].Renderer, rendererA) &&
        ReferenceEquals(recalled.Layers[1].Renderer, rendererB),
        "scene recall recreated matching renderer instances");
      Assert(rendererA.Frame.pixels[0].color == 0x110000 &&
        rendererB.Frame.pixels[0].color == 0x002200,
        "scene recall lost retained renderer state");
      Assert(created == 2, "scene recall created extra renderers");
    }

    private static void DuplicateCommandsNeedIds() {
      var config = new global::Spectrum.SpectrumConfiguration {
        domeLayerStack = new List<DomeLayerSettings> {
          Layer("wave", "command-wave-a"),
          Layer("wave", "command-wave-b"),
        },
      };
      var controller = new global::Spectrum.Web.LayersController(
        new InlineGateway(), config);

      (bool ambiguousFire, string fireError) =
        controller.FireAsync("wave").GetAwaiter().GetResult();
      Assert(!ambiguousFire && fireError.Contains("use an instance id"),
        "ambiguous renderer-key fire was accepted");
      Assert(config.domeLayerFireCounters.Count == 0,
        "ambiguous fire changed a counter");

      (bool fired, string targetedFireError) =
        controller.FireAsync("command-wave-b").GetAwaiter().GetResult();
      Assert(fired, targetedFireError);
      Assert(config.domeLayerFireCounters.Count == 1 &&
        config.domeLayerFireCounters["command-wave-b"] == 1,
        "targeted fire reached the wrong duplicate");

      (bool ambiguousClear, string clearError) =
        controller.ClearAsync("wave").GetAwaiter().GetResult();
      Assert(!ambiguousClear && clearError.Contains("use an instance id"),
        "ambiguous renderer-key clear was accepted");
      Assert(config.domeLayerClearCounters.Count == 0,
        "ambiguous clear changed a counter");

      (bool cleared, string targetedClearError) =
        controller.ClearAsync("command-wave-a").GetAwaiter().GetResult();
      Assert(cleared, targetedClearError);
      Assert(config.domeLayerClearCounters.Count == 1 &&
        config.domeLayerClearCounters["command-wave-a"] == 1,
        "targeted clear reached the wrong duplicate");
    }

    private static void ZeroOpacityIdentity() {
      DomeTopology topology = OnePixelTopology();
      foreach (DomeBlend operation in DomeBlend.All) {
        var dest = new DomeFrame(topology);
        dest.pixels[0].color = 0x123456;
        dest.pixels[0].SetAlpha(.35);
        dest.pixels[0].hue = .25;
        var source = new DomeFrame(topology);
        source.pixels[0].color = 0xFEDCBA;
        source.pixels[0].hue = .75;
        var snapshot = new DomeFrame(topology);
        snapshot.CopyFrom(dest);
        operation.Execute(new DomeBlendContext(
          dest, source,
          (operation.Requirements &
            CompositeRequirements.ReadsDestinationNeighbors) != 0
              ? snapshot : null,
          operation.CompileOptions(
            ImmutableDictionary<string, ParameterValue>.Empty),
          0, 0, null));
        Assert(dest.pixels[0].color == 0x123456 &&
          dest.pixels[0].a == .35 && dest.pixels[0].hue == .25,
          operation.Name + " changed the destination");
      }
    }

    private static void KernelMatrix() {
      var expectedHalf = new Dictionary<DomeBlend, int[]> {
        [DomeBlend.Over] = new[] {
          0x000000, 0x7F7F7F, 0x7F7F00, 0x00FF00, 0x3F00BF, 0x504040,
        },
        [DomeBlend.Add] = new[] {
          0x7F7F7F, 0xFFFFFF, 0xFF7F00, 0x00FF7F, 0x7F00FF, 0x606070,
        },
        [DomeBlend.Screen] = new[] {
          0x7F7F7F, 0xFFFFFF, 0xFF7F00, 0x00FF7F, 0x7F00FF, 0x575769,
        },
        [DomeBlend.Lighten] = new[] {
          0x7F7F7F, 0xFFFFFF, 0xFF7F00, 0x00FF7F, 0x7F00FF, 0x404060,
        },
        [DomeBlend.Multiply] = new[] {
          0x000000, 0x7F7F7F, 0x7F0000, 0x007F00, 0x00007F, 0x182836,
        },
        [DomeBlend.Desaturate] = new[] {
          0x000000, 0xFFFFFF, 0xA52626, 0x00FF00, 0x0707C6, 0x2D3D4D,
        },
        [DomeBlend.Hue] = new[] {
          0x000000, 0x7F7F7F, 0xE57F00, 0x00FF00, 0x003FD8, 0x106070,
        },
      };
      var expectedFull = new Dictionary<DomeBlend, int[]> {
        [DomeBlend.Over] = new[] {
          0x000000, 0x000000, 0x00FF00, 0x00FF00, 0x7F007F, 0x804020,
        },
        [DomeBlend.Add] = new[] {
          0xFFFFFF, 0xFFFFFF, 0xFFFF00, 0x00FFFF, 0xFF00FF, 0xA08080,
        },
        [DomeBlend.Screen] = new[] {
          0xFFFFFF, 0xFFFFFF, 0xFFFF00, 0x00FFFF, 0xFF00FF, 0x8F6F73,
        },
        [DomeBlend.Lighten] = new[] {
          0xFFFFFF, 0xFFFFFF, 0xFFFF00, 0x00FFFF, 0xFF00FF, 0x804060,
        },
        [DomeBlend.Multiply] = new[] {
          0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x10100C,
        },
        [DomeBlend.Desaturate] = new[] {
          0x000000, 0xFFFFFF, 0x4C4C4C, 0x00FF00, 0x0E0E8E, 0x3A3A3A,
        },
        [DomeBlend.Hue] = new[] {
          0x000000, 0x000000, 0xCBFF00, 0x00FF00, 0x007FB2, 0x008080,
        },
      };

      foreach (DomeBlend operation in expectedHalf.Keys) {
        DomeFrame half = ExecuteKernel(operation, .5);
        DomeFrame full = ExecuteKernel(operation, 1);
        AssertColors(operation.Name + " at 0.5", half,
          expectedHalf[operation]);
        AssertColors(operation.Name + " at 1", full,
          expectedFull[operation]);
        AssertKernelChannels(operation, half, .5);
        AssertKernelChannels(operation, full, 1);
      }
    }

    private static DomeFrame ExecuteKernel(DomeBlend operation, double opacity) {
      int[] destColors = {
        0x000000, 0xFFFFFF, 0xFF0000, 0x00FF00, 0x0000FF, 0x204060,
      };
      int[] sourceColors = {
        0xFFFFFF, 0x000000, 0x00FF00, 0x0000FF, 0xFF0000, 0x804020,
      };
      double[] sourceAlpha = { 0, 1, 1, 0, .5, 1 };
      DomeTopology topology = LinearTopology(destColors.Length);
      var dest = new DomeFrame(topology);
      var source = new DomeFrame(topology);
      for (int i = 0; i < destColors.Length; i++) {
        dest.pixels[i].color = destColors[i];
        dest.pixels[i].SetAlpha(.25);
        dest.pixels[i].hue = i / 10d;
        source.pixels[i].color = sourceColors[i];
        source.pixels[i].SetAlpha(sourceAlpha[i]);
        source.pixels[i].hue = .6 + i / 100d;
      }
      operation.Execute(new DomeBlendContext(
        dest, source, null, EmptyCompositeOptions.Instance,
        opacity, 0, null));
      return dest;
    }

    private static void AssertKernelChannels(
      DomeBlend operation, DomeFrame frame, double opacity
    ) {
      double[] expectedAlpha = operation == DomeBlend.Over
        ? (opacity == .5
          ? new[] { .25, .625, .625, .25, .4375, .625 }
          : new[] { .25, 1, 1, .25, .625, 1 })
        : new[] { .25, .25, .25, .25, .25, .25 };
      double[] expectedHue;
      if (operation == DomeBlend.Over) {
        expectedHue = new[] { 0, .61, .62, .3, .64, .65 };
      } else if ((operation.Requirements &
          CompositeRequirements.PublishesHue) != 0) {
        expectedHue = new[] { .6, .61, .62, .63, .64, .65 };
      } else {
        expectedHue = new[] { 0, .1, .2, .3, .4, .5 };
      }
      for (int i = 0; i < frame.pixels.Length; i++) {
        AssertClose(expectedAlpha[i], frame.pixels[i].a,
          operation.Name + " alpha " + i);
        AssertClose(expectedHue[i], frame.pixels[i].hue,
          operation.Name + " hue " + i);
      }
    }

    private static void OperationOptionsAreNormalized() {
      ChromaticFringeOptions fringe = (ChromaticFringeOptions)
        CompileOptions(DomeBlend.ChromaticFringe, new Dictionary<string, double> {
          ["offset"] = double.NaN,
          ["spin"] = double.PositiveInfinity,
          ["follow"] = -1,
        });
      AssertClose(.045, fringe.Offset, "fringe NaN default");
      AssertClose(2, fringe.Spin, "fringe spin clamp");
      Assert(fringe.FollowOrientation, "fringe bool coercion failed");

      EdgeSpectrumOptions edge = (EdgeSpectrumOptions)
        CompileOptions(DomeBlend.EdgeSpectrum, new Dictionary<string, double> {
          ["strength"] = double.NegativeInfinity,
          ["offset"] = double.PositiveInfinity,
        });
      AssertClose(0, edge.Strength, "edge strength clamp");
      AssertClose(.12, edge.Offset, "edge offset clamp");

      RefractOptions refract = (RefractOptions)
        CompileOptions(DomeBlend.Refract, new Dictionary<string, double> {
          ["strength"] = double.NaN,
        });
      AssertClose(.05, refract.Strength, "refract NaN default");

      IridescenceOptions iridescence = (IridescenceOptions)
        CompileOptions(DomeBlend.Iridescence, new Dictionary<string, double> {
          ["strength"] = -1,
          ["bands"] = 99,
          ["spin"] = double.NaN,
          ["follow"] = 0,
        });
      AssertClose(0, iridescence.Strength, "iridescence strength clamp");
      AssertClose(8, iridescence.Bands, "iridescence bands clamp");
      AssertClose(.2, iridescence.Spin, "iridescence spin default");
      Assert(!iridescence.FollowOrientation,
        "iridescence bool coercion failed");
    }

    private static ICompositeOptions CompileOptions(
      DomeBlend operation, Dictionary<string, double> parameters
    ) {
      DomeLayerSettings layer = Layer("background", "options-fixture");
      layer.BlendMode = operation.Name;
      layer.Params = parameters;
      (LayerStackSnapshot snapshot, string error) =
        new LayerStackService().CreateSnapshot(new[] { layer });
      Assert(error == null, error);
      return operation.CompileOptions(snapshot.Layers[0].OperationParameters);
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

    private static void SpatialPassSnapshots() {
      DomeTopology topology = TwoPixelTopology();
      var bottomFrame = new DomeFrame(topology);
      bottomFrame.pixels[0].color = 0x110000;
      bottomFrame.pixels[1].color = 0x002200;
      var maskFrame = new DomeFrame(topology);
      var firstOperation = new SwapSnapshotOperation("swap-1");
      var secondOperation = new SwapSnapshotOperation("swap-2");
      var plan = new RenderPlan(ImmutableArray.Create(
        Compiled(
          new FakeRenderer("bottom", bottomFrame), DomeBlend.Add, 1,
          ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(
          new FakeRenderer("mask-1", maskFrame), firstOperation, 1,
          ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(
          new FakeRenderer("mask-2", maskFrame), secondOperation, 1,
          ImmutableDictionary<string, ParameterValue>.Empty)));
      int frameAllocations = 0;
      var compositor = new DomeCompositor(
        () => {
          frameAllocations++;
          return new DomeFrame(topology);
        },
        elapsedSeconds: () => 0);
      compositor.Publish(plan);

      DomeFrame result = compositor.Compose();
      Assert(firstOperation.FirstSeen == 0x110000 &&
        firstOperation.SecondSeen == 0x002200,
        "the first spatial pass did not see the pre-pass destination");
      Assert(secondOperation.FirstSeen == 0x002200 &&
        secondOperation.SecondSeen == 0x110000,
        "the second spatial pass received a stale snapshot");
      Assert(ReferenceEquals(
        firstOperation.SeenSnapshot, secondOperation.SeenSnapshot),
        "spatial passes did not reuse scratch storage");
      Assert(frameAllocations == 2,
        "expected one destination and one scratch frame");
      Assert(result.pixels[0].color == 0x110000 &&
        result.pixels[1].color == 0x002200,
        "spatial writes smeared instead of reading the snapshot");
    }

    private static void MaskedAdjustmentFixtures() {
      AssertFixture(
        DomeBlend.Desaturate,
        ImmutableDictionary<string, ParameterValue>.Empty,
        "000000 FFFFFF A52626 959595 0707C6 333B43 4F4F4F 507040 102030");
      AssertFixture(
        DomeBlend.Hue,
        ImmutableDictionary<string, ParameterValue>.Empty,
        "000000 000000 E57F00 33FF00 003FD8 087078 0066FF 234020 102030");
    }

    private static void PrismFixtures() {
      AssertFixture(
        DomeBlend.ChromaticFringe,
        Parameters(("offset", .02), ("spin", 0), ("follow", 0)),
        "000000 FFFF00 FF007F 00FF00 0800BF 2040D7 404020 288020 102030");
      AssertFixture(
        DomeBlend.EdgeSpectrum,
        Parameters(("strength", .35), ("offset", .02)),
        "000000 FFFFFF FF0000 16FF25 0F0AFF 294E60 955920 45802E 102030");
      AssertFixture(
        DomeBlend.Iridescence,
        Parameters(
          ("strength", .6), ("bands", 2), ("spin", 0), ("follow", 0)),
        "000000 66FF6F B24C27 11FF00 0026DB 114E4B 3B660C 2C8018 102030");
      AssertFixture(
        DomeBlend.Refract,
        Parameters(("strength", .02)),
        "000000 204060 8F2030 804020 003FBF C7CFD7 00FF00 306040 102030");
    }

    private static void AssertFixture(
      DomeBlend operation,
      ImmutableDictionary<string, ParameterValue> parameters,
      string expectedFull
    ) {
      DomeFrame full = ComposeFixture(operation, parameters, 1);
      string actual = ColorSignature(full);
      Assert(actual == expectedFull,
        operation.Name + " fixture expected " + expectedFull +
        " but got " + actual);

      DomeFrame half = ComposeFixture(operation, parameters, .5);
      int[] bottomColors = FixtureBottomColors();
      for (int i = 0; i < bottomColors.Length; i++) {
        double br = (bottomColors[i] >> 16) & 0xFF;
        double bg = (bottomColors[i] >> 8) & 0xFF;
        double bb = bottomColors[i] & 0xFF;
        AssertClose((br + full.pixels[i].r) / 2, half.pixels[i].r,
          operation.Name + " half-opacity red " + i);
        AssertClose((bg + full.pixels[i].g) / 2, half.pixels[i].g,
          operation.Name + " half-opacity green " + i);
        AssertClose((bb + full.pixels[i].b) / 2, half.pixels[i].b,
          operation.Name + " half-opacity blue " + i);
        AssertClose(0, full.pixels[i].a,
          operation.Name + " changed the blank destination alpha " + i);
        AssertClose(i / 10d, full.pixels[i].hue,
          operation.Name + " changed destination hue " + i);
      }
    }

    private static DomeFrame ComposeFixture(
      DomeBlend operation,
      ImmutableDictionary<string, ParameterValue> parameters,
      double opacity
    ) {
      DomeTopology topology = FixtureTopology();
      int[] bottomColors = FixtureBottomColors();
      int[] sourceColors = {
        0xFFFFFF, 0x000000, 0x00FF00,
        0x0000FF, 0xFF0000, 0x804020,
        0xFFFFFF, 0x202020, 0xFFFFFF,
      };
      double[] sourceAlpha = { 0, 1, .5, 1, .25, .75, 1, .5, 0 };
      var bottom = new DomeFrame(topology);
      var source = new DomeFrame(topology);
      for (int i = 0; i < bottomColors.Length; i++) {
        bottom.pixels[i].color = bottomColors[i];
        bottom.pixels[i].hue = i / 10d;
        source.pixels[i].color = sourceColors[i];
        source.pixels[i].SetAlpha(sourceAlpha[i]);
        source.pixels[i].hue = i / 8d;
      }
      var plan = new RenderPlan(ImmutableArray.Create(
        Compiled(
          new FakeRenderer("fixture-bottom", bottom), DomeBlend.Add, 1,
          ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(
          new FakeRenderer("fixture-mask", source), operation, opacity,
          parameters)));
      var compositor = new DomeCompositor(
        () => new DomeFrame(topology), elapsedSeconds: () => 0);
      compositor.Publish(plan);
      return compositor.Compose();
    }

    private static int[] FixtureBottomColors() => new[] {
      0x000000, 0xFFFFFF, 0xFF0000,
      0x00FF00, 0x0000FF, 0x204060,
      0x804020, 0x408020, 0x102030,
    };

    private static DomeTopology FixtureTopology() => new(new[] {
      new DomeTopologyPixel(0, 0, .48, .48),
      new DomeTopologyPixel(0, 1, .50, .48),
      new DomeTopologyPixel(0, 2, .52, .48),
      new DomeTopologyPixel(0, 3, .48, .50),
      new DomeTopologyPixel(0, 4, .50, .50),
      new DomeTopologyPixel(0, 5, .52, .50),
      new DomeTopologyPixel(0, 6, .48, .52),
      new DomeTopologyPixel(0, 7, .50, .52),
      new DomeTopologyPixel(0, 8, .52, .52),
    });

    private static ImmutableDictionary<string, ParameterValue> Parameters(
      params (string Key, double Value)[] values
    ) {
      var result = ImmutableDictionary.CreateBuilder<string, ParameterValue>(
        StringComparer.Ordinal);
      foreach ((string key, double value) in values) {
        result[key] = new ParameterValue(DomeLayerParamType.Double, value);
      }
      return result.ToImmutable();
    }

    private static string ColorSignature(DomeFrame frame) {
      var colors = new string[frame.pixels.Length];
      for (int i = 0; i < colors.Length; i++) {
        colors[i] = frame.pixels[i].color.ToString("X6");
      }
      return string.Join(" ", colors);
    }

    private static void PlanReplacement() {
      DomeTopology topology = OnePixelTopology();
      var firstFrame = new DomeFrame(topology);
      firstFrame.pixels[0].color = 0x120000;
      var secondFrame = new DomeFrame(topology);
      secondFrame.pixels[0].color = 0x003400;
      var compositor = new DomeCompositor(
        () => new DomeFrame(topology), elapsedSeconds: () => 0);
      compositor.Publish(new RenderPlan(ImmutableArray.Create(
        Compiled(
          new FakeRenderer("first", firstFrame), DomeBlend.Add, 1,
          ImmutableDictionary<string, ParameterValue>.Empty))));
      Assert(compositor.Compose().pixels[0].color == 0x120000,
        "the first plan did not render");

      compositor.Publish(new RenderPlan(ImmutableArray.Create(
        Compiled(
          new FakeRenderer("second", secondFrame), DomeBlend.Add, 1,
          ImmutableDictionary<string, ParameterValue>.Empty))));
      Assert(compositor.Compose().pixels[0].color == 0x003400,
        "the replacement plan retained the old destination");

      var maskFrame = new DomeFrame(topology);
      maskFrame.pixels[0].color = 0xFFFFFF;
      maskFrame.pixels[0].hue = .75;
      compositor.Publish(new RenderPlan(ImmutableArray.Create(
        Compiled(
          new FakeRenderer("mask", maskFrame), DomeBlend.Desaturate, 1,
          ImmutableDictionary<string, ParameterValue>.Empty))));
      DomeFrame blankAdjustment = compositor.Compose();
      Assert(blankAdjustment.pixels[0].color == 0 &&
        blankAdjustment.pixels[0].a == 0 &&
        blankAdjustment.pixels[0].hue == 0,
        "the explicit destination retained stale mutable channels");

      compositor.Publish(RenderPlan.Empty);
      Assert(compositor.Compose() == null,
        "an empty plan did not preserve the hold-last-frame contract");
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

    private static T BuiltInOptions<T>(DomeLayerSettings layer)
      where T : class, ILayerRendererOptions {
      (LayerStackSnapshot snapshot, string error) =
        new LayerStackService().CreateSnapshot(new[] { layer });
      if (error != null) {
        throw new InvalidOperationException(error);
      }
      LayerDefinition definition = LayerCatalog.Default.Get(
        layer.VisualizerKey);
      ILayerRendererOptions options = definition.CompileOptions(
        snapshot.Layers[0].RendererParameters);
      return options as T ?? throw new InvalidOperationException(
        "Unexpected options type " + options.GetType().Name + ".");
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

    private static DomeTopology TwoPixelTopology() => new(new[] {
      new DomeTopologyPixel(0, 0, .45, .5),
      new DomeTopologyPixel(1, 0, .55, .5),
    });

    private static DomeTopology LinearTopology(int count) {
      var pixels = new DomeTopologyPixel[count];
      for (int i = 0; i < count; i++) {
        pixels[i] = new DomeTopologyPixel(0, i, .4 + i * .02, .5);
      }
      return new DomeTopology(pixels);
    }

    private static void AssertColors(
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

    private static void AssertClose(
      double expected, double actual, string message
    ) {
      Assert(Math.Abs(expected - actual) < 0.000000001,
        message + " expected " + expected + " but got " + actual);
    }

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

    private sealed class InlineGateway : ControlGateway {
      public void Post(Action mutation) => mutation();
      public Task InvokeAsync(Action mutation) {
        mutation();
        return Task.CompletedTask;
      }
    }

    private sealed class SwapSnapshotOperation : ICompositeOperation {
      public string Id { get; }
      public CompositeRequirements Requirements =>
        CompositeRequirements.ReadsDestination |
        CompositeRequirements.ReadsDestinationNeighbors;
      public DomeFrame SeenSnapshot { get; private set; }
      public int FirstSeen { get; private set; }
      public int SecondSeen { get; private set; }

      public SwapSnapshotOperation(string id) {
        this.Id = id;
      }

      public ICompositeOptions CompileOptions(
        ImmutableDictionary<string, ParameterValue> parameters
      ) => EmptyCompositeOptions.Instance;

      public void Execute(in DomeBlendContext context) {
        this.SeenSnapshot = context.Snapshot;
        Assert(this.SeenSnapshot != null, "spatial pass received no snapshot");
        this.FirstSeen = this.SeenSnapshot.pixels[0].color;
        this.SecondSeen = this.SeenSnapshot.pixels[1].color;
        context.Dest.pixels[0].color = this.SecondSeen;
        context.Dest.pixels[1].color = this.FirstSeen;
      }
    }
  }
}
