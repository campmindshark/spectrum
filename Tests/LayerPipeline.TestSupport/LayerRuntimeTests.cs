using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Visualizers;
using static Spectrum.LayerPipeline.Tests.LayerPipelineTestFixtures;
using static Spectrum.LayerPipeline.Tests.TestAssertions;

namespace Spectrum.LayerPipeline.Tests {

  public static class LayerRuntimeTests {
    public static void Register(Action<string, Action> run) {
      run(nameof(RuntimeUpdatesInPlace), RuntimeUpdatesInPlace);
      run(nameof(RuntimeOptionsSwap), RuntimeOptionsSwap);
      run(nameof(RendererStoreLifecycle), RendererStoreLifecycle);
      run(nameof(RendererStoreRollsBackGeneration), RendererStoreRollsBackGeneration);
      run(nameof(TypedOptionsPreserveNumericCasts), TypedOptionsPreserveNumericCasts);
      run(nameof(EarthTextureFollowsSpotlight), EarthTextureFollowsSpotlight);
      run(nameof(PlanFreezesRendererInputs), PlanFreezesRendererInputs);
    }
    private static void RuntimeUpdatesInPlace() {
      LayerSnapshot first = SnapshotWithParameter("runtime-1", "speed", 1);
      LayerRendererRuntime? captured = null;
      var topology = OnePixelTopology();
      var catalog = new LayerCatalog(new[] {
        new LayerDefinition(
          "test", "Test",
          runtime => {
            captured = runtime;
            return new FakeRenderer(
              "test", new DomeFrame(topology));
          },
          new[] {
            new DomeLayerParam {
              Key = "speed", Type = DomeLayerParamType.Double,
              Min = 0, Max = 10, Default = 0,
            },
          },
          values => new ScalarOptions(values["speed"].Value))
      });
      var compiler = new RenderPlanCompiler();
      var store = new LayerRendererStore(catalog);
      RenderPlan initial = Compile(
        compiler, store,
        new LayerStackSnapshot(ImmutableArray.Create(first)));
      LayerSnapshot second = SnapshotWithParameter("runtime-1", "speed", 2);
      RenderPlan updated = Compile(
        compiler, store,
        new LayerStackSnapshot(ImmutableArray.Create(second)));

      Assert(ReferenceEquals(
        initial.Layers[0].Renderer, updated.Layers[0].Renderer),
        "renderer instance was recreated");
      Assert(captured != null &&
          captured.GetOptions<ScalarOptions>().Value == 2,
        "renderer runtime kept stale parameters");
    }

    private static void RuntimeOptionsSwap() {
      DomeLayerSettings first = Layer("wave", "runtime-wave");
      first.RendererParams = new Dictionary<string, double> { ["speed"] = .25 };
      (LayerStackSnapshot? initial, string? initialError) =
        new LayerStackService(DomeLayerCatalog.Metadata).CreateSnapshot(new[] { first });
      Assert(initial != null && initialError == null, initialError);
      LayerDefinition? definition = DomeLayerCatalog.Metadata.Get("wave");
      Assert(definition != null, "Wave is missing from the layer catalog");
      var runtime = new LayerRendererRuntime(
        initial.Layers[0], definition.CompileOptions);
      WaveLayerOptions original = runtime.GetOptions<WaveLayerOptions>();
      Assert(original.Speed == .25, "initial typed option missing");

      DomeLayerSettings second = Layer("wave", "runtime-wave");
      second.RendererParams = new Dictionary<string, double> { ["speed"] = 1.25 };
      (LayerStackSnapshot? changed, string? changedError) =
        new LayerStackService(DomeLayerCatalog.Metadata).CreateSnapshot(new[] { second });
      Assert(changed != null && changedError == null, changedError);
      runtime.Publish(changed.Layers[0]);
      WaveLayerOptions replacement = runtime.GetOptions<WaveLayerOptions>();
      Assert(replacement.Speed == 1.25,
        "replacement typed option was not published");
      Assert(original.Speed == .25,
        "the original typed options were mutated");
      Assert(initial.Layers[0].RendererParameters["speed"].Value == .25,
        "the original snapshot was mutated");
    }

    private static void RendererStoreLifecycle() {
      var created = new List<DisposableFakeRenderer>();
      LayerDefinition Definition(string id) => new(
        id, id,
        runtime => {
          var renderer = new DisposableFakeRenderer(id, OnePixelTopology());
          created.Add(renderer);
          return renderer;
        },
        Array.Empty<DomeLayerParam>(),
        values => EmptyLayerRendererOptions.Instance);
      var store = new LayerRendererStore(new LayerCatalog(new[] {
        Definition("first"), Definition("second"),
      }));

      LayerRendererBinding initial = store.Resolve(
        SnapshotForRenderer("lifecycle", "first"));
      LayerRendererBinding retained = store.Resolve(
        SnapshotForRenderer("lifecycle", "first"));
      Assert(initial.Created && !retained.Created,
        "matching instance was not retained");
      Assert(ReferenceEquals(initial.Renderer, retained.Renderer),
        "matching instance changed renderer");

      LayerRendererBinding replaced = store.Resolve(
        SnapshotForRenderer("lifecycle", "second"));
      Assert(replaced.Created, "renderer-kind change was not created");
      Assert(ReferenceEquals(initial.Renderer, replaced.ReplacedRenderer),
        "replaced renderer was not returned to the owner");
      ((IDisposable)replaced.ReplacedRenderer).Dispose();
      Assert(created[0].Disposed, "replaced renderer was not disposable");

      IReadOnlyList<ILayerRenderer> evicted = store.Retain(
        new HashSet<LayerInstanceId>());
      Assert(evicted.Count == 1 &&
        ReferenceEquals(evicted[0], replaced.Renderer),
        "unretained renderer was not evicted");
      ((IDisposable)evicted[0]).Dispose();
      Assert(created[1].Disposed, "evicted renderer was not disposable");
    }

    private static void RendererStoreRollsBackGeneration() {
      var created = new List<DisposableFakeRenderer>();
      LayerRendererRuntime? retainedRuntime = null;
      var catalog = new LayerCatalog(new[] {
        new LayerDefinition(
          "test", "Test",
          runtime => {
            retainedRuntime ??= runtime;
            var renderer = new DisposableFakeRenderer(
              "test", OnePixelTopology());
            created.Add(renderer);
            return renderer;
          },
          Array.Empty<DomeLayerParam>(),
          values => new ScalarOptions(
            values.TryGetValue("value", out ParameterValue value)
              ? value.Value : 0)),
      });
      var store = new LayerRendererStore(catalog);
      LayerSnapshot initialLayer = SnapshotWithParameter(
        "transaction-retained", "value", 1);
      var initialStack = new LayerStackSnapshot(
        ImmutableArray.Create(initialLayer));
      using (LayerRendererStore.Transaction initial =
          store.Prepare(initialStack)) {
        initial.Commit();
      }
      ILayerRenderer? liveRenderer = store.Get(initialLayer);
      Assert(retainedRuntime != null &&
          retainedRuntime.GetOptions<ScalarOptions>().Value == 1,
        "initial renderer options were not committed");

      LayerSnapshot changedLayer = SnapshotWithParameter(
        "transaction-retained", "value", 2);
      LayerSnapshot newLayer = SnapshotWithParameter(
        "transaction-new", "value", 3);
      using (LayerRendererStore.Transaction candidate = store.Prepare(
          new LayerStackSnapshot(
            ImmutableArray.Create(changedLayer, newLayer)))) {
        Assert(retainedRuntime.GetOptions<ScalarOptions>().Value == 1,
          "candidate options leaked into the live runtime before commit");
        Assert(candidate.Bindings.Count == 2 &&
            candidate.Bindings[1].Created,
          "candidate generation did not construct its new renderer");
        // Simulate a later operation-compiler failure by leaving the complete
        // renderer candidate uncommitted.
      }

      Assert(ReferenceEquals(store.Get(initialLayer), liveRenderer) &&
          retainedRuntime.GetOptions<ScalarOptions>().Value == 1,
        "rolling back a candidate changed the live renderer generation");
      Assert(created.Count == 2 && created[1].Disposed,
        "rolling back did not dispose the unpublished renderer");
    }

    private static void TypedOptionsPreserveNumericCasts() {
      DomeLayerSettings volume = Layer("volume", "typed-volume");
      volume.RendererParams = new Dictionary<string, double> {
        ["animationSize"] = 3.75,
      };
      Assert(BuiltInOptions<VolumeLayerOptions>(volume).AnimationSize == 3,
        "volume animation size no longer truncates");

      DomeLayerSettings points = Layer("point-cloud", "typed-points");
      points.RendererParams = new Dictionary<string, double> { ["count"] = 47.75 };
      Assert(BuiltInOptions<PointCloudLayerOptions>(points).Count == 47,
        "point count no longer truncates");

      DomeLayerSettings noise = Layer("noise-cloud", "typed-noise");
      noise.RendererParams = new Dictionary<string, double> { ["octaves"] = 2.75 };
      Assert(BuiltInOptions<NoiseCloudLayerOptions>(noise).Octaves == 2,
        "noise octave count no longer truncates");

      DomeLayerSettings sparkler = Layer("sparkler", "typed-sparkler");
      sparkler.RendererParams = new Dictionary<string, double> {
        ["emissionRate"] = 12.5,
      };
      AssertClose(
        12.5, BuiltInOptions<SparklerLayerOptions>(sparkler).EmissionRate,
        "sparkler emission rate did not compile into renderer options");

      DomeLayerSettings vortex = Layer("vortex", "typed-vortex");
      vortex.RendererParams = new Dictionary<string, double> {
        ["audioBrightness"] = 1,
        ["audioSpeed"] = 1,
      };
      VortexLayerOptions vortexOptions =
        BuiltInOptions<VortexLayerOptions>(vortex);
      Assert(vortexOptions.AudioBrightness && vortexOptions.BeatSpeed,
        "vortex audio/beat hooks did not compile into renderer options");
    }

    private static void EarthTextureFollowsSpotlight() {
      LayerDefinition? definition = DomeLayerCatalog.Metadata.Get("earth");
      Assert(definition != null && definition.DisplayName == "Earth",
        "Earth was not registered");

      DomeLayerSettings layer = Layer("earth", "typed-earth");
      layer.RendererParams = new Dictionary<string, double> {
        ["spinSpeed"] = -0.125,
      };
      EarthLayerOptions options = BuiltInOptions<EarthLayerOptions>(layer);
      AssertClose(-0.125, options.SpinSpeed,
        "Earth spin speed did not compile into renderer options");

      LEDDomeEarthVisualizer.TextureCoordinates(
        OrientationCenter.Spot, Quaternion.Identity, 0,
        out _, out double northV);
      LEDDomeEarthVisualizer.TextureCoordinates(
        OrientationCenter.NegSpot, Quaternion.Identity, 0,
        out _, out double southV);
      AssertClose(0, northV, "Spot did not map to the north texture pole");
      AssertClose(1, southV,
        "NegSpot did not map to the south texture pole");

      LEDDomeEarthVisualizer.TextureCoordinates(
        Vector3.UnitZ, Quaternion.Identity, 0,
        out double zeroU, out double equatorV);
      LEDDomeEarthVisualizer.TextureCoordinates(
        Vector3.UnitZ, Quaternion.Identity, 0.25,
        out double spunU, out double spunV);
      AssertClose(0.5, zeroU, "the zero meridian was not centered");
      AssertClose(0.5, equatorV, "the zero meridian left the equator");
      AssertClose(0.25, spunU,
        "a quarter spin did not advance longitude by a quarter turn");
      AssertClose(equatorV, spunV, "spinning changed latitude");

      Quaternion orientation = Quaternion.CreateFromAxisAngle(
        Vector3.UnitZ, (float)(Math.PI / 2));
      Vector3 aimedNorth = Vector3.Transform(
        OrientationCenter.Spot, Quaternion.Conjugate(orientation));
      LEDDomeEarthVisualizer.TextureCoordinates(
        aimedNorth, orientation, 0, out _, out double aimedV);
      Assert(aimedV < 0.001,
        "the orientation aim did not move the globe's north pole");

      using Stream? texture = typeof(LEDDomeEarthVisualizer).Assembly
        .GetManifestResourceStream(
          LEDDomeEarthVisualizer.TextureResourceName);
      Assert(texture != null && texture.Length > 0,
        "the Earth texture was not embedded in the application");

      var config = ConfigurationWithLayers(layer);
      var runtime = new global::Spectrum.Operator(config);
      DomeLayerVisualizer? earth = null;
      foreach (Visualizer visualizer in runtime.DomeOutput.GetVisualizers()) {
        if (visualizer is DomeLayerVisualizer candidate &&
            candidate.LayerKey == "earth") {
          earth = candidate;
          break;
        }
      }
      Assert(earth != null && earth.GetInputs().Length == 1 &&
        ReferenceEquals(earth.GetInputs()[0], runtime.OrientationInput),
        "Earth is not bound exclusively to OrientationInput");
      ((Visualizer)earth).Visualize();
      Assert(earth.LayerBuffer.pixels[0].a == 1,
        "Earth did not paint its upper-hemisphere texture");
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
          runtime => new FakeRenderer(
            "test", new DomeFrame(topology), declared),
          new[] {
            new DomeLayerParam {
              Key = "speed", Type = DomeLayerParamType.Double,
              Min = 0, Max = 10, Default = 0,
            },
          },
          values => new ScalarOptions(values["speed"].Value))
      });
      var compiler = new RenderPlanCompiler();
      var store = new LayerRendererStore(catalog);
      RenderPlan plan = Compile(
        compiler, store,
        new LayerStackSnapshot(ImmutableArray.Create(snapshot)));

      declared[0] = null!;
      Assert(plan.Layers[0].RequiredInputs.Length == 2,
        "duplicate input requirements survived compilation");
      Assert(ReferenceEquals(plan.Layers[0].RequiredInputs[0], first) &&
        ReferenceEquals(plan.Layers[0].RequiredInputs[1], second),
        "the plan retained the renderer's mutable input array");
    }

  }
}