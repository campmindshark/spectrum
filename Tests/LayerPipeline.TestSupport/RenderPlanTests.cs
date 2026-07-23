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

  public static class RenderPlanTests {
    public static void Register(Action<string, Action> run) {
      run(nameof(PlanSchedulesInstances), PlanSchedulesInstances);
      run(nameof(SceneRecallRetainsState), SceneRecallRetainsState);
      run(nameof(DuplicateCommandsNeedIds), DuplicateCommandsNeedIds);
      run(nameof(PlanReplacement), PlanReplacement);
    }
    private static void PlanSchedulesInstances() {
      DomeLayerSettings disabled = Layer("background", "background-off");
      disabled.Enabled = false;
      var config = ConfigurationWithLayers(
        disabled, Layer("background", "background-on"));
      var runtime = new global::Spectrum.Operator(config);
      RenderPlan plan = runtime.DomeOutput.RenderPlan;
      Assert(plan.Layers.Length == 1, "disabled layer entered the plan");
      Assert(plan.Layers[0].Snapshot.Id.Value == "background-on",
        "the enabled instance was not scheduled");
    }

    private static void SceneRecallRetainsState() {
      var config = ConfigurationWithLayers(
        Layer("wave", "scene-wave-a"),
        Layer("wave", "scene-wave-b"));
      var scenes = new SceneService(config, DomeLayerCatalog.Metadata);
      (bool saved, string? saveError) = scenes.Save("duplicates");
      Assert(saved, saveError);

      int created = 0;
      DomeTopology topology = OnePixelTopology();
      var catalog = new LayerCatalog(new[] {
        new LayerDefinition(
          "wave", "Wave",
          runtime => {
            created++;
            return new FakeRenderer("wave", new DomeFrame(topology));
          },
          Array.Empty<DomeLayerParam>(),
          values => EmptyLayerRendererOptions.Instance),
        new LayerDefinition(
          "background", "Background",
          runtime => new FakeRenderer(
            "background", new DomeFrame(topology)),
          Array.Empty<DomeLayerParam>(),
          values => EmptyLayerRendererOptions.Instance)
      });
      var compiler = new RenderPlanCompiler();
      var store = new LayerRendererStore(catalog);
      LayerStackSnapshot initial =
        ((ILayerStackSnapshotSource)config).DomeLayerStackSnapshot;
      RenderPlan first = Compile(compiler, store, initial);
      ILayerRenderer rendererA = first.Layers[0].Renderer;
      ILayerRenderer rendererB = first.Layers[1].Renderer;
      Assert(!ReferenceEquals(rendererA, rendererB),
        "duplicate instances shared renderer state");
      rendererA.Frame.pixels[0].color = 0x110000;
      rendererB.Frame.pixels[0].color = 0x002200;

      var reorderedSnapshot = new LayerStackSnapshot(ImmutableArray.Create(
        initial.Layers[1], initial.Layers[0]));
      RenderPlan reordered = Compile(compiler, store, reorderedSnapshot);
      Assert(ReferenceEquals(reordered.Layers[0].Renderer, rendererB) &&
        ReferenceEquals(reordered.Layers[1].Renderer, rendererA),
        "reordering changed instance identity");

      config.ReplaceDomeLayerStack(new List<DomeLayerSettings> {
        Layer("background", "temporary-background"),
      });
      Compile(
        compiler, store,
        ((ILayerStackSnapshotSource)config).DomeLayerStackSnapshot);

      (bool applied, string? applyError) = scenes.Apply("duplicates");
      Assert(applied, applyError);
      RenderPlan recalled = Compile(
        compiler, store,
        ((ILayerStackSnapshotSource)config).DomeLayerStackSnapshot);

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
      var config = ConfigurationWithLayers(
        Layer("wave", "command-wave-a"),
        Layer("wave", "command-wave-b"));
      var controller = new global::Spectrum.Web.LayersController(
        new InlineGateway(), config);

      (bool ambiguousFire, string? fireError) =
        controller.FireAsync("wave").GetAwaiter().GetResult();
      Assert(!ambiguousFire &&
          fireError?.Contains("unknown layer instance") == true,
        "renderer-key fire was accepted");
      Assert(config.domeLayerFireCounters.Count == 0,
        "ambiguous fire changed a counter");

      (bool fired, string? targetedFireError) =
        controller.FireAsync("command-wave-b").GetAwaiter().GetResult();
      Assert(fired, targetedFireError);
      Assert(config.domeLayerFireCounters.Count == 1 &&
        config.domeLayerFireCounters["command-wave-b"] == 1,
        "targeted fire reached the wrong duplicate");

      (bool ambiguousClear, string? clearError) =
        controller.ClearAsync("wave").GetAwaiter().GetResult();
      Assert(!ambiguousClear &&
          clearError?.Contains("unknown layer instance") == true,
        "renderer-key clear was accepted");
      Assert(config.domeLayerClearCounters.Count == 0,
        "ambiguous clear changed a counter");

      (bool cleared, string? targetedClearError) =
        controller.ClearAsync("command-wave-a").GetAwaiter().GetResult();
      Assert(cleared, targetedClearError);
      Assert(config.domeLayerClearCounters.Count == 1 &&
        config.domeLayerClearCounters["command-wave-a"] == 1,
        "targeted clear reached the wrong duplicate");
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
      Assert(RequireFrame(
          compositor.Compose(), "initial plan frame").pixels[0].color ==
          0x120000,
        "the first plan did not render");

      compositor.Publish(new RenderPlan(ImmutableArray.Create(
        Compiled(
          new FakeRenderer("second", secondFrame), DomeBlend.Add, 1,
          ImmutableDictionary<string, ParameterValue>.Empty))));
      Assert(RequireFrame(
          compositor.Compose(), "replacement plan frame").pixels[0].color ==
          0x003400,
        "the replacement plan retained the old destination");

      var maskFrame = new DomeFrame(topology);
      maskFrame.pixels[0].color = 0xFFFFFF;
      maskFrame.pixels[0].hue = .75;
      compositor.Publish(new RenderPlan(ImmutableArray.Create(
        Compiled(
          new FakeRenderer("mask", maskFrame), DomeBlend.Desaturate, 1,
          ImmutableDictionary<string, ParameterValue>.Empty))));
      DomeFrame blankAdjustment = RequireFrame(
        compositor.Compose(), "blank adjustment frame");
      Assert(blankAdjustment.pixels[0].color == 0 &&
        blankAdjustment.pixels[0].a == 0 &&
        blankAdjustment.pixels[0].hue == 0,
        "the explicit destination retained stale mutable channels");

      compositor.Publish(RenderPlan.Empty);
      Assert(compositor.Compose() == null,
        "an empty plan did not preserve the hold-last-frame contract");
    }

  }
}