using System;
using System.Collections.Immutable;
using Spectrum.Base;
using Spectrum.LEDs;
using static Spectrum.LayerPipeline.Tests.TestAssertions;

namespace Spectrum.LayerPipeline.Tests {

  public static class DomeOutputPublicationTests {
    public static void Register(Action<string, Action> run) {
      run("native simulator publication uses a replaceable frame mailbox",
        NativeFrameMailbox);
      run("browser simulator publication remains independent from native state",
        IndependentWebChannel);
      run("operator frames retain one accepted render-state generation",
        RenderStateCapturesOneGeneration);
      run("dome output settings reconcile at the output-owner boundary",
        OutputSettingsReconcileAtOwnerBoundary);
      run("dome render pipeline owns stable visualizer snapshots",
        RenderPipelineOwnsVisualizerSnapshots);
      run("dome render pipeline publishes before post-frame hue mutation",
        RenderPipelinePublishesBeforeHueMutation);
    }

    private static void NativeFrameMailbox() {
      var publisher = new DomeSimulatorPublisher {
        NativeHasConsumer = true,
      };

      publisher.PublishPixel(3, 4, 0x102030, simulationEnabled: true);
      Assert(publisher.NativeCommands.Count == 1,
        "native diagnostic pixel was not queued");

      DomeSimulatorFrameCapture capture =
        publisher.BeginFrame(3, simulationEnabled: true);
      Assert(capture.Enabled && capture.NativeFrame != null &&
          capture.WebFrame == null,
        "native frame capture did not follow consumer state");
      capture.SetColor(0, 0x112233);
      capture.SetColor(1, 0x445566);
      capture.SetColor(2, 0x778899);
      publisher.CompleteFrame(capture, simulationEnabled: true);

      Assert(publisher.NativeCommands.IsEmpty,
        "normal frame did not supersede older diagnostics");
      bool hadFrame = publisher.TryTakeNativeFrame(out int[]? frame);
      Assert(hadFrame && frame != null &&
          frame[0] == 0x112233 &&
          frame[1] == 0x445566 &&
          frame[2] == 0x778899,
        "native mailbox did not retain the completed frame");
      DomeSimulatorPublisher.ReturnFrame(frame);

      publisher.FlushFrame(simulationEnabled: true);
      Assert(publisher.NativeCommands.IsEmpty,
        "completed normal frame also published a redundant flush command");
      publisher.FlushFrame(simulationEnabled: true);
      Assert(publisher.NativeCommands.TryDequeue(out var flush) &&
          flush.isFlush,
        "diagnostic-only frame did not publish its flush command");

      DomeSimulatorFrameCapture disabled =
        publisher.BeginFrame(1, simulationEnabled: true);
      disabled.SetColor(0, 0x010101);
      publisher.CompleteFrame(disabled, simulationEnabled: false);
      Assert(!publisher.TryTakeNativeFrame(out _),
        "native frame survived simulation being disabled during capture");

      DomeSimulatorFrameCapture abandoned =
        publisher.BeginFrame(1, simulationEnabled: true);
      abandoned.SetColor(0, 0xABCDEF);
      publisher.CompleteFrame(abandoned, simulationEnabled: true);
      publisher.NativeHasConsumer = false;
      Assert(!publisher.TryTakeNativeFrame(out _),
        "closing the native consumer retained a pooled frame");
    }

    private static void IndependentWebChannel() {
      var publisher = new DomeSimulatorPublisher {
        WebHasConsumer = true,
      };

      publisher.PublishPixel(2, 5, 0x123456, simulationEnabled: false);
      Assert(publisher.NativeCommands.IsEmpty &&
          publisher.WebCommands.Count == 1,
        "browser diagnostics depended on native simulation state");

      DomeSimulatorFrameCapture capture =
        publisher.BeginFrame(2, simulationEnabled: false);
      Assert(capture.Enabled && capture.NativeFrame == null &&
          capture.WebFrame != null,
        "browser frame capture activated the native mailbox");
      capture.SetColor(0, 0x010203);
      capture.SetColor(1, 0xA0B0C0);
      publisher.CompleteFrame(capture, simulationEnabled: false);

      bool hadFrame = publisher.TryTakeWebFrame(out int[]? frame);
      Assert(publisher.WebCommands.IsEmpty &&
          hadFrame && frame != null &&
          frame[0] == 0x010203 &&
          frame[1] == 0xA0B0C0,
        "browser mailbox did not replace its diagnostic backlog");
      DomeSimulatorPublisher.ReturnFrame(frame);

      publisher.WebHasConsumer = false;
      Assert(!publisher.TryTakeWebFrame(out _) &&
          publisher.WebCommands.IsEmpty,
        "closing the browser consumer retained publication state");
    }

    private static void RenderStateCapturesOneGeneration() {
      var config = new global::Spectrum.SpectrumConfiguration();
      var beat = new BeatBroadcaster(config);
      DomeRuntimeFrameSnapshot runtime =
        DomeRuntimeFrameSnapshot.Empty with { Generation = 1 };
      DomeOutputSettingsSnapshot output =
        DomeOutputSettingsSnapshot.Empty with { Generation = 1 };
      var firstPlan = new RenderPlan(ImmutableArray<CompiledLayer>.Empty);
      DomeShowStateSnapshot firstShow =
        DomeShowStateSnapshot.Empty with { Generation = 1 };
      var state = new DomeRenderState(
        beat,
        new DomeRenderGeneration(firstPlan, firstShow),
        () => runtime,
        () => output);

      DomeShowStateSnapshot captured = state.BeginFrame();
      Assert(ReferenceEquals(captured, firstShow) &&
          ReferenceEquals(state.FrameGeneration.Plan, firstPlan) &&
          state.RuntimeSettings.Generation == 1 &&
          state.OutputSettings.Generation == 1,
        "frame capture did not retain its initial generation");

      var secondPlan = new RenderPlan(ImmutableArray<CompiledLayer>.Empty);
      DomeShowStateSnapshot secondShow =
        DomeShowStateSnapshot.Empty with { Generation = 2 };
      DomeShowStateSnapshot thirdShow =
        DomeShowStateSnapshot.Empty with { Generation = 3 };
      state.Publish(new DomeRenderGeneration(secondPlan, secondShow));
      state.PublishShowState(thirdShow);
      runtime = runtime with { Generation = 2 };
      output = output with { Generation = 2 };

      Assert(ReferenceEquals(state.FrameGeneration.Plan, firstPlan) &&
          ReferenceEquals(state.FrameGeneration.ShowState, firstShow) &&
          state.RuntimeSettings.Generation == 1 &&
          state.OutputSettings.Generation == 1,
        "an active frame mixed a newly published generation");
      Assert(ReferenceEquals(state.Plan, secondPlan) &&
          ReferenceEquals(state.ShowState, thirdShow),
        "show-state publication did not preserve the accepted plan");

      state.EndFrame();
      Assert(ReferenceEquals(state.FrameGeneration.Plan, secondPlan) &&
          ReferenceEquals(state.FrameGeneration.ShowState, thirdShow) &&
          state.RuntimeSettings.Generation == 2 &&
          state.OutputSettings.Generation == 2,
        "ending a frame did not expose the latest complete generation");
    }

    private static void OutputSettingsReconcileAtOwnerBoundary() {
      var config = new global::Spectrum.SpectrumConfiguration();
      var runtimeSettings = (IRuntimeSettingsConfiguration)config;
      var mapper = new DomeOutputMapper(
        DomeWiringLayout.MaxStripLength,
        () => DomeWiringLayout.StrutCount,
        DomeWiringLayout.GetLedCount,
        DomeWiringLayout.GetRawAddress);
      var coordinator = new DomeOutputSettingsCoordinator(
        config,
        runtimeSettings,
        mapper,
        new DomeOpcTransport(new RuntimeTelemetry()));
      DomeOutputMapping initialMapping = mapper.Current;
      int notifications = 0;
      coordinator.SettingsApplied += () => notifications++;

      config.domeBrightness = 0.25;
      coordinator.EnsureApplied();
      Assert(notifications == 0 &&
          ReferenceEquals(mapper.Current, initialMapping),
        "an unrelated setting rebuilt the output projection");

      config.ReplaceDomeCableMapping(
        new[] { 9, 8, 7, 6, 5, 4, 3, 2, 1, 0 });
      long mappingGeneration =
        runtimeSettings.DomeOutputSettingsSnapshot.MappingGeneration;
      Assert(coordinator.AppliedMappingGeneration != mappingGeneration &&
          ReferenceEquals(mapper.Current, initialMapping),
        "configuration publication rewrote output-owner state");
      coordinator.EnsureApplied();
      Assert(coordinator.AppliedMappingGeneration == mappingGeneration &&
          !ReferenceEquals(mapper.Current, initialMapping) &&
          notifications == 1,
        "the output owner did not apply the pending mapping generation");

      coordinator.SettingsApplied += () =>
        throw new InvalidOperationException("observer failure");
      config.domeBeagleboneOPCAddress = "127.0.0.1:7890";
      long transportGeneration =
        runtimeSettings.DomeOutputSettingsSnapshot.TransportGeneration;
      coordinator.EnsureApplied();
      Assert(coordinator.AppliedTransportGeneration == transportGeneration &&
          notifications == 2,
        "transport reconciliation did not isolate a settings observer");
    }

    private static void RenderPipelineOwnsVisualizerSnapshots() {
      DomeTopology topology = LayerPipelineTestFixtures.OnePixelTopology();
      var pipeline = new DomeRenderPipeline(
        () => new DomeFrame(topology),
        _ => { },
        null,
        null,
        () => 0,
        () => 1);
      var first = new TestVisualizer();
      var second = new TestVisualizer();

      pipeline.RegisterVisualizer(first);
      Visualizer[] initial = pipeline.GetVisualizers();
      Assert(initial.Length == 1 &&
          ReferenceEquals(initial[0], first) &&
          ReferenceEquals(initial, pipeline.GetVisualizers()),
        "unchanged visualizer registration rebuilt its cached snapshot");

      pipeline.RegisterVisualizer(second);
      Visualizer[] expanded = pipeline.GetVisualizers();
      Assert(!ReferenceEquals(initial, expanded) &&
          expanded.Length == 2 &&
          ReferenceEquals(expanded[0], first) &&
          ReferenceEquals(expanded[1], second),
        "visualizer registration did not publish an ordered snapshot");

      pipeline.UnregisterVisualizer(first);
      Visualizer[] reduced = pipeline.GetVisualizers();
      Assert(!ReferenceEquals(expanded, reduced) &&
          reduced.Length == 1 &&
          ReferenceEquals(reduced[0], second),
        "visualizer removal did not invalidate the published snapshot");
    }

    private static void RenderPipelinePublishesBeforeHueMutation() {
      DomeTopology topology = LayerPipelineTestFixtures.OnePixelTopology();
      var renderer = new LayerPipelineTestFixtures.FakeRenderer(
        "post-frame-hue",
        new DomeFrame(topology));
      renderer.Frame.pixels[0].color = 0xFF0000;
      var plan = new RenderPlan(ImmutableArray.Create(
        LayerPipelineTestFixtures.Compiled(
          renderer,
          DomeBlend.Add,
          1,
          ImmutableDictionary<string, ParameterValue>.Empty)));
      int publishedSourceColor = -1;
      var pipeline = new DomeRenderPipeline(
        () => new DomeFrame(topology),
        _ => publishedSourceColor = renderer.Frame.pixels[0].color,
        null,
        null,
        () => 0,
        () => 1);
      var generation = new DomeRenderGeneration(
        plan,
        DomeShowStateSnapshot.Empty with { GlobalHueSpeed = 1 });

      pipeline.Render(generation);

      Assert(publishedSourceColor == 0xFF0000,
        "persistent layer hue changed before frame publication");
      Assert(renderer.Frame.pixels[0].color != 0xFF0000,
        "post-frame hue mutation did not advance the persistent layer");
    }

    private sealed class TestVisualizer : Visualizer {
      public int Priority => 0;
      public bool Enabled { get; set; }
      public void Visualize() { }
      public Input[] GetInputs() => Array.Empty<Input>();
    }
  }
}
