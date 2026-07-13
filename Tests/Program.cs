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
      Run("compositor preserves stack order and bottom behavior", StackOrder);
      Run("scratch copies only mutable channels", ScratchCopiesChannelsOnly);
      Run("operator creates independent duplicate renderers", DuplicateRenderers);
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

    private static void StackOrder() {
      DomeTopology topology = OnePixelTopology();
      var bottomFrame = new LEDDomeOutputBuffer(topology);
      bottomFrame.pixels[0].color = 0x200000;
      var topFrame = new LEDDomeOutputBuffer(topology);
      topFrame.pixels[0].color = 0x002000;
      var bottom = new FakeRenderer("bottom", bottomFrame);
      var top = new FakeRenderer("top", topFrame);
      var empty = ImmutableDictionary<string, ParameterValue>.Empty;
      var plan = new RenderPlan(ImmutableArray.Create(
        Compiled(bottom, DomeBlend.Multiply, 1, empty),
        Compiled(top, DomeBlend.Add, 0.5, empty)));
      var compositor = new DomeCompositor(
        () => new LEDDomeOutputBuffer(topology), elapsedSeconds: () => 0);
      compositor.Publish(plan);
      LEDDomeOutputBuffer result = compositor.Compose();
      Assert(result.pixels[0].color == 0x201000,
        "unexpected composite 0x" + result.pixels[0].color.ToString("X6"));
    }

    private static void ScratchCopiesChannelsOnly() {
      var a = new LEDDomeOutputPixel { strutIndex = 4, x = .25, y = .75 };
      a.color = 0x123456;
      var b = new LEDDomeOutputPixel { strutIndex = 9, x = .9, y = .1 };
      b.color = 0xFFFFFF;
      var source = new LEDDomeOutputBuffer(new[] { a });
      var target = new LEDDomeOutputBuffer(new[] { b });
      target.CopyFrom(source);
      Assert(target.pixels[0].color == 0x123456, "channels did not copy");
      Assert(target.pixels[0].strutIndex == 9 && target.pixels[0].x == .9,
        "topology metadata was copied");
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

    private static void ZeroOpacityIdentity() {
      DomeTopology topology = OnePixelTopology();
      foreach (DomeBlend operation in new[] {
        DomeBlend.Over, DomeBlend.Add, DomeBlend.Screen, DomeBlend.Lighten,
        DomeBlend.Multiply, DomeBlend.Desaturate, DomeBlend.Hue,
      }) {
        var dest = new LEDDomeOutputBuffer(topology);
        dest.pixels[0].color = 0x123456;
        dest.pixels[0].hue = .25;
        var source = new LEDDomeOutputBuffer(topology);
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
        snapshot, renderer, operation, operation.CompileOptions(parameters));
    }

    private static DomeLayerSettings Layer(string key, string id) => new() {
      InstanceId = id,
      VisualizerKey = key,
      BlendMode = DomeBlend.Add.Name,
      Opacity = 1,
      Enabled = true,
    };

    private static DomeTopology OnePixelTopology() => new(new[] {
      new LEDDomeOutputPixel { strutIndex = 0, strutLEDIndex = 0, x = .5, y = .5 },
    });

    private static void Assert(bool condition, string message) {
      if (!condition) {
        throw new InvalidOperationException(message);
      }
    }

    private sealed class FakeRenderer : ILayerRenderer {
      public string RendererId { get; }
      public LEDDomeOutputBuffer Frame { get; }
      public bool IsAvailable => true;
      public FakeRenderer(string id, LEDDomeOutputBuffer frame) {
        this.RendererId = id;
        this.Frame = frame;
      }
    }
  }
}
