using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Visualizers;
using static Spectrum.LayerPipeline.Tests.LayerPipelineTestFixtures;

namespace Spectrum.LayerPipeline.Tests {

  [TestClass]
  [DoNotParallelize]
  public sealed class LayerCatalogTests {

    [TestMethod]
    public void CatalogIsUnique() {
      var ids = new HashSet<string>();
      foreach (LayerDefinition definition in DomeLayerCatalog.Metadata.Definitions) {
        Assert.IsTrue(ids.Add(definition.Id), "duplicate " + definition.Id);
        Assert.IsTrue(definition.CompileOptions != null,
          "missing options compiler for " + definition.Id);
        var keys = new HashSet<string>();
        foreach (DomeLayerParam parameter in definition.Parameters) {
          Assert.IsTrue(keys.Add(parameter.Key), "duplicate parameter " + parameter.Key);
        }
        (LayerStackSnapshot? snapshot, string? error) =
          new LayerStackService(DomeLayerCatalog.Metadata).CreateSnapshot(new[] {
            Layer(definition.Id, "options-" + definition.Id),
          });
        Assert.IsTrue(snapshot != null && error == null, error);
        ILayerRendererOptions options = definition.CompileOptions(
          snapshot.Layers[0].RendererParameters);
        Assert.IsTrue(options != null, "null options for " + definition.Id);
      }
    }

    [TestMethod]
    public void TunnelParametersCompile() {
      LayerDefinition? definition = DomeLayerCatalog.Metadata.Get("tunnel");
      Assert.IsTrue(definition != null && definition.DisplayName == "Tunnel",
        "Tunnel is missing from the layer catalog");

      TunnelLayerOptions defaults = BuiltInOptions<TunnelLayerOptions>(
        Layer("tunnel", "tunnel-defaults"));
      Assert.IsTrue(defaults.RingCount == 12, "unexpected Tunnel ring count");
      Assert.IsTrue(Math.Abs(defaults.Speed - 0.18) < 1e-9,
        "unexpected Tunnel speed");
      Assert.IsTrue(Math.Abs(defaults.Thickness - 0.025) < 1e-9,
        "unexpected Tunnel thickness");
      Assert.IsTrue(defaults.Brightness == 1 && defaults.Variation == 0.8,
        "unexpected Tunnel brightness or variation");
      Assert.IsTrue(!defaults.BindToOrientation,
        "Tunnel unexpectedly binds orientation by default");
      Assert.IsTrue(defaults.Color == 0xFFFFFF, "unexpected Tunnel color");

      var configured = Layer("tunnel", "tunnel-clamped");
      configured.RendererParams = new Dictionary<string, double> {
        ["count"] = 100,
        ["speed"] = -1,
        ["thickness"] = 1,
        ["brightness"] = 2,
        ["variation"] = -1,
        ["bindOrientation"] = 1,
        ["color"] = 0x123456,
      };
      TunnelLayerOptions clamped =
        BuiltInOptions<TunnelLayerOptions>(configured);
      Assert.IsTrue(clamped.RingCount == 24 && clamped.Speed == 0,
        "Tunnel count or speed did not clamp");
      Assert.IsTrue(clamped.Thickness == 0.12 && clamped.Brightness == 1 &&
          clamped.Variation == 0,
        "Tunnel shape controls did not clamp");
      Assert.IsTrue(clamped.BindToOrientation,
        "Tunnel orientation binding did not compile");
      Assert.IsTrue(clamped.Color == 0x123456, "Tunnel color did not compile");

      AssertClose(
        0,
        LEDDomeTunnelVisualizer.AngularDistance(
          Vector3.UnitX, Vector3.UnitX),
        "Tunnel axis center is not radius zero");
      AssertClose(
        Math.PI / 2,
        LEDDomeTunnelVisualizer.AngularDistance(
          Vector3.UnitY, Vector3.UnitX),
        "Tunnel perpendicular ring geometry changed");
      AssertClose(
        Math.PI,
        LEDDomeTunnelVisualizer.AngularDistance(
          -Vector3.UnitX, Vector3.UnitX),
        "Tunnel antipodal ring geometry changed");
      AssertClose(
        .5,
        LEDDomeTunnelVisualizer.NormalizeAngularDistance(
          Math.PI / 4, Math.PI / 2),
        "Tunnel oriented radius is not linear in surface angle");
    }

    [TestMethod]
    public void OrientationRingsUseAngularDistance() {
      AngularRingBand midRipple =
        OrientationRingGeometry.RippleBand(300);
      Assert.IsTrue(midRipple.Contains(Vector3.UnitZ, Vector3.UnitX),
        "Ripple did not reach a quarter turn halfway to the antipode");
      Vector3 sixtyDegrees = new Vector3(
        (float)Math.Sin(Math.PI / 3), 0,
        (float)Math.Cos(Math.PI / 3));
      Assert.IsTrue(!midRipple.Contains(Vector3.UnitZ, sixtyDegrees),
        "Ripple retained its nonlinear chord-distance radius");
      Assert.IsTrue(!OrientationRingGeometry.RippleBand(700).Contains(
          Vector3.UnitZ, -Vector3.UnitZ),
        "Ripple remained visible after passing the antipode");

      for (int ring = 0; ring < 5; ring++) {
        double ringCenter = ring * .2 + .0125;
        Vector3 onRing = new Vector3(
          (float)Math.Sin(ringCenter * Math.PI), 0,
          (float)Math.Cos(ringCenter * Math.PI));
        Assert.IsTrue(OrientationRingGeometry.StampGridContains(
            DomeSurfaceGeometry.UnitSphereDot(Vector3.UnitZ, onRing)),
          "Stamp grid lost angular ring " + ring);

        double gapCenter = ring * .2 + .1;
        Vector3 betweenRings = new Vector3(
          (float)Math.Sin(gapCenter * Math.PI), 0,
          (float)Math.Cos(gapCenter * Math.PI));
        Assert.IsTrue(!OrientationRingGeometry.StampGridContains(
            DomeSurfaceGeometry.UnitSphereDot(
              Vector3.UnitZ, betweenRings)),
          "Stamp grid spacing is not angular at gap " + ring);
      }
    }

    [TestMethod]
    public void RippleDesaturationReducesSaturation() {
      LayerDefinition? ripple = DomeLayerCatalog.Metadata.Get("ripple");
      DomeLayerParam? desaturation = ripple?.Parameters.FirstOrDefault(
        parameter => parameter.Key == "desaturation");
      Assert.IsTrue(desaturation != null && desaturation.Min == 0 &&
        desaturation.Max == 1 && desaturation.Step == 0.05 &&
        desaturation.Default == 0,
        "Ripple desaturation slider is missing or malformed");

      DomeLayerSettings layer = Layer("ripple", "ripple-desaturation-options");
      layer.RendererParams = new Dictionary<string, double> {
        ["desaturation"] = 0.4,
      };
      AssertClose(0.4, BuiltInOptions<RippleLayerOptions>(layer).Desaturation,
        "Ripple desaturation did not compile into renderer options");

      AssertClose(1, LEDDomeRippleVisualizer.SaturationFor(0, 0),
        "default Ripple saturation changed");
      AssertClose(0.6, LEDDomeRippleVisualizer.SaturationFor(0, 0.4),
        "Ripple desaturation did not reduce saturation");
      AssertClose(0.3, LEDDomeRippleVisualizer.SaturationFor(300, 0.4),
        "Ripple desaturation did not preserve the lifetime fade");
      AssertClose(0, LEDDomeRippleVisualizer.SaturationFor(0, 1),
        "full Ripple desaturation was not grayscale");
    }

    [TestMethod]
    public void QuaternionTestIsDiagnostic() {
      Assert.IsTrue(DomeLayerCatalog.Metadata.Get("quaternion-test") == null,
        "Quaternion Test is still exposed as a layer renderer");

      ParameterRegistry registry =
        global::Spectrum.SpectrumConfigurationSchema.BuildParameterRegistry();
      Assert.IsTrue(registry.TryGet(
          "domeTestPattern", out ParameterDescriptor? testPattern) &&
        testPattern != null,
        "Quaternion Test parameter is missing");
      IReadOnlyList<string>? testPatternOptions = testPattern.Options;
      Assert.IsTrue(testPatternOptions?.Count == 6 &&
        testPatternOptions[5] == "Quaternion Test",
        "Quaternion Test is missing from the dome test-pattern selector");

      var config = new global::Spectrum.SpectrumConfiguration();
      var runtime = new global::Spectrum.Operator(config);
      Visualizer? diagnostic = runtime.DomeOutput.GetVisualizers()
        .FirstOrDefault(v => v is LEDDomeQuaternionTestVisualizer);
      Assert.IsTrue(diagnostic != null && diagnostic is not DomeLayerVisualizer,
        "Quaternion Test was not registered as a diagnostic visualizer");
      Assert.IsTrue(diagnostic.GetInputs().Length == 1 && ReferenceEquals(
          diagnostic.GetInputs()[0], runtime.OrientationInput),
        "Quaternion Test is not bound to the orientation input");
      config.domeTestPattern = 5;
      Assert.IsTrue(diagnostic.Priority == 1000,
        "Quaternion Test does not override the active layer stack");
      config.domeTestPattern = 0;
      Assert.IsTrue(diagnostic.Priority == 0,
        "Quaternion Test remains active after clearing the test pattern");
    }

    [TestMethod]
    public void DuplicateKinds() {
      var input = new[] {
        Layer("wave", "a"), Layer("wave", "b"),
      };
      (List<DomeLayerSettings>? stack, string? error) =
        new LayerStackService(DomeLayerCatalog.Metadata).Normalize(input);
      Assert.IsTrue(stack != null && error == null, error);
      Assert.IsTrue(stack.Count == 2, "layers were rejected");
      Assert.IsTrue(stack[0].InstanceId != stack[1].InstanceId, "IDs collided");
    }

    [TestMethod]
    public void ParameterNamespaces() {
      DomeLayerSettings layer = Layer("wave", "wave-1");
      layer.BlendMode = DomeBlend.ChromaticFringe.Id;
      layer.RendererParams = new Dictionary<string, double> {
        ["speed"] = 999,
        ["unknown"] = 1,
      };
      layer.OperationParams = new Dictionary<string, double> {
        ["offset"] = 999,
        ["unknown"] = 1,
      };
      (LayerStackSnapshot? snapshot, string? error) =
        new LayerStackService(DomeLayerCatalog.Metadata).CreateSnapshot(new[] { layer });
      Assert.IsTrue(snapshot != null && error == null, error);
      LayerSnapshot compiled = snapshot.Layers[0];
      Assert.IsTrue(compiled.RendererParameters.ContainsKey("speed"),
        "renderer option missing");
      Assert.IsTrue(!compiled.RendererParameters.ContainsKey("offset"),
        "operation option leaked into renderer namespace");
      Assert.IsTrue(compiled.OperationParameters.ContainsKey("offset"),
        "operation option missing");
      Assert.IsTrue(!compiled.OperationParameters.ContainsKey("unknown"),
        "unknown option survived");
    }
  }
}
