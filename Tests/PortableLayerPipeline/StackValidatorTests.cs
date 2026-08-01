using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using Spectrum.Base;

namespace Spectrum.LayerPipeline.Tests {

  [TestClass]
  [DoNotParallelize]
  public sealed class StackValidatorTests {


    [TestMethod]
    public void InvalidIdentityAndSchema() {
      AssertRejected(new[] {
        ValidLayer("same"),
        ValidLayer("same"),
      }, "duplicate layer instance id");

      DomeLayerSettings unknownVisualizer = ValidLayer("unknown-renderer");
      unknownVisualizer.VisualizerKey = "not-a-visualizer";
      AssertRejected(new[] { unknownVisualizer }, "unknown visualizer key");

      DomeLayerSettings unknownBlend = ValidLayer("unknown-blend");
      unknownBlend.BlendMode = "not-a-blend";
      AssertRejected(new[] { unknownBlend }, "unknown blend mode");

      AssertRejected(
        new DomeLayerSettings[] { null! }, "needs a visualizerKey");
    }

    [TestMethod]
    public void SizeAndOpacityBoundaries() {
      foreach (double opacity in new[] { double.NaN, -0.001, 1.001 }) {
        DomeLayerSettings layer = ValidLayer("opacity-" + opacity);
        layer.Opacity = opacity;
        AssertRejected(new[] { layer }, "opacity");
      }

      DomeLayerSettings transparent = ValidLayer("transparent");
      transparent.Opacity = 0;
      DomeLayerSettings opaque = ValidLayer("opaque");
      opaque.Opacity = 1;
      (List<DomeLayerSettings>? boundaryStack, string? boundaryError) =
        StackValidator.Validate(
          new[] { transparent, opaque }, DomeLayerCatalog.Metadata);
      Assert.IsTrue(boundaryError == null && boundaryStack?.Count == 2,
        "valid opacity boundaries were rejected: " + boundaryError);

      var oversized = new List<DomeLayerSettings>();
      for (int i = 0; i <= StackValidator.MaxLayers; i++) {
        oversized.Add(ValidLayer("layer-" + i));
      }
      AssertRejected(oversized, "too many layers");
    }

    [TestMethod]
    public void ValidatedStackOwnership() {
      DomeLayerSettings input = ValidLayer("owned");
      input.Notes = new string('n', StackValidator.MaxNotesLength + 10);
      var source = new List<DomeLayerSettings> { input };

      (List<DomeLayerSettings>? validated, string? error) =
        StackValidator.Validate(source, DomeLayerCatalog.Metadata);
      Assert.IsTrue(validated != null && error == null, error);
      DomeLayerSettings output = validated[0];

      string inputNotes = Require(input.Notes, "input notes");
      string outputNotes = Require(output.Notes, "validated notes");
      Dictionary<string, double> inputRendererParams = Require(
        input.RendererParams, "input renderer parameters");
      Dictionary<string, double> outputRendererParams = Require(
        output.RendererParams, "validated renderer parameters");
      Dictionary<string, double> inputOperationParams = Require(
        input.OperationParams, "input operation parameters");
      Dictionary<string, double> outputOperationParams = Require(
        output.OperationParams, "validated operation parameters");

      Assert.IsTrue(!ReferenceEquals(source, validated) &&
        !ReferenceEquals(input, output),
        "validator returned caller-owned stack objects");
      Assert.IsTrue(!ReferenceEquals(inputRendererParams, outputRendererParams) &&
        !ReferenceEquals(inputOperationParams, outputOperationParams),
        "validator returned caller-owned parameter dictionaries");
      Assert.IsTrue(outputNotes.Length == StackValidator.MaxNotesLength &&
        inputNotes.Length == StackValidator.MaxNotesLength + 10,
        "notes were not normalized without mutating the input");

      inputRendererParams["color"] = 0;
      Assert.IsTrue(outputRendererParams["color"] == 0x123456,
        "validated renderer parameters changed with their source");
      outputOperationParams["offset"] = 0;
      Assert.IsTrue(inputOperationParams["offset"] == 0.045,
        "source operation parameters changed with validated output");
    }

    [TestMethod]
    public void SceneApplyIsAtomic() {
      var liveLayer = ValidLayer("live");
      var liveStack = new List<DomeLayerSettings> { liveLayer };
      var config = new global::Spectrum.SpectrumConfiguration {
        domeGlobalFadeSpeed = 0.25,
        domeGlobalHueSpeed = 0.5,
      };
      config.ReplaceDomeLayerStack(liveStack);
      config.ReplaceDomePalettes(new List<DomePalette> {
        new DomePalette {
          Name = "Live",
          Colors = new[] { new LEDColor(0x112233) },
        },
      });

      DomeLayerSettings incompleteFirstLayer = ValidLayer(null);
      DomeLayerSettings invalidSecondLayer = ValidLayer("invalid");
      invalidSecondLayer.VisualizerKey = "not-a-visualizer";
      config.ReplaceDomeScenes(new List<DomeScene> {
        new DomeScene {
          Name = "Broken",
          Layers = new List<DomeLayerSettings> {
            incompleteFirstLayer,
            invalidSecondLayer,
          },
          GlobalFadeSpeed = 0.75,
          GlobalHueSpeed = 1.5,
        },
      });

      (bool applied, string? error) = new SceneService(
        config, DomeLayerCatalog.Metadata).Apply("Broken");
      Assert.IsTrue(!applied && error != null,
        "invalid scene was accepted");
      Assert.IsTrue(config.domeLayerStack.Length == 1 &&
        config.domeLayerStack[0].InstanceId == liveLayer.InstanceId,
        "invalid scene partially replaced the live stack");
      Assert.IsTrue(config.domeGlobalFadeSpeed == 0.25 &&
        config.domeGlobalHueSpeed == 0.5,
        "invalid scene partially replaced global settings");
      Assert.IsTrue(config.domePalettes[0].GetSingleColor(0) == 0x112233,
        "invalid scene partially replaced the palette");
      Assert.IsTrue(incompleteFirstLayer.InstanceId == null,
        "failed validation mutated an earlier scene layer");
    }

    private static DomeLayerSettings ValidLayer(string? instanceId) =>
      new DomeLayerSettings {
        InstanceId = instanceId,
        VisualizerKey = "background",
        BlendMode = DomeBlend.ChromaticFringe.Id,
        Opacity = 0.5,
        Enabled = true,
        RendererParams = new Dictionary<string, double> {
          ["color"] = 0x123456,
        },
        OperationParams = new Dictionary<string, double> {
          ["offset"] = 0.045,
        },
      };

    private static void AssertRejected(
      IReadOnlyList<DomeLayerSettings> input, string expectedError
    ) {
      (List<DomeLayerSettings>? stack, string? error) =
        StackValidator.Validate(input, DomeLayerCatalog.Metadata);
      Assert.IsTrue(stack == null, "invalid stack produced normalized output");
      Assert.IsTrue(error != null && error.Contains(
          expectedError, StringComparison.OrdinalIgnoreCase),
        "expected error containing '" + expectedError + "', got '" + error + "'");
    }

    private static T Require<T>(T? value, string name) where T : class =>
      value ?? throw new InvalidOperationException(name + " is missing");

  }
}
