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

  public static class LayerPipelineCoreTests {
    public static void Register(Action<string, Action> run) {
      run(nameof(StackOrder), StackOrder);
      run(nameof(ScratchCopiesChannelsOnly), ScratchCopiesChannelsOnly);
      run(nameof(FramesRequireTopology), FramesRequireTopology);
      run(nameof(OperatorUsesInjectedPlatformInputs), OperatorUsesInjectedPlatformInputs);
      run(nameof(DuplicateRenderers), DuplicateRenderers);
      run(nameof(RebootNotificationsAreUnlocked), RebootNotificationsAreUnlocked);
      run(nameof(LayerRenderersAvoidConfiguration), LayerRenderersAvoidConfiguration);
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
      DomeFrame? result = compositor.Compose();
      Assert(result != null && result.pixels[0].color == 0x001000,
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

    private static void OperatorUsesInjectedPlatformInputs() {
      var config = ConfigurationWithLayers(
        Layer("volume", "injected-audio"));
      var factory = new FakeSpectrumInputFactory();
      var runtime = new global::Spectrum.Operator(
        config, new InlineGateway(), factory);

      Assert(ReferenceEquals(runtime.AudioInput, factory.Audio) &&
          ReferenceEquals(runtime.MidiInput, factory.Midi) &&
          ReferenceEquals(runtime.MidiLog, factory.Midi.MidiLog),
        "operator replaced an injected platform input");
      DomeLayerVisualizer volume = runtime.DomeOutput.GetVisualizers()
        .OfType<DomeLayerVisualizer>()
        .Single(layer => layer.LayerKey == "volume");
      Assert(volume.GetInputs().Length == 1 &&
          ReferenceEquals(volume.GetInputs()[0], factory.Audio),
        "renderer catalog did not consume the portable audio contract");
    }

    private static void DuplicateRenderers() {
      var config = ConfigurationWithLayers(
        Layer("background", "background-a"),
        Layer("background", "background-b"));
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

    private static void RebootNotificationsAreUnlocked() {
      var config = new global::Spectrum.SpectrumConfiguration();
      var runtime = new global::Spectrum.Operator(config);
      runtime.Enabled = true;
      int notifications = 0;
      Action<bool> handler = enabled => {
        notifications++;
        Task<bool> read = Task.Run(() => runtime.Enabled);
        Assert(
          read.Wait(TimeSpan.FromSeconds(1)),
          "EnabledChanged fired while the renderer lock was held");
      };
      try {
        runtime.EnabledChanged += handler;
        runtime.Reboot();
        runtime.EnabledChanged -= handler;
        Assert(notifications == 2,
          "reboot did not publish both enabled transitions");
      } finally {
        runtime.EnabledChanged -= handler;
        runtime.Enabled = false;
      }
    }

    private static void LayerRenderersAvoidConfiguration() {
      Type layerType = typeof(DomeLayerVisualizer);
      Type configurationType = typeof(Configuration);
      Type outputType = typeof(LEDDomeOutput);
      Type renderContextType = typeof(DomeRenderContext);
      foreach (Type type in
          typeof(global::Spectrum.Operator).Assembly.GetTypes()) {
        if (type.IsInterface || !layerType.IsAssignableFrom(type)) {
          continue;
        }
        bool receivesRenderContext = false;
        foreach (var constructor in type.GetConstructors(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic)) {
          foreach (var parameter in constructor.GetParameters()) {
            Assert(parameter.ParameterType != configurationType,
              type.Name + " still receives persisted Configuration");
            Assert(parameter.ParameterType != outputType,
              type.Name + " still receives the concrete LED output");
            receivesRenderContext |=
              parameter.ParameterType == renderContextType;
          }
        }
        Assert(receivesRenderContext,
          type.Name + " does not receive the narrow render context");
      }
    }

  }
}