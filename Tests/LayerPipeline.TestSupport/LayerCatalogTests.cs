using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Visualizers;
using static Spectrum.LayerPipeline.Tests.TestAssertions;

namespace Spectrum.LayerPipeline.Tests {

  public static class LayerCatalogTests {
    public static void Register(Action<string, Action> run) {
      run(nameof(CatalogIsUnique), CatalogIsUnique);
      run(nameof(TunnelParametersCompile), TunnelParametersCompile);
      run(nameof(OrientationRingsUseAngularDistance), OrientationRingsUseAngularDistance);
      run(nameof(RippleDesaturationReducesSaturation), RippleDesaturationReducesSaturation);
      run(nameof(QuaternionTestIsDiagnostic), QuaternionTestIsDiagnostic);
      run(nameof(DuplicateKinds), DuplicateKinds);
      run(nameof(ParameterNamespaces), ParameterNamespaces);
    }

    private static void CatalogIsUnique() {
      var ids = new HashSet<string>();
      foreach (LayerDefinition definition in DomeLayerCatalog.Metadata.Definitions) {
        Assert(ids.Add(definition.Id), "duplicate " + definition.Id);
        Assert(definition.CompileOptions != null,
          "missing options compiler for " + definition.Id);
        var keys = new HashSet<string>();
        foreach (DomeLayerParam parameter in definition.Parameters) {
          Assert(keys.Add(parameter.Key), "duplicate parameter " + parameter.Key);
        }
        (LayerStackSnapshot? snapshot, string? error) =
          new LayerStackService(DomeLayerCatalog.Metadata).CreateSnapshot(new[] {
            Layer(definition.Id, "options-" + definition.Id),
          });
        Assert(snapshot != null && error == null, error);
        ILayerRendererOptions options = definition.CompileOptions(
          snapshot.Layers[0].RendererParameters);
        Assert(options != null, "null options for " + definition.Id);
      }
    }

    private static void TunnelParametersCompile() {
      LayerDefinition? definition = DomeLayerCatalog.Metadata.Get("tunnel");
      Assert(definition != null && definition.DisplayName == "Tunnel",
        "Tunnel is missing from the layer catalog");

      TunnelLayerOptions defaults = BuiltInOptions<TunnelLayerOptions>(
        Layer("tunnel", "tunnel-defaults"));
      Assert(defaults.RingCount == 12, "unexpected Tunnel ring count");
      Assert(Math.Abs(defaults.Speed - 0.18) < 1e-9,
        "unexpected Tunnel speed");
      Assert(Math.Abs(defaults.Thickness - 0.025) < 1e-9,
        "unexpected Tunnel thickness");
      Assert(defaults.Brightness == 1 && defaults.Variation == 0.8,
        "unexpected Tunnel brightness or variation");
      Assert(!defaults.BindToOrientation,
        "Tunnel unexpectedly binds orientation by default");
      Assert(defaults.Color == 0xFFFFFF, "unexpected Tunnel color");

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
      Assert(clamped.RingCount == 24 && clamped.Speed == 0,
        "Tunnel count or speed did not clamp");
      Assert(clamped.Thickness == 0.12 && clamped.Brightness == 1 &&
          clamped.Variation == 0,
        "Tunnel shape controls did not clamp");
      Assert(clamped.BindToOrientation,
        "Tunnel orientation binding did not compile");
      Assert(clamped.Color == 0x123456, "Tunnel color did not compile");

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

    private static void OrientationRingsUseAngularDistance() {
      AssertClose(
        .5,
        DomeSurfaceGeometry.NormalizedAngularDistance(
          Vector3.UnitZ, Vector3.UnitX),
        "normalized angular distance does not map a quarter turn to .5");

      AngularRingBand midRipple =
        OrientationRingGeometry.RippleBand(300);
      Assert(midRipple.Contains(Vector3.UnitZ, Vector3.UnitX),
        "Ripple did not reach a quarter turn halfway to the antipode");
      Vector3 sixtyDegrees = new Vector3(
        (float)Math.Sin(Math.PI / 3), 0,
        (float)Math.Cos(Math.PI / 3));
      Assert(!midRipple.Contains(Vector3.UnitZ, sixtyDegrees),
        "Ripple retained its nonlinear chord-distance radius");
      Assert(!OrientationRingGeometry.RippleBand(700).Contains(
          Vector3.UnitZ, -Vector3.UnitZ),
        "Ripple remained visible after passing the antipode");

      for (int ring = 0; ring < 5; ring++) {
        double ringCenter = ring * .2 + .0125;
        Vector3 onRing = new Vector3(
          (float)Math.Sin(ringCenter * Math.PI), 0,
          (float)Math.Cos(ringCenter * Math.PI));
        Assert(OrientationRingGeometry.StampGridContains(
            DomeSurfaceGeometry.UnitSphereDot(Vector3.UnitZ, onRing)),
          "Stamp grid lost angular ring " + ring);

        double gapCenter = ring * .2 + .1;
        Vector3 betweenRings = new Vector3(
          (float)Math.Sin(gapCenter * Math.PI), 0,
          (float)Math.Cos(gapCenter * Math.PI));
        Assert(!OrientationRingGeometry.StampGridContains(
            DomeSurfaceGeometry.UnitSphereDot(
              Vector3.UnitZ, betweenRings)),
          "Stamp grid spacing is not angular at gap " + ring);
      }
    }

    private static void RippleDesaturationReducesSaturation() {
      LayerDefinition? ripple = DomeLayerCatalog.Metadata.Get("ripple");
      DomeLayerParam? desaturation = ripple?.Parameters.FirstOrDefault(
        parameter => parameter.Key == "desaturation");
      Assert(desaturation != null && desaturation.Min == 0 &&
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

    private static void QuaternionTestIsDiagnostic() {
      Assert(DomeLayerCatalog.Metadata.Get("quaternion-test") == null,
        "Quaternion Test is still exposed as a layer renderer");

      ParameterRegistry registry =
        global::Spectrum.Web.SpectrumParameters.BuildRegistry();
      Assert(registry.TryGet(
          "domeTestPattern", out ParameterDescriptor? testPattern) &&
        testPattern != null,
        "Quaternion Test parameter is missing");
      IReadOnlyList<string>? testPatternOptions = testPattern.Options;
      Assert(testPatternOptions?.Count == 6 &&
        testPatternOptions[5] == "Quaternion Test",
        "Quaternion Test is missing from the dome test-pattern selector");

      var config = new global::Spectrum.SpectrumConfiguration();
      var runtime = new global::Spectrum.Operator(config);
      Visualizer? diagnostic = runtime.DomeOutput.GetVisualizers()
        .FirstOrDefault(v => v is LEDDomeQuaternionTestVisualizer);
      Assert(diagnostic != null && diagnostic is not DomeLayerVisualizer,
        "Quaternion Test was not registered as a diagnostic visualizer");
      Assert(diagnostic.GetInputs().Length == 1 && ReferenceEquals(
          diagnostic.GetInputs()[0], runtime.OrientationInput),
        "Quaternion Test is not bound to the orientation input");
      config.domeTestPattern = 5;
      Assert(diagnostic.Priority == 1000,
        "Quaternion Test does not override the active layer stack");
      config.domeTestPattern = 0;
      Assert(diagnostic.Priority == 0,
        "Quaternion Test remains active after clearing the test pattern");
    }

    private static void DuplicateKinds() {
      var input = new[] {
        Layer("wave", "a"), Layer("wave", "b"),
      };
      (List<DomeLayerSettings>? stack, string? error) =
        new LayerStackService(DomeLayerCatalog.Metadata).Normalize(input);
      Assert(stack != null && error == null, error);
      Assert(stack.Count == 2, "layers were rejected");
      Assert(stack[0].InstanceId != stack[1].InstanceId, "IDs collided");
    }

    private static void ParameterNamespaces() {
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
      Assert(snapshot != null && error == null, error);
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
    private static T BuiltInOptions<T>(DomeLayerSettings layer)
      where T : class, ILayerRendererOptions {
      (LayerStackSnapshot? snapshot, string? error) =
        new LayerStackService(DomeLayerCatalog.Metadata).CreateSnapshot(
          new[] { layer });
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

    private static DomeLayerSettings Layer(string key, string? id) => new() {
      InstanceId = id,
      VisualizerKey = key,
      BlendMode = DomeBlend.Add.Id,
      Opacity = 1,
      Enabled = true,
    };

    private static void AssertClose(
      double expected, double actual, string message
    ) {
      Assert(Math.Abs(expected - actual) < 0.000000001,
        message + " expected " + expected + " but got " + actual);
    }
  }
}
