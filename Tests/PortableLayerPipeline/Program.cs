using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Visualizers;
using XSerializer;

namespace Spectrum.LayerPipeline.Tests {

  [TestClass]
  public sealed class PortableLayerPipelineTests {
    private static readonly IReadOnlyDictionary<string, Action> TestCases =
      BuildTestCases();

    public static IEnumerable<object[]> DiscoverTestCases() =>
      TestCases.Keys.OrderBy(name => name)
        .Select(name => new object[] { name });

    [TestMethod]
    [DoNotParallelize]
    [DynamicData(nameof(DiscoverTestCases))]
    public void Run(string name) {
      TestCases[name]();
    }

    private static IReadOnlyDictionary<string, Action> BuildTestCases() {
      var tests = new Dictionary<string, Action>();
      foreach (MethodInfo method in typeof(PortableLayerPipelineTests).GetMethods(
          BindingFlags.NonPublic | BindingFlags.Static)) {
        if (method.ReturnType == typeof(void) &&
            method.GetParameters().Length == 0) {
          tests.Add(method.Name, () => Invoke(method));
        }
      }

      void Register(string name, Action test) => tests.Add(name, test);
      MotionEmbersTests.Register(Register);
      StackValidatorTests.Register(Register);
      WandProtocolTests.Register(Register);
      PaletteServiceTests.Register(Register);
      AdvisoryLockTests.Register(Register);
      ColorPerformanceTests.Register(Register);
      OPCWireTests.Register(Register);
      return tests;
    }

    private static void Invoke(MethodInfo method) {
      try {
        method.Invoke(null, null);
      } catch (TargetInvocationException error)
          when (error.InnerException != null) {
        ExceptionDispatchInfo.Capture(error.InnerException).Throw();
      }
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
        (LayerStackSnapshot snapshot, string error) =
          new LayerStackService(DomeLayerCatalog.Metadata).CreateSnapshot(new[] {
            Layer(definition.Id, "options-" + definition.Id),
          });
        Assert(error == null, error);
        ILayerRendererOptions options = definition.CompileOptions(
          snapshot.Layers[0].RendererParameters);
        Assert(options != null, "null options for " + definition.Id);
      }
    }

    private static void BuiltInFeaturesLiveInPortableCore() {
      Type catalogType = typeof(LayerCatalog);
      Type runtimeType = typeof(global::Spectrum.Operator);
      Type coreType = typeof(global::Spectrum.BuiltInDomeLayerCatalog);
      Assert(catalogType.GetProperty("Default") == null,
        "Base still owns the application feature catalog");
      Assert(typeof(RadialLayerOptions).Assembly == coreType.Assembly,
        "typed built-in options are outside the portable core");
      Assert(coreType.Assembly == runtimeType.Assembly,
        "the runtime and portable feature metadata are split across assemblies");
      Assert(catalogType.Assembly.GetType(
          typeof(RadialLayerOptions).FullName) == null,
        "Base still contains built-in renderer option types");
      Assert(DomeLayerCatalog.Metadata.Definitions.Count > 0 &&
          DomeLayerCatalog.Metadata.Definitions.All(
            definition => definition.CreateRenderer == null),
        "the metadata catalog unexpectedly owns runtime factories");
    }

    private static void TunnelParametersCompile() {
      LayerDefinition definition = DomeLayerCatalog.Metadata.Get("tunnel");
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
      LayerDefinition ripple = DomeLayerCatalog.Metadata.Get("ripple");
      DomeLayerParam desaturation = ripple.Parameters.FirstOrDefault(
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

    private static void PointCloudUsesVisibleHemisphere() {
      const int count = 320;
      double previousZ = double.PositiveInfinity;
      for (int i = 0; i < count; i++) {
        Vector3 point =
          LEDDomePointCloudVisualizer.FibonacciHemispherePoint(i, count);
        Assert(point.Z > 0 && point.Z <= 1,
          "Point Cloud seeded a home outside the visible hemisphere");
        Assert(Math.Abs(point.Length() - 1) < .000001,
          "Point Cloud seeded a non-unit home");
        if (i > 0) {
          Assert(Math.Abs((previousZ - point.Z) - 1d / count) < .000001,
            "Point Cloud hemisphere bands are not equal-area");
        }
        previousZ = point.Z;
      }
      Assert(
        LEDDomePointCloudVisualizer.FibonacciHemispherePoint(0, count).Z > .99f &&
        LEDDomePointCloudVisualizer.FibonacciHemispherePoint(count - 1, count).Z < .01f,
        "Point Cloud homes do not span crown to rim");

      Vector3 lowerAxis = Vector3.Normalize(new Vector3(1, 2, -3));
      Vector3 folded =
        LEDDomePointCloudVisualizer.FoldAxisToUpperHemisphere(lowerAxis);
      Assert(folded.Z > 0 && Vector3.Distance(folded, -lowerAxis) < .000001,
        "Point Cloud did not fold a lower-hemisphere aim axis");
      Vector3 upperAxis = Vector3.Normalize(new Vector3(-2, 1, 3));
      Assert(Vector3.Distance(
          LEDDomePointCloudVisualizer.FoldAxisToUpperHemisphere(upperAxis),
          upperAxis) < .000001,
        "Point Cloud changed an already-visible aim axis");

      Vector3 crossing = Vector3.Normalize(new Vector3(.4f, .2f, -.1f));
      Vector3 reflected =
        LEDDomePointCloudVisualizer.ReflectAcrossRim(crossing);
      Assert(reflected.Z > 0 && Math.Abs(reflected.Length() - 1) < .000001,
        "Point Cloud rim reflection left the visible hemisphere");
      Assert(reflected.X == crossing.X && reflected.Y == crossing.Y,
        "Point Cloud rim reflection jumped across the dome");
    }

    private static void PointCloudSpatialIndexMatchesBruteForce() {
      const int pixelCount = 1024;
      var positionBuilder = ImmutableArray.CreateBuilder<Vector3>(pixelCount);
      for (int pixel = 0; pixel < pixelCount; pixel++) {
        positionBuilder.Add(
          LEDDomePointCloudVisualizer.FibonacciHemispherePoint(
            pixel, pixelCount));
      }
      ImmutableArray<Vector3> positions = positionBuilder.MoveToImmutable();
      var index =
        new LEDDomePointCloudVisualizer.PixelSpatialIndex(positions);
      var actualValues = new double[pixelCount];
      var actualHues = new double[pixelCount];
      var expectedValues = new double[pixelCount];
      var expectedHues = new double[pixelCount];

      foreach ((int count, double size) in new[] {
          (4, 0.02), (48, 0.14), (320, 0.5),
      }) {
        var spots = new LEDDomePointCloudVisualizer.Spot[count];
        for (int spot = 0; spot < count; spot++) {
          // The coprime permutation keeps spot and pixel lattice indices from
          // lining up while remaining deterministic.
          int position = (spot * 73 + 19) % pixelCount;
          spots[spot] = new LEDDomePointCloudVisualizer.Spot {
            pos = positions[position],
            hue = (spot + 0.25) / count,
          };
        }

        LEDDomePointCloudVisualizer.ResolveNearestSpots(
          index, spots, size, actualValues, actualHues);
        ResolvePointCloudBruteForce(
          positions, spots, size, expectedValues, expectedHues);

        for (int pixel = 0; pixel < pixelCount; pixel++) {
          Assert(actualValues[pixel] == expectedValues[pixel],
            "Point Cloud spatial value differed from brute force at size " +
            size + ", pixel " + pixel);
          if (expectedValues[pixel] > 0) {
            Assert(actualHues[pixel] == expectedHues[pixel],
              "Point Cloud spatial winner differed from brute force at size " +
              size + ", pixel " + pixel);
          }
        }

        // Repeated renders with an unchanged spot size must reuse the index and
        // scratch arrays without adding GC pressure to the operator thread.
        for (int warmup = 0; warmup < 4; warmup++) {
          LEDDomePointCloudVisualizer.ResolveNearestSpots(
            index, spots, size, actualValues, actualHues);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < 20; frame++) {
          LEDDomePointCloudVisualizer.ResolveNearestSpots(
            index, spots, size, actualValues, actualHues);
        }
        long allocated =
          GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(allocated == 0,
          "Point Cloud spatial render allocated " + allocated +
          " bytes at size " + size);
      }
    }

    private static void ResolvePointCloudBruteForce(
      ImmutableArray<Vector3> positions,
      LEDDomePointCloudVisualizer.Spot[] spots,
      double spotSize,
      double[] bestValues,
      double[] bestHues
    ) {
      Array.Clear(bestValues, 0, bestValues.Length);
      double cosRadius = Math.Cos(spotSize);
      double radiusSpan = Math.Max(1 - cosRadius, 1e-6);
      for (int pixel = 0; pixel < positions.Length; pixel++) {
        for (int spot = 0; spot < spots.Length; spot++) {
          double cos = Vector3.Dot(positions[pixel], spots[spot].pos);
          if (cos <= cosRadius) {
            continue;
          }
          double value = (cos - cosRadius) / radiusSpan;
          if (value > bestValues[pixel]) {
            bestValues[pixel] = value;
            bestHues[pixel] = spots[spot].hue;
          }
        }
      }
    }

    private static void QuaternionTestIsDiagnostic() {
      Assert(DomeLayerCatalog.Metadata.Get("quaternion-test") == null,
        "Quaternion Test is still exposed as a layer renderer");

      ParameterRegistry registry =
        global::Spectrum.Web.SpectrumParameters.BuildRegistry();
      Assert(registry.TryGet(
          "domeTestPattern", out ParameterDescriptor testPattern) &&
        testPattern.Options.Count == 6 &&
        testPattern.Options[5] == "Quaternion Test",
        "Quaternion Test is missing from the dome test-pattern selector");

      var config = new global::Spectrum.SpectrumConfiguration();
      var runtime = new global::Spectrum.Operator(config);
      Visualizer diagnostic = runtime.DomeOutput.GetVisualizers()
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
      (List<DomeLayerSettings> stack, string error) =
        new LayerStackService(DomeLayerCatalog.Metadata).Normalize(input);
      Assert(error == null, error);
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
      (LayerStackSnapshot snapshot, string error) =
        new LayerStackService(DomeLayerCatalog.Metadata).CreateSnapshot(new[] { layer });
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
      Assert(captured.GetOptions<ScalarOptions>().Value == 2,
        "renderer runtime kept stale parameters");
    }

    private static void SimulatorTopDownProjection() {
      const int strut = 65;
      Tuple<double, double> stripPoint =
        StrutLayoutFactory.GetProjectedPoint(strut, 0);
      Tuple<double, double> explicitStripPoint =
        StrutLayoutFactory.GetProjectedPoint(
          strut, 0, DomeProjection.StripExtents);
      Tuple<double, double> topDownPoint =
        StrutLayoutFactory.GetProjectedPoint(
          strut, 0, DomeProjection.TopDown);

      AssertClose(stripPoint.Item1, explicitStripPoint.Item1,
        "the default simulator projection changed x");
      AssertClose(stripPoint.Item2, explicitStripPoint.Item2,
        "the default simulator projection changed y");

      double stripX = stripPoint.Item1 - .5;
      double stripY = stripPoint.Item2 - .5;
      double topDownX = topDownPoint.Item1 - .5;
      double topDownY = topDownPoint.Item2 - .5;
      double stripRadius = Math.Sqrt(stripX * stripX + stripY * stripY);
      double topDownRadius = Math.Sqrt(
        topDownX * topDownX + topDownY * topDownY);
      Assert(topDownRadius > stripRadius,
        "the top-down projection did not spread the dome crown");
      Assert(topDownRadius <= .5000000001,
        "the top-down projection escaped the dome silhouette");
      AssertClose(0, stripX * topDownY - stripY * topDownX,
        "the top-down projection changed azimuth");

      int ledIndex = 3;
      Tuple<double, double> endpoint0 =
        StrutLayoutFactory.GetProjectedPoint(
          strut, 0, DomeProjection.TopDown);
      Tuple<double, double> endpoint1 =
        StrutLayoutFactory.GetProjectedPoint(
          strut, 1, DomeProjection.TopDown);
      Tuple<double, double> led = StrutLayoutFactory.GetProjectedLEDPoint(
        strut, ledIndex, DomeProjection.TopDown);
      double d = (ledIndex + 1.0) /
        (LEDDomeOutput.GetNumLEDs(strut) + 2.0);
      AssertClose(endpoint0.Item1 + (endpoint1.Item1 - endpoint0.Item1) * d,
        led.Item1, "the top-down LED left its physical strut (x)");
      AssertClose(endpoint0.Item2 + (endpoint1.Item2 - endpoint0.Item2) * d,
        led.Item2, "the top-down LED left its physical strut (y)");

      double expectedTheta = Math.Min(stripRadius * 2, 1) * Math.PI / 2;
      double projectedTheta = Math.Asin(Math.Min(topDownRadius * 2, 1));
      AssertClose(expectedTheta, projectedTheta,
        "the top-down projection changed the physical polar angle");
    }

    private static void DomeTopologyUsesTopDownNormals() {
      var config = new global::Spectrum.SpectrumConfiguration();
      var output = new LEDDomeOutput(
        config, new RuntimeTelemetry(), new BeatBroadcaster(config));
      DomeFrame frame = output.MakeDomeFrame();

      Assert(frame.Topology.PixelCount == frame.pixels.Length,
        "the topology and frame pixel counts differ");
      for (int i = 0; i < frame.Topology.PixelCount; i++) {
        DomeTopologyPixel pixel = frame.Topology.PixelAt(i);
        Tuple<double, double> strip =
          StrutLayoutFactory.GetProjectedLEDPoint(
            pixel.StrutIndex, pixel.LedIndex, DomeProjection.StripExtents);
        Tuple<double, double> topDown =
          StrutLayoutFactory.GetProjectedLEDPoint(
            pixel.StrutIndex, pixel.LedIndex, DomeProjection.TopDown);
        AssertClose(strip.Item1, pixel.StripX,
          "topology strip x differs at pixel " + i);
        AssertClose(strip.Item2, pixel.StripY,
          "topology strip y differs at pixel " + i);
        Assert(pixel.X == pixel.StripX && pixel.Y == pixel.StripY,
          "the planar compatibility coordinates changed at pixel " + i);
        AssertClose(topDown.Item1, pixel.TopDownX,
          "topology top-down x differs at pixel " + i);
        AssertClose(topDown.Item2, pixel.TopDownY,
          "topology top-down y differs at pixel " + i);

        double x = 2 * pixel.TopDownX - 1;
        double y = 1 - 2 * pixel.TopDownY;
        Assert(x * x + y * y <= 1.000000001,
          "top-down pixel escaped the dome silhouette at pixel " + i);

        Vector3 normal = frame.Normals[i];
        Assert(float.IsFinite(normal.X) && float.IsFinite(normal.Y) &&
          float.IsFinite(normal.Z),
          "topology normal is not finite at pixel " + i);
        Assert(normal.Z >= 0,
          "topology normal points below the rim at pixel " + i);
        Assert(Math.Abs(normal.Length() - 1) < .000001,
          "topology normal is not unit length at pixel " + i);
      }

      var explicitProjections = new DomeTopology(new[] {
        new DomeTopologyPixel(0, 0, .5, .5, .75, .5),
        new DomeTopologyPixel(1, 0, .5, .5, 1.1, .5),
      });
      Vector3 midDome = explicitProjections.Normals[0];
      Assert(Math.Abs(midDome.X - .5) < .000001 &&
        Math.Abs(midDome.Z - Math.Sqrt(.75)) < .000001,
        "normal construction used strip coordinates instead of top-down");
      Vector3 clampedRim = explicitProjections.Normals[1];
      Assert(Math.Abs(clampedRim.X - 1) < .000001 &&
        Math.Abs(clampedRim.Z) < .000001 &&
        Math.Abs(clampedRim.Length() - 1) < .000001,
        "top-down overshoot was not clamped to a unit rim normal");
    }

    private static void SphereDirectionsProjectToStripExtents() {
      Vector2 crown = StrutLayoutFactory.ProjectSphereToStrip(Vector3.UnitZ);
      Assert(crown.Length() < .000001,
        "the crown did not project to the strip origin");

      double theta = Math.PI / 4;
      double azimuth = Math.PI / 6;
      var direction = new Vector3(
        (float)(Math.Sin(theta) * Math.Cos(azimuth)),
        (float)(Math.Sin(theta) * Math.Sin(azimuth)),
        (float)Math.Cos(theta));
      Vector2 midDome = StrutLayoutFactory.ProjectSphereToStrip(
        direction * 3);
      Assert(Math.Abs(midDome.Length() - .5) < .000001,
        "mid-dome direction has the wrong strip radius");
      Assert(Math.Abs(midDome.X - .5 * Math.Cos(azimuth)) < .000001 &&
        Math.Abs(midDome.Y + .5 * Math.Sin(azimuth)) < .000001,
        "mid-dome direction changed azimuth");

      Vector2 rim = StrutLayoutFactory.ProjectSphereToStrip(Vector3.UnitY);
      Assert(Math.Abs(rim.X) < .000001 && Math.Abs(rim.Y + 1) < .000001,
        "rim direction did not reach the strip silhouette");
      Vector2 foldedAxis = StrutLayoutFactory.ProjectSphereToStrip(
        new Vector3(1, 0, -1), foldAxisToUpperHemisphere: true);
      Assert(Math.Abs(foldedAxis.X + .5) < .000001 &&
        Math.Abs(foldedAxis.Y) < .000001,
        "axis projection did not select the upper-hemisphere endpoint");

      foreach (Vector3 expected in new[] {
        Vector3.UnitZ, direction, Vector3.UnitY,
      }) {
        Vector2 strip = StrutLayoutFactory.ProjectSphereToStrip(expected);
        double radius = strip.Length();
        double roundTripTheta = radius * Math.PI / 2;
        Vector3 actual = radius < .0000001
          ? Vector3.UnitZ
          : Vector3.Normalize(new Vector3(
              (float)(Math.Sin(roundTripTheta) * strip.X / radius),
              (float)(-Math.Sin(roundTripTheta) * strip.Y / radius),
              (float)Math.Cos(roundTripTheta)));
        Assert(Vector3.Distance(expected, actual) < .000001,
          "sphere-to-strip round trip changed a canonical direction");
      }

      bool rejectedZero = false;
      try {
        StrutLayoutFactory.ProjectSphereToStrip(Vector3.Zero);
      } catch (ArgumentException) {
        rejectedZero = true;
      }
      Assert(rejectedZero, "sphere-to-strip accepted a zero direction");

      bool rejectedLowerHemisphere = false;
      try {
        StrutLayoutFactory.ProjectSphereToStrip(
          new Vector3(1, 0, -1));
      } catch (ArgumentOutOfRangeException) {
        rejectedLowerHemisphere = true;
      }
      Assert(rejectedLowerHemisphere,
        "sphere-to-strip silently flattened a lower-hemisphere direction");
    }

    private static void TargetedPlanarCoordinatesRoundTripNormals() {
      var config = new global::Spectrum.SpectrumConfiguration();
      var output = new LEDDomeOutput(
        config, new RuntimeTelemetry(), new BeatBroadcaster(config));
      DomeFrame frame = output.MakeDomeFrame();
      ImmutableArray<Vector2> projected =
        DomeSurfaceGeometry.ProjectNormalsToStrip(frame.Normals);

      Assert(projected.Length == frame.Topology.PixelCount,
        "targeted planar projection changed the pixel count");
      double maximumRoundTripError = 0;
      for (int i = 0; i < projected.Length; i++) {
        Vector2 strip = projected[i];
        double radius = strip.Length();
        Assert(float.IsFinite(strip.X) && float.IsFinite(strip.Y) &&
          radius <= 1.000001,
          "targeted planar coordinate left the dome at pixel " + i);

        double theta = radius * Math.PI / 2;
        Vector3 roundTrip = radius < .0000001
          ? Vector3.UnitZ
          : Vector3.Normalize(new Vector3(
              (float)(Math.Sin(theta) * strip.X / radius),
              (float)(-Math.Sin(theta) * strip.Y / radius),
              (float)Math.Cos(theta)));
        double roundTripError = Vector3.Distance(frame.Normals[i], roundTrip);
        maximumRoundTripError = Math.Max(
          maximumRoundTripError, roundTripError);
      }
      Assert(maximumRoundTripError < .00005,
        "targeted planar round trip exceeded float tolerance: " +
        maximumRoundTripError);
    }

    private static void TunnelFixedModeMatchesCrownAxis() {
      var config = new global::Spectrum.SpectrumConfiguration();
      var output = new LEDDomeOutput(
        config, new RuntimeTelemetry(), new BeatBroadcaster(config));
      ImmutableArray<Vector3> positions =
        output.MakeDomeFrame().BakePixelPositions();
      double[] fixedRadii =
        LEDDomeTunnelVisualizer.BuildFixedRadii(positions);
      var crownRadii = new double[positions.Length];
      LEDDomeTunnelVisualizer.BuildNormalizedAngularRadii(
        positions, Vector3.UnitZ, crownRadii);

      Assert(fixedRadii.Length == positions.Length,
        "Tunnel fixed radius count differs from the topology");
      for (int i = 0; i < positions.Length; i++) {
        AssertClose(fixedRadii[i], crownRadii[i],
          "Tunnel fixed and crown-bound radii differ at pixel " + i);
      }
      AssertClose(1, fixedRadii.Max(),
        "Tunnel fixed field does not reach the farthest LED");
    }

    private static void RuntimeOptionsSwap() {
      DomeLayerSettings first = Layer("wave", "runtime-wave");
      first.RendererParams = new Dictionary<string, double> { ["speed"] = .25 };
      (LayerStackSnapshot initial, string initialError) =
        new LayerStackService(DomeLayerCatalog.Metadata).CreateSnapshot(new[] { first });
      Assert(initialError == null, initialError);
      LayerDefinition definition = DomeLayerCatalog.Metadata.Get("wave");
      var runtime = new LayerRendererRuntime(
        initial.Layers[0], definition.CompileOptions);
      WaveLayerOptions original = runtime.GetOptions<WaveLayerOptions>();
      Assert(original.Speed == .25, "initial typed option missing");

      DomeLayerSettings second = Layer("wave", "runtime-wave");
      second.RendererParams = new Dictionary<string, double> { ["speed"] = 1.25 };
      (LayerStackSnapshot changed, string changedError) =
        new LayerStackService(DomeLayerCatalog.Metadata).CreateSnapshot(new[] { second });
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
      LayerRendererRuntime retainedRuntime = null;
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
      ILayerRenderer liveRenderer = store.Get(initialLayer);
      Assert(retainedRuntime.GetOptions<ScalarOptions>().Value == 1,
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
      LayerDefinition definition = DomeLayerCatalog.Metadata.Get("earth");
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

      using Stream texture = typeof(LEDDomeEarthVisualizer).Assembly
        .GetManifestResourceStream(
          LEDDomeEarthVisualizer.TextureResourceName);
      Assert(texture != null && texture.Length > 0,
        "the Earth texture was not embedded in the application");

      var config = ConfigurationWithLayers(layer);
      var runtime = new global::Spectrum.Operator(config);
      DomeLayerVisualizer earth = null;
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

    private static void AstronomyOptionsAndHeading() {
      DomeLayerSettings layer = Layer("astronomy", "typed-astronomy");
      layer.RendererParams = new Dictionary<string, double> {
        ["northHeading"] = 999,
        ["startDate"] = 20260715,
        ["timeOffsetHours"] = 999,
        ["showDaytimeSky"] = 0,
        ["showNighttimeSky"] = 0,
        ["playbackSpeed"] = 999,
        ["loop"] = 1,
      };
      AstronomyLayerOptions options =
        BuiltInOptions<AstronomyLayerOptions>(layer);
      Assert(options.NorthHeading == 359 &&
        options.StartDate == 20260715 && options.TimeOffsetHours == 168 &&
        !options.ShowDaytimeSky && !options.ShowNighttimeSky &&
        options.PlaybackSpeed == 8 && options.Loop,
        "astronomy controls were not clamped by their schema");
      AstronomyLayerOptions defaultOptions =
        BuiltInOptions<AstronomyLayerOptions>(
          Layer("astronomy", "default-astronomy"));
      Assert(defaultOptions.ShowDaytimeSky &&
          defaultOptions.ShowNighttimeSky &&
          defaultOptions.PlaybackSpeed == 1,
        "astronomy controls did not preserve their default appearance");
      Assert(DomeLayerDate.TryDecode(defaultOptions.StartDate, out _),
        "astronomy start date did not default to a valid local date");
      Assert(DomeLayerDate.TryParse("2026-07-15", out double encodedDate) &&
          encodedDate == 20260715 &&
          !DomeLayerDate.TryParse("2026-02-30", out _),
        "astronomy date text parsing accepted an invalid date");

      DateTime referenceUtc = new DateTime(
        2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);
      DateTime baseTime = AstronomySky.StartDateUtc(
        options.StartDate, referenceUtc);
      Assert(baseTime == new DateTime(
          2026, 7, 15, 7, 0, 0, DateTimeKind.Utc),
        "Black Rock City summer midnight did not convert from PDT");
      DateTime winterMidnight = AstronomySky.StartDateUtc(
        20260115, referenceUtc);
      Assert(winterMidnight == new DateTime(
          2026, 1, 15, 8, 0, 0, DateTimeKind.Utc),
        "Black Rock City winter midnight did not convert from PST");
      double baseJulianDay = AstronomySky.JulianDay(baseTime);
      double endJulianDay = AstronomySky.JulianDay(
        baseTime.AddHours(options.TimeOffsetHours));
      Assert(Math.Abs(endJulianDay - baseJulianDay - 7) < 1e-9,
        "astronomy time slider did not span one week");

      double stopped = LEDDomeAstronomyVisualizer.PlaybackOffset(
        167, 2, 1, false, out bool completed);
      Assert(completed && stopped == 168,
        "non-looping astronomy playback did not stop at one week");
      double wrapped = LEDDomeAstronomyVisualizer.PlaybackOffset(
        167, 2, 1, true, out completed);
      Assert(!completed && wrapped == 1,
        "looping astronomy playback did not wrap to the start");
      double halfSpeed = LEDDomeAstronomyVisualizer.PlaybackOffset(
        0, 2, 0.5, false, out completed);
      double tripleSpeed = LEDDomeAstronomyVisualizer.PlaybackOffset(
        0, 2, 3, false, out completed);
      Assert(halfSpeed == 1 && tripleSpeed == 6,
        "astronomy playback speed did not scale elapsed time");
      Assert(!LEDDomeAstronomyVisualizer.UsesPlaybackInterpolation(1) &&
        LEDDomeAstronomyVisualizer.UsesPlaybackInterpolation(1.1),
        "astronomy interpolation threshold did not start above 1x");
      Assert(LEDDomeAstronomyVisualizer.InterpolationFramesPerSecond(1) == 10 &&
        LEDDomeAstronomyVisualizer.InterpolationFramesPerSecond(8) == 60,
        "astronomy interpolation did not ramp from 10 to 60 FPS");
      Assert(LEDDomeAstronomyVisualizer.InterpolateColor(
          0x000000, 0xFFFFFF, 0.5) == 0x7F7F7F,
        "astronomy playback did not linearly interpolate keyframes");
      Assert(LEDDomeAstronomyVisualizer.SkyColor(
            0, true, true) == 0x082040 &&
          LEDDomeAstronomyVisualizer.SkyColor(
            0, false, true) == 0x000000 &&
          LEDDomeAstronomyVisualizer.SkyColor(
            1, true, true) == 0x000006 &&
          LEDDomeAstronomyVisualizer.SkyColor(
            1, true, false) == 0x000000 &&
          !LEDDomeAstronomyVisualizer.StarsVisible(1, false) &&
          LEDDomeAstronomyVisualizer.StarsVisible(1, true),
        "astronomy day/night sky toggles did not isolate their effects");

      DomeLayerSettings playbackLayer = Layer(
        "astronomy", "astronomy-playback-controls");
      playbackLayer.RendererParams = new Dictionary<string, double> {
        ["timeOffsetHours"] = 10,
      };
      var playbackConfig = ConfigurationWithLayers(playbackLayer);
      var playbackRuntime = new global::Spectrum.Operator(playbackConfig);
      LEDDomeAstronomyVisualizer playbackVisualizer = null;
      foreach (
        Visualizer visualizer in playbackRuntime.DomeOutput.GetVisualizers()
      ) {
        if (visualizer is LEDDomeAstronomyVisualizer astronomy) {
          playbackVisualizer = astronomy;
          break;
        }
      }
      Assert(playbackVisualizer != null,
        "astronomy playback visualizer was not created");
      playbackVisualizer.Visualize();
      playbackConfig.ReplaceDomeLayerFireCounters(
        new Dictionary<string, int> {
        [playbackLayer.InstanceId] = 1,
      });
      playbackVisualizer.Visualize();
      Assert(playbackVisualizer.PlaybackActive,
        "astronomy Play did not start playback");
      playbackConfig.ReplaceDomeLayerClearCounters(
        new Dictionary<string, int> {
        [playbackLayer.InstanceId] = 1,
      });
      playbackVisualizer.Visualize();
      Assert(!playbackVisualizer.PlaybackActive,
        "astronomy Stop did not halt playback");
      double stoppedOffset = playbackVisualizer.PlaybackStartOffset;
      Assert(stoppedOffset >= 10,
        "astronomy Stop moved playback behind its starting offset");
      playbackConfig.ReplaceDomeLayerFireCounters(
        new Dictionary<string, int> {
        [playbackLayer.InstanceId] = 2,
      });
      playbackVisualizer.Visualize();
      Assert(playbackVisualizer.PlaybackActive &&
          Math.Abs(
            playbackVisualizer.PlaybackStartOffset - stoppedOffset) < 1e-6,
        "astronomy Play did not resume from the stopped offset");

      Vector3 northAtZero = AstronomySky.ToDome(Vector3.UnitY, 0);
      Vector3 eastAtZero = AstronomySky.ToDome(Vector3.UnitX, 0);
      Vector3 northAtNinety = AstronomySky.ToDome(Vector3.UnitY, 90);
      Assert(Vector3.Distance(northAtZero, Vector3.UnitY) < 1e-6f,
        "zero heading did not put north on projected +Y");
      Assert(Vector3.Distance(eastAtZero, Vector3.UnitX) < 1e-6f,
        "zero heading did not put east on projected +X");
      Assert(Vector3.Distance(northAtNinety, Vector3.UnitX) < 1e-6f,
        "clockwise north heading did not rotate toward projected +X");

      double julianDay = AstronomySky.JulianDay(baseTime);
      AstronomyBody[] bodies = AstronomySky.Bodies(julianDay);
      Assert(bodies.Length == 5 &&
        bodies[0].Name == "Sun" && bodies[1].Name == "Moon" &&
        bodies[2].Name == "Mercury" && bodies[3].Name == "Venus" &&
        bodies[4].Name == "Mars",
        "astronomy body set changed");
      foreach (AstronomyBody body in bodies) {
        Assert(float.IsFinite(body.Equatorial.X) &&
          float.IsFinite(body.Equatorial.Y) &&
          float.IsFinite(body.Equatorial.Z),
          body.Name + " produced a non-finite position");
        Assert(Math.Abs(body.Equatorial.Length() - 1) < 1e-5,
          body.Name + " position was not normalized");
      }
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

      declared[0] = null;
      Assert(plan.Layers[0].RequiredInputs.Length == 2,
        "duplicate input requirements survived compilation");
      Assert(ReferenceEquals(plan.Layers[0].RequiredInputs[0], first) &&
        ReferenceEquals(plan.Layers[0].RequiredInputs[1], second),
        "the plan retained the renderer's mutable input array");
    }

    private static void ConfigurationPublishesSnapshot() {
      var config = ConfigurationWithLayers(Layer("background", null));
      var source = (ILayerStackSnapshotSource)config;
      LayerStackSnapshot published = source.DomeLayerStackSnapshot;
      DomeLayerView dto = config.domeLayerStack[0];

      Assert(!string.IsNullOrWhiteSpace(dto.InstanceId),
        "the publication boundary did not assign an instance ID");
      Assert(published.Layers[0].Id.Value == dto.InstanceId,
        "the DTO and immutable snapshot received different identities");
      DomeLayerView changed = dto with { Enabled = false };
      Assert(published.Layers[0].Enabled &&
          config.domeLayerStack[0].Enabled && !changed.Enabled,
        "the published snapshot retained a mutable view");
    }

    private static void ShowStateTransactionsAreAtomic() {
      var colors = new LEDColor[DomePalette.SlotCount];
      colors[0] = new LEDColor(0x112233, 0x445566);
      var config = new global::Spectrum.SpectrumConfiguration {
        domeGlobalFadeSpeed = 0.25,
        domeGlobalHueSpeed = 0.5,
      };
      config.ReplaceDomeLayerStack(new[] {
        Layer("background", "old-layer"),
      });
      config.ReplaceDomePalettes(new[] {
        new DomePalette { Name = "Live", Colors = colors },
      });
      var source = (IDomeShowStateConfiguration)config;
      DomeShowStateSnapshot beforePaletteEdit =
        source.DomeShowStateSnapshot;

      new PaletteService(config).ReplaceColors(
        "Live", new[] { new LEDColor(0xAABBCC) });
      DomeShowStateSnapshot afterPaletteEdit = source.DomeShowStateSnapshot;
      Assert(afterPaletteEdit.Generation > beforePaletteEdit.Generation,
        "an in-place palette edit did not publish a new generation");
      Assert(beforePaletteEdit.Palettes[0].GetSingleColor(0) == 0x112233 &&
          afterPaletteEdit.Palettes[0].GetSingleColor(0) == 0xAABBCC,
        "a show-state snapshot retained mutable palette objects");

      config.ReplaceDomeScenes(new List<DomeScene> {
        new DomeScene {
          Name = "Next",
          Layers = new List<DomeLayerSettings> {
            Layer("radial", "new-layer"),
          },
          GlobalFadeSpeed = 0.75,
          GlobalHueSpeed = 1.5,
        },
      });
      int generationNotifications = 0;
      int compatibilityNotifications = 0;
      config.PropertyChanged += (sender, e) => {
        if (e.PropertyName ==
            DomeShowStateSnapshot.NotificationPropertyName) {
          generationNotifications++;
          return;
        }
        if (e.PropertyName == nameof(config.domeLayerStack) ||
            e.PropertyName == nameof(config.domeGlobalFadeSpeed) ||
            e.PropertyName == nameof(config.domeGlobalHueSpeed)) {
          compatibilityNotifications++;
          Assert(config.domeLayerStack[0].InstanceId == "new-layer" &&
              config.domeGlobalFadeSpeed == 0.75 &&
              config.domeGlobalHueSpeed == 1.5,
            "a subscriber observed a partially applied scene");
        }
      };

      (bool applied, string error) = new SceneService(
        config, DomeLayerCatalog.Metadata).Apply("Next");
      DomeShowStateSnapshot appliedState = source.DomeShowStateSnapshot;
      Assert(applied, error);
      Assert(generationNotifications == 1 &&
          compatibilityNotifications == 3,
        "scene recall did not publish exactly one show generation");
      Assert(appliedState.LayerStack.Layers[0].Id.Value == "new-layer" &&
          appliedState.GlobalFadeSpeed == 0.75 &&
          appliedState.GlobalHueSpeed == 1.5 &&
          appliedState.Palettes[0].GetSingleColor(0) == 0xAABBCC,
        "the recalled generation mixed old and new show values");
    }

    private static void ShowStateSseIsAtomic() {
      var config = new global::Spectrum.SpectrumConfiguration {
        domeGlobalFadeSpeed = 0.1,
        domeGlobalHueSpeed = 0.2,
      };
      config.ReplaceDomeLayerStack(new[] {
        Layer("background", "sse-old"),
      });
      config.ReplaceDomePalettes(new[] {
        new DomePalette {
          Name = "SSE",
          Colors = new[] { new LEDColor(0x123456) },
        },
      });
      config.ReplaceDomeScenes(new[] {
        new DomeScene {
          Name = "SSE Next",
          Layers = new List<DomeLayerSettings> {
            Layer("background", "sse-new"),
          },
          GlobalFadeSpeed = 0.8,
          GlobalHueSpeed = 1.2,
        },
      });
      using var stream = new global::Spectrum.Web.ConfigEventStream(
        global::Spectrum.Web.SpectrumParameters.BuildRegistry(),
        config, null, null, null);
      global::Spectrum.Web.ConfigEventStream.Subscriber subscriber =
        stream.Subscribe(ControlRole.Maintenance, out Guid id);

      (bool applied, string error) =
        new SceneService(
          config, DomeLayerCatalog.Metadata).Apply("SSE Next");
      Assert(applied, error);
      var frames = new List<string>();
      while (subscriber.Reader.TryRead(out string frame)) {
        frames.Add(frame);
      }
      Assert(frames.Count == 1 &&
          frames[0].Contains("\"kind\":\"show\"") &&
          frames[0].Contains("sse-new") &&
          frames[0].Contains("\"globalFadeSpeed\":0.8") &&
          frames[0].Contains("\"globalHueSpeed\":1.2"),
        "SSE exposed a compound show update as intermediate frames");
      stream.Unsubscribe(id);
    }

    private static void InitialShowStateSseIsAtomic() {
      var oldColors = new LEDColor[DomePalette.SlotCount];
      oldColors[0] = new LEDColor(0x112233);
      var config = new global::Spectrum.SpectrumConfiguration {
        domeGlobalFadeSpeed = 0.125,
        domeGlobalHueSpeed = 0.25,
      };
      config.ReplaceDomeLayerStack(new[] {
        Layer("background", "initial-old"),
      });
      config.ReplaceDomePalettes(new[] {
        new DomePalette { Name = "Old", Colors = oldColors },
      });
      DomeShowStateSnapshot captured = null;
      int captureCount = 0;
      using var snapshotCaptured = new ManualResetEventSlim();
      using var continueSerialization = new ManualResetEventSlim();
      using var stream = new global::Spectrum.Web.ConfigEventStream(
        global::Spectrum.Web.SpectrumParameters.BuildRegistry(),
        config, null, null, null,
        snapshot => {
          if (Interlocked.Increment(ref captureCount) != 1) {
            return;
          }
          captured = snapshot;
          snapshotCaptured.Set();
          continueSerialization.Wait();
        });

      Task<List<string>> initialFrames =
        Task.Run(() => stream.InitialStateFrames());
      bool didCapture = snapshotCaptured.Wait(TimeSpan.FromSeconds(2));
      if (!didCapture) {
        continueSerialization.Set();
      }
      Assert(didCapture,
        "initial SSE serialization did not capture the show snapshot");

      var newColors = new LEDColor[DomePalette.SlotCount];
      newColors[0] = new LEDColor(0xAABBCC);
      ((IDomeShowStateConfiguration)config).ApplyDomeShowState(
        new DomeShowStateUpdate(
          new List<DomeLayerSettings> {
            Layer("background", "initial-new"),
          },
          new List<DomePalette> {
            new DomePalette { Name = "New", Colors = newColors },
          },
          0.75,
          1.5,
          DomeSceneView.ToScenes(config.domeScenes)));
      continueSerialization.Set();

      string show = initialFrames.GetAwaiter().GetResult().Single(
        frame => frame.Contains("\"kind\":\"show\""));
      Assert(show.Contains(
            "\"generation\":" + captured.Generation) &&
          show.Contains("initial-old") &&
          show.Contains("#112233") &&
          show.Contains("\"globalFadeSpeed\":0.125") &&
          show.Contains("\"globalHueSpeed\":0.25") &&
          !show.Contains("initial-new") &&
          !show.Contains("#AABBCC"),
        "an initial SSE frame mixed fields from two show generations");
    }

    private static void ConfigurationMutationsUseStateDispatcher() {
      var config = new global::Spectrum.SpectrumConfiguration();
      var dispatcher = new QueuedStateDispatcher();
      config.AttachMutationDispatcher(dispatcher);
      int notificationThread = -1;
      int notifications = 0;
      config.PropertyChanged += (sender, e) => {
        if (e.PropertyName == nameof(config.domeBrightness)) {
          notificationThread = Environment.CurrentManagedThreadId;
          notifications++;
        }
      };

      Task.Run(() => config.domeBrightness = 0.75).GetAwaiter().GetResult();
      Assert(Math.Abs(config.domeBrightness - 0.1) < 0.000001 &&
          notifications == 0 && dispatcher.PendingCount == 1,
        "an off-thread configuration write bypassed the dispatcher");

      dispatcher.Drain();
      Assert(Math.Abs(config.domeBrightness - 0.75) < 0.000001 &&
          notifications == 1 &&
          notificationThread == Environment.CurrentManagedThreadId,
        "PropertyChanged was not delivered on the state-owner thread");
    }

    private static void RuntimeSettingsPublishCompleteGenerations() {
      var config = new global::Spectrum.SpectrumConfiguration();
      var source = (IRuntimeSettingsConfiguration)config;

      var aliasedCounters = new Dictionary<string, int> {
        ["immutable"] = 7,
      };
      config.ReplaceDomeLayerFireCounters(aliasedCounters);
      DomeRuntimeFrameSnapshot retained = source.DomeRuntimeFrameSnapshot;
      aliasedCounters["immutable"] = 99;
      Assert(retained.FireGeneration("immutable") == 7,
        "a published command snapshot retained its mutable source map");

      var aliasedCableMapping = Enumerable.Range(
        0, LEDDomeOutput.NumCables).ToArray();
      config.ReplaceDomeCableMapping(aliasedCableMapping);
      DomeOutputSettingsSnapshot retainedOutput =
        source.DomeOutputSettingsSnapshot;
      aliasedCableMapping[0] = 9;
      Assert(retainedOutput.CableMapping[0] == 0,
        "a published output snapshot retained its mutable source array");

      Exception readerFailure = null;
      int iterations = 1500;
      Task writer = Task.Run(() => {
        for (int generation = 1; generation <= iterations; generation++) {
          var counters = new Dictionary<string, int>();
          for (int layer = 0; layer < 16; layer++) {
            counters["layer-" + layer] = generation;
          }
          config.ReplaceDomeLayerFireCounters(counters);
          config.ReplaceDomeCableMapping(generation % 2 == 0
            ? Enumerable.Range(0, LEDDomeOutput.NumCables).ToArray()
            : Enumerable.Range(
                0, LEDDomeOutput.NumCables).Reverse().ToArray());
        }
      });

      while (!writer.IsCompleted && readerFailure == null) {
        try {
          DomeRuntimeFrameSnapshot runtime =
            source.DomeRuntimeFrameSnapshot;
          if (runtime.FireGenerations.Count == 16) {
            int expected = runtime.FireGenerations["layer-0"];
            foreach (int value in runtime.FireGenerations.Values) {
              Assert(value == expected,
                "a reader observed a torn fire-counter generation");
            }
          }

          var mapping = source.DomeOutputSettingsSnapshot.CableMapping;
          if (mapping.Length == LEDDomeOutput.NumCables) {
            bool identity = true;
            bool reverse = true;
            for (int i = 0; i < mapping.Length; i++) {
              identity &= mapping[i] == i;
              reverse &= mapping[i] == mapping.Length - 1 - i;
            }
            Assert(identity || reverse,
              "a reader observed a torn cable-mapping generation");
          }
        } catch (Exception error) {
          readerFailure = error;
        }
      }
      writer.GetAwaiter().GetResult();
      if (readerFailure != null) {
        throw readerFailure;
      }
    }

    private static void WebReadsUseStateDispatcher() {
      var layer = Layer("background", "web-owner-read");
      layer.RendererParams = new Dictionary<string, double> {
        ["level"] = 0.4,
      };
      var config = ConfigurationWithLayers(layer);
      var dispatcher = new QueuedStateDispatcher();
      config.AttachMutationDispatcher(dispatcher);
      var controller = new global::Spectrum.Web.LayersController(
        dispatcher, config);

      Exception directReadError = null;
      try {
        Task.Run(() => config.domeLayerStack).GetAwaiter().GetResult();
      } catch (Exception error) {
        directReadError = error;
      }
      Assert(directReadError == null,
        "an immutable off-owner configuration read was rejected");

      Task<global::Spectrum.Web.LayersController.LayersState> read =
        Task.Run(controller.StateAsync);
      Assert(dispatcher.WaitForPending(TimeSpan.FromSeconds(2)) &&
          dispatcher.PendingCount == 1 && !read.IsCompleted,
        "a compound web read bypassed the state-owner dispatcher");
      dispatcher.Drain();
      var state = read.GetAwaiter().GetResult();
      Assert(state.layers.Count == 1 &&
          state.layers[0].instanceId == "web-owner-read",
        "the owner-thread web projection returned the wrong layer state");
      state.layers[0].rendererParams["level"] = 0.9;
      Assert(Math.Abs(
          config.domeLayerStack[0].RendererParams["level"] - 0.4) < 1e-9,
        "a web DTO retained a mutable configuration alias");
    }

    private static void ControlStormAvoidsPlanWork() {
      var layers = new List<DomeLayerSettings>();
      for (int i = 0; i < StackValidator.MaxLayers; i++) {
        layers.Add(Layer("background", "storm-" + i));
      }
      var config = new global::Spectrum.SpectrumConfiguration();
      config.ReplaceDomeLayerStack(layers);
      config.ReplaceDomePalettes(new List<DomePalette> {
        new DomePalette {
          Name = "Storm",
          Colors = DomePalette.CopyColors(
            new[] { new LEDColor(0x123456) }),
        },
      });
      var runtime = new global::Spectrum.Operator(config);
      var source = (IRuntimeSettingsConfiguration)config;
      int reconciliations = runtime.LayerPlanReconciliationCount;
      RenderPlan acceptedPlan = runtime.DomeOutput.RenderPlan;

      DomeShowStateSnapshot beforeGlobal =
        ((IDomeShowStateConfiguration)config).DomeShowStateSnapshot;
      config.domeGlobalFadeSpeed = 1.25;
      DomeShowStateSnapshot afterGlobal =
        ((IDomeShowStateConfiguration)config).DomeShowStateSnapshot;
      Assert(beforeGlobal.Palettes.Equals(afterGlobal.Palettes),
        "a global-only edit recompiled the palette array");

      for (int i = 0; i < 200; i++) {
        config.domeBrightness = (i % 101) / 100.0;
        config.domeMaxBrightness = ((i + 17) % 101) / 100.0;
        config.orientationDeviceSpotlight = i % 9 - 2;
        config.ReplaceDomeLayerFireCounters(new Dictionary<string, int> {
          [layers[i % layers.Count].InstanceId] = i + 1,
        });
        config.ReplaceDomeLayerClearCounters(new Dictionary<string, int> {
          [layers[(i + 1) % layers.Count].InstanceId] = i + 1,
        });
        config.domeGlobalFadeSpeed = (i % 31) / 10.0;
        config.domeGlobalHueSpeed = (i % 29) / 10.0;
        if (i % 20 == 0) {
          new PaletteService(config).ReplaceColors(
            "Storm", new[] {
              new LEDColor((0x010101 * i) & 0xFFFFFF),
            });
        }
      }

      Assert(runtime.LayerPlanReconciliationCount == reconciliations,
        "control-only changes reconciled the layer plan");
      Assert(ReferenceEquals(runtime.DomeOutput.RenderPlan, acceptedPlan),
        "control-only changes replaced the accepted render plan");

      var environment = new global::Spectrum.ConfigurationDomeLayerEnvironment();
      var ids = layers.Select(
        layer => new LayerInstanceId(layer.InstanceId)).ToArray();
      DomeShowStateSnapshot show =
        ((IDomeShowStateConfiguration)config).DomeShowStateSnapshot;
      int checksum = 0;
      for (int warmup = 0; warmup < 100; warmup++) {
        DomeRuntimeFrameSnapshot frame = source.DomeRuntimeFrameSnapshot;
        environment.BeginOperatorFrame(show, frame);
        for (int layer = 0; layer < ids.Length; layer++) {
          checksum += environment.FireGeneration(ids[layer]);
          checksum += environment.ClearGeneration(ids[layer]);
        }
        checksum += environment.OutputBrightnessByte;
      }
      long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
      for (int frameIndex = 0; frameIndex < 1000; frameIndex++) {
        DomeRuntimeFrameSnapshot frame = source.DomeRuntimeFrameSnapshot;
        environment.BeginOperatorFrame(show, frame);
        for (int layer = 0; layer < ids.Length; layer++) {
          checksum += environment.FireGeneration(ids[layer]);
          checksum += environment.ClearGeneration(ids[layer]);
        }
        checksum += environment.OutputBrightnessByte;
      }
      long allocated =
        GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
      Assert(allocated == 0,
        "runtime frame capture allocated " + allocated + " managed bytes");
      GC.KeepAlive(checksum);
    }


    private static void MidiBindingsPublishStateCommands() {
      var config = new global::Spectrum.SpectrumConfiguration();
      var dispatcher = new QueuedStateDispatcher();
      config.AttachMutationDispatcher(dispatcher);
      var bindingConfig = new ContinuousKnobMidiBindingConfig {
        BindingName = "brightness",
        knobIndex = 7,
        configPropertyName = nameof(config.domeBrightness),
        startValue = 0,
        endValue = 1,
      };
      Binding binding = bindingConfig.GetBindings(
        config, new BeatBroadcaster(config), dispatcher)[0];

      BindingInvocation invocation =
        Task.Run(() => binding.callback(7, 0.6)).GetAwaiter().GetResult();
      Assert(Math.Abs(config.domeBrightness - 0.1) < 0.000001 &&
          dispatcher.PendingCount == 1,
        "a MIDI binding assigned configuration on its callback thread");
      dispatcher.Drain();
      invocation.Completion.GetAwaiter().GetResult();
      Assert(Math.Abs(config.domeBrightness - 0.6) < 0.000001,
        "the queued MIDI state command was not applied");
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

    private static void MagneticFieldUsesSignedCharges() {
      LayerDefinition definition = DomeLayerCatalog.Metadata.Get("magnetic-field");
      Assert(definition != null && definition.DisplayName == "Magnetic Field",
        "Magnetic Field was not registered");

      double positive = OrientationCenter.SignedUnitChargePotential(
        new System.Numerics.Vector3(-0.8f, 0.6f, 0));
      double negative = OrientationCenter.SignedUnitChargePotential(
        new System.Numerics.Vector3(0.8f, -0.6f, 0));
      double neutral = OrientationCenter.SignedUnitChargePotential(
        System.Numerics.Vector3.UnitY);
      Assert(positive > 0, "the +1 pole did not produce positive potential");
      Assert(negative < 0, "the -1 pole did not produce negative potential");
      AssertClose(0, neutral, "the equidistant plane did not cancel");
      AssertClose(positive, -negative,
        "the two charges are not antipodally symmetric");

      const int LINE_COUNT = 8;
      const double LINE_WIDTH = 0.05;
      AssertClose(1, OrientationCenter.UnitFieldLineStrength(
        System.Numerics.Vector3.UnitY, LINE_COUNT, LINE_WIDTH),
        "a field-line meridian was not fully lit");
      AssertClose(1, OrientationCenter.UnitFieldLineStrength(
        OrientationCenter.Spot, LINE_COUNT, LINE_WIDTH),
        "field lines did not converge at the +1 pole");
      AssertClose(1, OrientationCenter.UnitFieldLineStrength(
        OrientationCenter.NegSpot, LINE_COUNT, LINE_WIDTH),
        "field lines did not converge at the -1 pole");
      var betweenLines = new System.Numerics.Vector3(
        0,
        (float)Math.Cos(Math.PI / LINE_COUNT),
        (float)Math.Sin(Math.PI / LINE_COUNT));
      AssertClose(0, OrientationCenter.UnitFieldLineStrength(
        betweenLines, LINE_COUNT, LINE_WIDTH),
        "the space between field lines was painted");

      DomeLayerSettings layer = Layer(
        "magnetic-field", "magnetic-field-options");
      layer.RendererParams = new Dictionary<string, double> {
        ["strength"] = 2.25,
        ["positiveColor"] = 0x112233,
        ["negativeColor"] = 0x445566,
        ["lineCount"] = 10.9,
        ["lineWidth"] = 0.045,
      };
      MagneticFieldLayerOptions options =
        BuiltInOptions<MagneticFieldLayerOptions>(layer);
      AssertClose(2.25, options.Strength,
        "field strength did not compile into renderer options");
      Assert(options.PositiveColor == 0x112233 &&
        options.NegativeColor == 0x445566,
        "charge colors did not compile into renderer options");
      Assert(options.LineCount == 10,
        "field-line count did not preserve integer truncation");
      AssertClose(0.045, options.LineWidth,
        "field-line width did not compile into renderer options");
    }

    private static void RippleTankIsOrientationOnly() {
      LayerDefinition caustics = DomeLayerCatalog.Metadata.Get("caustics");
      LayerDefinition rippleTank = DomeLayerCatalog.Metadata.Get("ripple-tank");
      Assert(caustics != null && rippleTank != null,
        "standalone Ripple Tank was not registered");
      foreach (DomeLayerParam parameter in caustics.Parameters) {
        Assert(parameter.Key != "wakeSize" &&
          parameter.Key != "wakeStrength" && parameter.Key != "trigger",
          "Caustics retained Ripple Tank parameter " + parameter.Key);
      }
      DomeLayerParam speed = null;
      DomeLayerParam damping = null;
      foreach (DomeLayerParam parameter in rippleTank.Parameters) {
        if (parameter.Key == "speed") {
          speed = parameter;
        }
        if (parameter.Key == "damping") {
          damping = parameter;
        }
        Assert(parameter.Key != "wakeSize" &&
          parameter.Key != "wakeStrength" && parameter.Key != "trigger",
          "Ripple Tank exposes removable wake tuning or a trigger control");
      }
      Assert(speed != null && speed.Max == 3,
        "Ripple Tank wave-speed slider does not cap at 3");
      Assert(damping != null && damping.Min == 0.02 &&
        damping.Max == 0.05 && damping.Step == 0.01 &&
        damping.Default == 0.02,
        "Ripple Tank damping slider is missing or malformed");

      DomeLayerSettings dampingLayer = Layer(
        "ripple-tank", "tank-damping-options");
      dampingLayer.RendererParams = new Dictionary<string, double> {
        ["damping"] = 0.04,
      };
      AssertClose(0.04,
        BuiltInOptions<RippleTankLayerOptions>(dampingLayer).Damping,
        "Ripple Tank damping did not compile into renderer options");

      double baseline =
        LEDDomeRippleTankVisualizer.WakeStrengthForAngularSpeed(0);
      double slow =
        LEDDomeRippleTankVisualizer.WakeStrengthForAngularSpeed(1);
      double fast =
        LEDDomeRippleTankVisualizer.WakeStrengthForAngularSpeed(2);
      Assert(baseline > 0,
        "slow tank motion does not have a visible wake baseline");
      Assert(slow > baseline && fast > slow,
        "wake height does not increase with sensor angular speed");
      AssertClose(1,
        LEDDomeRippleTankVisualizer.WakeStrengthForAngularSpeed(99),
        "wake strength does not cap at the stable maximum");

      var config = ConfigurationWithLayers(
        Layer("caustics", "caustics-inputs"),
        Layer("ripple-tank", "tank-inputs"));
      var runtime = new global::Spectrum.Operator(config);
      DomeLayerVisualizer causticsRenderer = null;
      DomeLayerVisualizer tankRenderer = null;
      foreach (Visualizer visualizer in runtime.DomeOutput.GetVisualizers()) {
        if (visualizer is not DomeLayerVisualizer layer) {
          continue;
        }
        if (layer.LayerKey == "caustics") {
          causticsRenderer = layer;
        } else if (layer.LayerKey == "ripple-tank") {
          tankRenderer = layer;
        }
      }
      Assert(causticsRenderer != null &&
        causticsRenderer.GetInputs().Length == 0,
        "Caustics still activates an input");
      Assert(tankRenderer != null && tankRenderer.GetInputs().Length == 1 &&
        ReferenceEquals(
          tankRenderer.GetInputs()[0], runtime.OrientationInput),
        "Ripple Tank is not bound exclusively to OrientationInput");
    }

    private static void WatchfulIrisBehavesAsSceneCharacter() {
      LayerDefinition definition = DomeLayerCatalog.Metadata.Get("watchful-iris");
      Assert(definition != null && definition.DisplayName == "Watchful Iris",
        "Watchful Iris was not registered");
      Assert(definition.FireAction?.Label == "Blink",
        "Watchful Iris manual blink action was not registered");

      WatchfulIrisLayerOptions defaults =
        BuiltInOptions<WatchfulIrisLayerOptions>(
          Layer("watchful-iris", "watchful-iris-defaults"));
      Assert(defaults.IrisComplexity == 14 &&
          defaults.PupilSize == 0.28 && defaults.DilationGain == 0.28 &&
          defaults.BlinkTrigger == 2 && defaults.EyelidSoftness == 0.035 &&
          defaults.ScleraBrightness == 1 && defaults.Palette == 0,
        "unexpected Watchful Iris defaults");
      Assert(definition.Parameters.All(
          parameter => parameter.Key != "trackingMode"),
        "Watchful Iris still exposes a tracking mode");

      DomeLayerSettings configured = Layer(
        "watchful-iris", "watchful-iris-clamped");
      configured.RendererParams = new Dictionary<string, double> {
        ["irisComplexity"] = 99,
        ["pupilSize"] = -1,
        ["dilationGain"] = 99,
        ["blinkTrigger"] = 99,
        ["eyelidSoftness"] = 99,
        ["scleraBrightness"] = 99,
        ["palette"] = 99,
      };
      WatchfulIrisLayerOptions clamped =
        BuiltInOptions<WatchfulIrisLayerOptions>(configured);
      Assert(clamped.IrisComplexity == 32 && clamped.PupilSize == 0.08 &&
          clamped.DilationGain == 0.8 && clamped.BlinkTrigger == 2 &&
          clamped.EyelidSoftness == 0.18 && clamped.ScleraBrightness == 2 &&
          clamped.Palette == PaletteService.MaxPalettes - 1,
        "Watchful Iris controls did not clamp");

      AssertClose(0.28,
        LEDDomeWatchfulIrisVisualizer.EffectivePupilRatio(0.28, 0.28, 0),
        "quiet Watchful Iris pupil changed its configured size");
      AssertClose(0.56,
        LEDDomeWatchfulIrisVisualizer.EffectivePupilRatio(0.28, 0.28, 1),
        "Watchful Iris dilation did not respond to audio");
      AssertClose(1, LEDDomeWatchfulIrisVisualizer.BlinkOpenness(0),
        "Watchful Iris blink did not begin open");
      AssertClose(0, LEDDomeWatchfulIrisVisualizer.BlinkOpenness(0.23),
        "Watchful Iris blink did not fully close");
      AssertClose(1, LEDDomeWatchfulIrisVisualizer.BlinkOpenness(0.46),
        "Watchful Iris blink did not reopen");
      Assert(LEDDomeWatchfulIrisVisualizer.ApertureCoverage(
          0, 0.1, 0, 0.01) == 0 &&
          LEDDomeWatchfulIrisVisualizer.ApertureCoverage(
            0, 0, 1, 0.01) == 1,
        "Watchful Iris eyelids did not mask the eye aperture");

      double simpleFiber = LEDDomeWatchfulIrisVisualizer.IrisFilament(
        0.64, 0.37, 5);
      double complexFiber = LEDDomeWatchfulIrisVisualizer.IrisFilament(
        0.64, 0.37, 25);
      Assert(simpleFiber >= 0 && simpleFiber <= 1 &&
          complexFiber >= 0 && complexFiber <= 1 &&
          Math.Abs(simpleFiber - complexFiber) > 0.01,
        "Watchful Iris complexity did not alter its bounded filament field");
      Assert(LEDDomeWatchfulIrisVisualizer.TrackingOffset(
          Quaternion.Identity).X < -0.3 &&
          LEDDomeWatchfulIrisVisualizer.TrackingOffset(
            Quaternion.CreateFromAxisAngle(
              Vector3.UnitZ, (float)Math.PI)).X > 0.3,
        "Watchful Iris did not follow the shared orientation center");

      Vector3 dramaticFacing =
        LEDDomeWatchfulIrisVisualizer.FacingFromGaze(
          new Vector2(0.31f, 0.18f));
      Assert(dramaticFacing.X > 0.5 && dramaticFacing.Y > 0.35 &&
          dramaticFacing.Z < 0.75 &&
          Math.Abs(dramaticFacing.Length() - 1) < 0.000001,
        "Watchful Iris did not lift its gaze into a dramatic spherical turn");
      Vector3 laggedFacing = LEDDomeWatchfulIrisVisualizer.SmoothFacing(
        Vector3.UnitZ, dramaticFacing, 0.1, 0.42);
      double laggedTurn = Math.Acos(Math.Clamp(laggedFacing.Z, -1, 1));
      double targetTurn = Math.Acos(Math.Clamp(dramaticFacing.Z, -1, 1));
      Assert(laggedTurn > 0 && laggedTurn < targetTurn,
        "Watchful Iris globe did not pursue its iris with inertia");
      Quaternion globeRotation =
        LEDDomeWatchfulIrisVisualizer.RotationFromForward(laggedFacing);
      Assert(Vector3.Distance(
          Vector3.Transform(Vector3.UnitZ, globeRotation),
          laggedFacing) < 0.000001,
        "Watchful Iris globe rotation did not face its lagged gaze");
      Quaternion pursuedRotation =
        LEDDomeWatchfulIrisVisualizer.SmoothGlobeRotation(
          Quaternion.Identity, dramaticFacing, 0.1, 0.34);
      Vector3 pursuedFacing = Vector3.Transform(
        Vector3.UnitZ, pursuedRotation);
      Vector3 expectedPursuit =
        LEDDomeWatchfulIrisVisualizer.SmoothFacing(
          Vector3.UnitZ, dramaticFacing, 0.1, 0.34);
      Assert(Vector3.Distance(pursuedFacing, expectedPursuit) < 0.000001,
        "Watchful Iris full globe orientation did not pursue its gaze");

      // Turning through two different axes must carry the first turn's
      // orientation forward. Rebuilding a minimal forward rotation would map
      // the pole correctly but discard this spherical transport/torsion.
      Vector3 rightFacing =
        LEDDomeWatchfulIrisVisualizer.FacingFromGaze(
          new Vector2(0.31f, 0));
      Vector3 upperFacing =
        LEDDomeWatchfulIrisVisualizer.FacingFromGaze(
          new Vector2(0, 0.18f));
      Quaternion transported =
        LEDDomeWatchfulIrisVisualizer.SmoothGlobeRotation(
          Quaternion.Identity, rightFacing, 0.1, 0);
      transported = LEDDomeWatchfulIrisVisualizer.SmoothGlobeRotation(
        transported, upperFacing, 0.1, 0);
      Quaternion rebuilt =
        LEDDomeWatchfulIrisVisualizer.RotationFromForward(upperFacing);
      Assert(Vector3.Distance(
          Vector3.Transform(Vector3.UnitZ, transported),
          upperFacing) < 0.000001 &&
          Vector3.Distance(
            Vector3.Transform(Vector3.UnitX, transported),
            Vector3.Transform(Vector3.UnitX, rebuilt)) > 0.01,
        "Watchful Iris sclera discarded its transported globe orientation");

      // At a nearly closed openness, the thin visible seam must travel to the
      // rotated forward pole. Evaluating the same pole in fixed dome axes
      // would incorrectly leave it behind the old horizontal eyelids.
      Vector3 transportedPole = Vector3.Transform(
        Vector3.UnitZ, transported);
      Vector3 localPole =
        LEDDomeWatchfulIrisVisualizer.GlobeLocalPosition(
          transportedPole, transported);
      double transportedClosure =
        LEDDomeWatchfulIrisVisualizer.ApertureCoverage(
          localPole.X, localPole.Y, 0.15, 0.01);
      double fixedClosure =
        LEDDomeWatchfulIrisVisualizer.ApertureCoverage(
          transportedPole.X, transportedPole.Y, 0.15, 0.01);
      Assert(Vector3.Distance(localPole, Vector3.UnitZ) < 0.000001 &&
          transportedClosure > 0.99 && fixedClosure < 0.01,
        "Watchful Iris blink seam did not rotate to the globe's new meridian");
      Vector3 vesselPoint = Vector3.Normalize(
        new Vector3(0.93f, -0.27f, 0));
      Assert(LEDDomeWatchfulIrisVisualizer.ScleraVascularStrength(
          vesselPoint) > 0.65 &&
          LEDDomeWatchfulIrisVisualizer.ScleraVascularStrength(
            Vector3.UnitZ) < 0.01,
        "Watchful Iris lacks visible globe-anchored rotation landmarks");
      Assert(LEDDomeWatchfulIrisVisualizer.ScaleScleraColor(
          0x804020, 0) == 0 &&
          LEDDomeWatchfulIrisVisualizer.ScaleScleraColor(
            0x804020, 0.5) == 0x402010 &&
          LEDDomeWatchfulIrisVisualizer.ScaleScleraColor(
            0xC08040, 2) == 0xFFFF80,
        "Watchful Iris sclera brightness did not scale and clip RGB");

      var onset = new IrisTransientDetector(0.48, 0.14);
      Assert(!onset.Sample(0.1, 0.016) && onset.Sample(0.8, 0.016) &&
          !onset.Sample(0.8, 0.016),
        "Watchful Iris audio onset did not emit one blink edge");

      var config = ConfigurationWithLayers(
        Layer("watchful-iris", "watchful-iris-render"));
      SetPaletteColors(config, color => 0x205090 + color * 0x160C02);
      var runtime = new global::Spectrum.Operator(config);
      DomeLayerVisualizer iris = null;
      foreach (Visualizer visualizer in runtime.DomeOutput.GetVisualizers()) {
        if (visualizer is DomeLayerVisualizer layer &&
            layer.LayerKey == "watchful-iris") {
          iris = layer;
          break;
        }
      }
      Assert(iris != null, "Watchful Iris renderer was not created");
      Input[] inputs = iris.GetInputs();
      Assert(inputs.Length == 2 &&
          ReferenceEquals(inputs[0], runtime.AudioInput) &&
          ReferenceEquals(inputs[1], runtime.OrientationInput),
        "Watchful Iris did not declare audio and orientation inputs");
      ((Visualizer)iris).Visualize();
      Assert(iris.LayerBuffer.pixels.Any(pixel => pixel.color != 0) &&
          iris.LayerBuffer.pixels
            .Select(pixel => pixel.color).Distinct().Count() > 12,
        "Watchful Iris did not render a patterned eye");
      Assert(iris.LayerBuffer.pixels.Any(pixel => {
          int color = pixel.color;
          return ((color >> 16) & 0xFF) + ((color >> 8) & 0xFF)
            + (color & 0xFF) < 30;
        }) && iris.LayerBuffer.pixels.Any(pixel => {
          int color = pixel.color;
          return ((color >> 16) & 0xFF) + ((color >> 8) & 0xFF)
            + (color & 0xFF) > 560;
        }),
        "Watchful Iris did not separate its dark pupil/lids and light sclera");
    }

    private static void LivingSkinUsesReactionDiffusion() {
      LayerDefinition definition = DomeLayerCatalog.Metadata.Get("living-skin");
      Assert(definition != null && definition.DisplayName == "Living Skin",
        "Living Skin was not registered");
      Assert(definition.FireAction?.Label == "Fire" &&
          definition.ClearAction?.Label == "Clear",
        "Living Skin seed/clear actions were not registered");

      LivingSkinLayerOptions defaults =
        BuiltInOptions<LivingSkinLayerOptions>(
          Layer("living-skin", "living-skin-defaults"));
      AssertClose(0.0367, defaults.FeedRate,
        "unexpected Living Skin feed rate");
      AssertClose(0.0649, defaults.KillRate,
        "unexpected Living Skin kill rate");
      AssertClose(2, defaults.DiffusionScale,
        "unexpected Living Skin diffusion scale");
      AssertClose(1, defaults.SimulationSpeed,
        "unexpected Living Skin simulation speed");
      Assert(defaults.SeedSource == 1 && defaults.EdgeContrast == 3 &&
          defaults.FeedButton == 1 && defaults.PoisonButton == 2 &&
          defaults.EraseButton == 3 && defaults.BrushRadius == 2 &&
          defaults.BrushStrength == 0.35 && defaults.Palette == 0,
        "unexpected Living Skin seed, edge, brush, or palette default");

      DomeLayerSettings configured = Layer(
        "living-skin", "living-skin-clamped");
      configured.RendererParams = new Dictionary<string, double> {
        ["feedRate"] = -1,
        ["killRate"] = 1,
        ["diffusionScale"] = 99,
        ["simulationSpeed"] = -1,
        ["seedSource"] = 99,
        ["edgeContrast"] = 99,
        ["feedButton"] = 99,
        ["poisonButton"] = -1,
        ["eraseButton"] = 99,
        ["brushRadius"] = 99,
        ["brushStrength"] = -1,
        ["palette"] = 99,
      };
      LivingSkinLayerOptions clamped =
        BuiltInOptions<LivingSkinLayerOptions>(configured);
      AssertClose(0.01, clamped.FeedRate,
        "Living Skin feed rate did not clamp");
      AssertClose(0.08, clamped.KillRate,
        "Living Skin kill rate did not clamp");
      Assert(clamped.DiffusionScale == 4 &&
          clamped.SimulationSpeed == 0 && clamped.SeedSource == 1 &&
          clamped.EdgeContrast == 8 && clamped.FeedButton == 3 &&
          clamped.PoisonButton == 0 && clamped.EraseButton == 3 &&
          clamped.BrushRadius == 4 && clamped.BrushStrength == 0.05 &&
          clamped.Palette == PaletteService.MaxPalettes - 1,
        "Living Skin controls did not clamp");

      Assert(LEDDomeLivingSkinVisualizer.BrushModeForButton(1, defaults) ==
          LivingSkinBrushMode.Feed &&
          LEDDomeLivingSkinVisualizer.BrushModeForButton(2, defaults) ==
          LivingSkinBrushMode.Poison &&
          LEDDomeLivingSkinVisualizer.BrushModeForButton(3, defaults) ==
          LivingSkinBrushMode.Erase &&
          LEDDomeLivingSkinVisualizer.BrushModeForButton(0, defaults) == null,
        "Living Skin button bindings did not select the configured brushes");

      const int size = 15;
      var simulation = new LivingSkinSimulation(
        new DomeFrame(GridTopology(size, size, 0.02)));
      simulation.Initialize(0);
      int center = size / 2 * size + size / 2;
      int frontier = center + 3;
      AssertClose(1, simulation.ChemicalAAt(center),
        "Living Skin clear state did not restore chemical A");
      AssertClose(0, simulation.ChemicalBAt(center),
        "Living Skin clear state retained chemical B");

      simulation.SeedAt(center);
      Assert(simulation.ChemicalBAt(center) > 0.9,
        "Living Skin seed did not inject chemical B");
      AssertClose(0, simulation.ChemicalBAt(frontier),
        "Living Skin seed escaped its injection patch");

      simulation.Clear();
      simulation.BrushAt(center, 1, 1, LivingSkinBrushMode.Feed);
      AssertClose(1, simulation.ChemicalAAt(center),
        "Living Skin feed brush did not restore substrate");
      Assert(simulation.ChemicalBAt(center) > 0,
        "Living Skin feed brush did not paint an activator trace");
      AssertClose(0, simulation.ChemicalBAt(frontier),
        "Living Skin feed brush escaped its bounded radius");
      simulation.BrushAt(center, 1, 1, LivingSkinBrushMode.Poison);
      AssertClose(0, simulation.ChemicalAAt(center),
        "Living Skin poison brush did not starve substrate");
      Assert(simulation.ChemicalBAt(center) < 0.05,
        "Living Skin poison brush did not suppress the activator");
      simulation.BrushAt(center, 1, 1, LivingSkinBrushMode.Erase);
      AssertClose(1, simulation.ChemicalAAt(center),
        "Living Skin erase brush did not restore dormant substrate");
      AssertClose(0, simulation.ChemicalBAt(center),
        "Living Skin erase brush retained activator");

      simulation.SeedAt(center);
      simulation.Step(0.0367, 0.0649, 1);
      Assert(simulation.ChemicalBAt(frontier) > 0,
        "Living Skin did not diffuse chemical B to a neighbor");

      for (int step = 0; step < 100; step++) {
        simulation.Step(0.0367, 0.0649, 1);
      }
      int livingPixels = 0;
      for (int i = 0; i < size * size; i++) {
        double a = simulation.ChemicalAAt(i);
        double b = simulation.ChemicalBAt(i);
        Assert(double.IsFinite(a) && double.IsFinite(b) &&
            a >= 0 && a <= 1 && b >= 0 && b <= 1,
          "Living Skin chemical state left its finite unit bounds");
        if (b > 0.01) {
          livingPixels++;
        }
      }
      Assert(livingPixels > 1,
        "Living Skin reaction collapsed instead of persisting");
      simulation.Clear();
      simulation.Step(0.0367, 0.0649, 1);
      for (int i = 0; i < size * size; i++) {
        AssertClose(1, simulation.ChemicalAAt(i),
          "Living Skin dormant A field evolved after clear");
        AssertClose(0, simulation.ChemicalBAt(i),
          "Living Skin dormant B field evolved after clear");
      }

      var config = ConfigurationWithLayers(
        Layer("living-skin", "living-skin-inputs"));
      var runtime = new global::Spectrum.Operator(config);
      DomeLayerVisualizer livingSkin = null;
      foreach (Visualizer visualizer in runtime.DomeOutput.GetVisualizers()) {
        if (visualizer is DomeLayerVisualizer layer &&
            layer.LayerKey == "living-skin") {
          livingSkin = layer;
          break;
        }
      }
      Assert(livingSkin != null, "Living Skin renderer was not created");
      Input[] livingSkinInputs = livingSkin.GetInputs();
      Assert(livingSkinInputs.Length == 1 &&
          ReferenceEquals(livingSkinInputs[0], runtime.OrientationInput),
        "Living Skin did not declare its wand-orientation input");
      ((Visualizer)livingSkin).Visualize();
    }

    private static void ArcLightningUsesPhysicalGraph() {
      LayerDefinition definition = DomeLayerCatalog.Metadata.Get("arc-lightning");
      Assert(definition != null && definition.DisplayName == "Arc Lightning",
        "Arc Lightning was not registered");
      Assert(definition.FireAction?.Label == "Fire" &&
          definition.ClearAction?.Label == "Clear",
        "Arc Lightning strike/clear actions were not registered");

      ArcLightningLayerOptions defaults =
        BuiltInOptions<ArcLightningLayerOptions>(
          Layer("arc-lightning", "arc-lightning-defaults"));
      Assert(defaults.BranchCount == 4,
        "unexpected Arc Lightning branch count");
      AssertClose(0.65, defaults.Jaggedness,
        "unexpected Arc Lightning jaggedness");
      Assert(defaults.Width == 2,
        "unexpected Arc Lightning width");
      AssertClose(0.4, defaults.Afterglow,
        "unexpected Arc Lightning afterglow");
      AssertClose(0.25, defaults.Duration,
        "unexpected Arc Lightning duration");
      Assert(defaults.Trigger == 1 && defaults.Button == 0 &&
          defaults.Level == 0.3 && defaults.Interval == 800 &&
          defaults.Palette == 0,
        "unexpected Arc Lightning trigger or palette default");

      DomeLayerSettings configured = Layer(
        "arc-lightning", "arc-lightning-clamped");
      configured.RendererParams = new Dictionary<string, double> {
        ["branchCount"] = 99,
        ["jaggedness"] = -1,
        ["width"] = 99,
        ["afterglow"] = -1,
        ["duration"] = 9,
        ["trigger"] = 99,
        ["button"] = 99,
        ["level"] = 9,
        ["interval"] = 1,
        ["palette"] = 99,
      };
      ArcLightningLayerOptions clamped =
        BuiltInOptions<ArcLightningLayerOptions>(configured);
      Assert(clamped.BranchCount == 12 && clamped.Jaggedness == 0 &&
          clamped.Width == 4 && clamped.Afterglow == 0 &&
          clamped.Duration == 1.5,
        "Arc Lightning shape/lifecycle controls did not clamp");
      Assert(clamped.Trigger == 2 && clamped.Button == 3 &&
          clamped.Level == 1 && clamped.Interval == 50 &&
          clamped.Palette == PaletteService.MaxPalettes - 1,
        "Arc Lightning trigger controls did not clamp");

      int[,] edges = new int[,] {
        { 0, 1 }, { 1, 2 }, { 2, 3 },
        { 1, 4 }, { 4, 5 }, { 5, 3 },
        { 2, 6 }, { 6, 7 },
      };
      var graph = new ArcLightningGraph(edges);
      var physicalGraph = new ArcLightningGraph(StrutLayoutFactory.lines);
      Assert(physicalGraph.VertexCount == 71 &&
          physicalGraph.Edges.Length == 190,
        "Arc Lightning did not map the deployed physical dome graph");
      ArcLightningPath path = graph.Route(
        0, 3, 2, 0, new Random(7));
      Assert(path.MainVertices[0] == 0 &&
          path.MainVertices[path.MainVertices.Length - 1] == 3,
        "Arc Lightning main route lost a selected pole");
      Assert(path.MainStruts.SequenceEqual(new[] { 0, 1, 2 }),
        "zero-jaggedness Arc Lightning did not use the shortest route");
      var usedStruts = new HashSet<int>(path.MainStruts);
      foreach (ImmutableArray<int> branch in path.BranchStruts) {
        Assert(branch.Length > 0,
          "Arc Lightning emitted an empty branch");
        foreach (int strut in branch) {
          Assert(usedStruts.Add(strut),
            "Arc Lightning branch reused a main or branch strut");
        }
      }
      Assert(path.BranchStruts.Length == 2,
        "Arc Lightning did not produce the requested available branches");
      Assert(graph.FarthestVertex(0) == 7,
        "Arc Lightning fallback pole was not graph-distant");

      ImmutableArray<ArcLightningPolePair> poleFan =
        LEDDomeArcLightningVisualizer.BuildPoleFan(
          new ArcLightningPole[] {
            new ArcLightningPole(30, 4),
            new ArcLightningPole(10, 1),
            new ArcLightningPole(40, 7),
            new ArcLightningPole(20, 3),
          }, 8);
      Assert(poleFan.SequenceEqual(new ArcLightningPolePair[] {
          new ArcLightningPolePair(1, 3),
          new ArcLightningPolePair(1, 4),
          new ArcLightningPolePair(1, 7),
        }),
        "Arc Lightning did not build a deterministic multi-wand pole fan");
      ImmutableArray<ArcLightningPolePair> cappedPoleFan =
        LEDDomeArcLightningVisualizer.BuildPoleFan(
          Enumerable.Range(0, 12)
            .Select(i => new ArcLightningPole(i, i)), 8);
      Assert(cappedPoleFan.Length == 8 &&
          cappedPoleFan[0] == new ArcLightningPolePair(0, 1) &&
          cappedPoleFan[7] == new ArcLightningPolePair(0, 8),
        "Arc Lightning multi-wand pole fan exceeded its strike bound");
      Assert(LEDDomeArcLightningVisualizer.BuildPoleFan(
          Array.Empty<ArcLightningPole>(), 8).IsEmpty &&
          LEDDomeArcLightningVisualizer.BuildPoleFan(
            new[] { new ArcLightningPole(8, 2) }, 8).IsEmpty,
        "Arc Lightning pole fan replaced a one/no-wand fallback");
      ArcLightningPolePair oneWandFallback =
        LEDDomeArcLightningVisualizer.FallbackPolePair(
          graph, new[] { new ArcLightningPole(8, 2) }, new Random(3));
      Assert(oneWandFallback == new ArcLightningPolePair(
          2, graph.FarthestVertex(2)),
        "Arc Lightning one-wand fallback lost its selected origin");
      ArcLightningPolePair noWandFallback =
        LEDDomeArcLightningVisualizer.FallbackPolePair(
          graph, Array.Empty<ArcLightningPole>(), new Random(3));
      Assert(noWandFallback.Origin >= 0 &&
          noWandFallback.Origin < graph.VertexCount &&
          noWandFallback.Destination ==
            graph.FarthestVertex(noWandFallback.Origin),
        "Arc Lightning hands-off fallback did not choose a distant pole");

      Assert(LEDDomeArcLightningVisualizer.VisibleEdgeCount(10, 0, 1) == 2,
        "Arc Lightning did not reveal an origin segment immediately");
      Assert(LEDDomeArcLightningVisualizer.VisibleEdgeCount(10, 0.5, 1) == 6,
        "Arc Lightning leading edge did not advance with strike age");
      Assert(LEDDomeArcLightningVisualizer.VisibleEdgeCount(10, 1, 1) == 10,
        "Arc Lightning did not reveal its full path by strike end");
      AssertClose(0,
        LEDDomeArcLightningVisualizer.AfterglowRetention(0, 0.1),
        "zero Arc Lightning afterglow retained old light");
      AssertClose(0.5,
        LEDDomeArcLightningVisualizer.AfterglowRetention(0.4, 0.4),
        "Arc Lightning afterglow is not a brightness half-life");

      var config = ConfigurationWithLayers(
        Layer("arc-lightning", "arc-lightning-inputs"));
      var runtime = new global::Spectrum.Operator(config);
      DomeLayerVisualizer lightning = null;
      foreach (Visualizer visualizer in runtime.DomeOutput.GetVisualizers()) {
        if (visualizer is DomeLayerVisualizer layer &&
            layer.LayerKey == "arc-lightning") {
          lightning = layer;
          break;
        }
      }
      Assert(lightning != null, "Arc Lightning renderer was not created");
      Input[] inputs = lightning.GetInputs();
      Assert(inputs.Length == 2 &&
          ReferenceEquals(inputs[0], runtime.AudioInput) &&
          ReferenceEquals(inputs[1], runtime.OrientationInput),
        "Arc Lightning did not declare audio and orientation inputs");
      ((Visualizer)lightning).Visualize();
    }

    private static void GlassMosaicUsesTriangleTopology() {
      LayerDefinition definition = DomeLayerCatalog.Metadata.Get("glass-mosaic");
      Assert(definition != null && definition.DisplayName == "Glass Mosaic",
        "Glass Mosaic was not registered");
      Assert(definition.FireAction?.Label == "Fire" &&
          definition.ClearAction?.Label == "Clear",
        "Glass Mosaic Fire/Clear actions were not registered");

      GlassMosaicLayerOptions defaults =
        BuiltInOptions<GlassMosaicLayerOptions>(
          Layer("glass-mosaic", "glass-defaults"));
      Assert(defaults.TileGrouping == 1,
        "unexpected Glass Mosaic tile grouping");
      AssertClose(30, defaults.CascadeSpeed,
        "unexpected Glass Mosaic cascade speed");
      Assert(defaults.PropagationRule == 0,
        "unexpected Glass Mosaic propagation rule");
      AssertClose(0.18, defaults.BorderBrightness,
        "unexpected Glass Mosaic border brightness");
      Assert(defaults.TileTransition == 0,
        "unexpected Glass Mosaic tile transition");
      Assert(defaults.Trigger == 1 && defaults.Button == 0 &&
          defaults.Palette == 0,
        "unexpected Glass Mosaic trigger or palette default");
      AssertClose(0.3, defaults.Level,
        "unexpected Glass Mosaic audio level");
      AssertClose(800, defaults.Interval,
        "unexpected Glass Mosaic audio interval");

      DomeLayerSettings configured = Layer(
        "glass-mosaic", "glass-clamped");
      configured.RendererParams = new Dictionary<string, double> {
        ["tileGrouping"] = 99,
        ["cascadeSpeed"] = 0,
        ["propagationRule"] = 99,
        ["borderBrightness"] = 0,
        ["tileTransition"] = 99,
        ["trigger"] = 99,
        ["button"] = 99,
        ["level"] = -1,
        ["interval"] = 99999,
        ["palette"] = 99,
      };
      GlassMosaicLayerOptions clamped =
        BuiltInOptions<GlassMosaicLayerOptions>(configured);
      Assert(clamped.TileGrouping == 6 && clamped.CascadeSpeed == 1 &&
          clamped.PropagationRule == 2 &&
          clamped.BorderBrightness == 0.02 &&
          clamped.TileTransition == 1 &&
          clamped.Trigger == 2 && clamped.Button == 3 &&
          clamped.Level == 0 && clamped.Interval == 4000 &&
          clamped.Palette == PaletteService.MaxPalettes - 1,
        "Glass Mosaic controls did not clamp");

      var topology = new GlassMosaicTopology(StrutLayoutFactory.lines);
      Assert(topology.TileCount == 120 && topology.EdgeCount == 190,
        "Glass Mosaic did not discover the deployed triangular faces");
      int boundaryEdges = 0;
      for (int edge = 0; edge < topology.EdgeCount; edge++) {
        int incident = topology.EdgeAt(edge).Tiles.Length;
        Assert(incident == 1 || incident == 2,
          "Glass Mosaic found a strut outside the face manifold");
        if (incident == 1) {
          boundaryEdges++;
        }
      }
      Assert(boundaryEdges == 20,
        "Glass Mosaic did not preserve the 20-strut dome rim");
      for (int tile = 0; tile < topology.TileCount; tile++) {
        Assert(topology.TileAt(tile).Struts.Length == 3,
          "Glass Mosaic created a non-triangular tile");
        foreach (int neighbor in topology.NeighborsAt(tile)) {
          Assert(topology.NeighborsAt(neighbor).Contains(tile),
            "Glass Mosaic face adjacency was not symmetric");
        }
      }

      var state = new GlassMosaicState(topology, 17);
      Assert(state.PhaseAt(0) == 0 && state.PhaseAt(9) == 1,
        "Glass Mosaic did not initialize persistent tile phases");
      state.StartCascade(0, 1, 0);
      ImmutableArray<int> waveOrder = state.LastCascadeTileOrder;
      Assert(waveOrder.Length == topology.TileCount &&
          waveOrder.Distinct().Count() == topology.TileCount,
        "Glass Mosaic neighbor wave did not cover each tile once");
      var reached = new HashSet<int> { waveOrder[0] };
      for (int index = 1; index < waveOrder.Length; index++) {
        int tile = waveOrder[index];
        Assert(topology.NeighborsAt(tile).Any(reached.Contains),
          "Glass Mosaic cascade jumped across disconnected faces");
        reached.Add(tile);
      }
      int secondTile = waveOrder[1];
      int secondInitialPhase = secondTile & 7;
      Assert(state.PhaseAt(0) == 1 && state.PreviousPhaseAt(0) == 0 &&
          state.PulseAt(0) == 1 && state.FlipProgressAt(0) == 0 &&
          state.PhaseAt(secondTile) == secondInitialPhase,
        "Glass Mosaic did not flip only its starting tile immediately");
      GlassMosaicTileAppearance instant =
        LEDDomeGlassMosaicVisualizer.TileAppearance(0, 1, 0, 0);
      Assert(instant.Phase == 1 && instant.Visibility == 1,
        "Glass Mosaic instant transition changed legacy phase rendering");
      GlassMosaicTileAppearance oldFace =
        LEDDomeGlassMosaicVisualizer.TileAppearance(0, 1, 0.25, 1);
      GlassMosaicTileAppearance edgeOn =
        LEDDomeGlassMosaicVisualizer.TileAppearance(0, 1, 0.5, 1);
      GlassMosaicTileAppearance newFace =
        LEDDomeGlassMosaicVisualizer.TileAppearance(0, 1, 0.75, 1);
      Assert(oldFace.Phase == 0 && newFace.Phase == 1,
        "Glass Mosaic flip did not select the old and new tile faces");
      AssertClose(0.5, oldFace.Visibility,
        "Glass Mosaic old face did not narrow toward edge-on");
      AssertClose(0, edgeOn.Visibility,
        "Glass Mosaic flip did not become invisible at edge-on");
      AssertClose(0.5, newFace.Visibility,
        "Glass Mosaic new face did not open from edge-on");
      state.AdvanceFlips(GlassMosaicState.FlipDurationSeconds * 0.25);
      AssertClose(0.25, state.FlipProgressAt(0),
        "Glass Mosaic flip did not advance with elapsed time");
      state.AdvanceCascade(0.99);
      Assert(state.PhaseAt(secondTile) == secondInitialPhase,
        "Glass Mosaic cascade ignored its tile-rate budget");
      state.AdvanceCascade(0.01);
      Assert(state.PhaseAt(secondTile) == ((secondInitialPhase + 1) & 7),
        "Glass Mosaic cascade did not advance at one tile of budget");
      state.DecayPulses(0.325);
      AssertClose(0.5, state.PulseAt(0),
        "Glass Mosaic arrival pulse did not decay independently");

      state.Reset();
      state.StartCascade(0, 3, 1);
      int groupedArrivals = Enumerable.Range(0, topology.TileCount)
        .Count(tile => state.PulseAt(tile) == 1);
      Assert(groupedArrivals == 3,
        "Glass Mosaic grouping did not flip one connected tile group");
      Assert(Enumerable.Range(0, topology.TileCount)
          .Count(tile => state.FlipProgressAt(tile) == 0) == 3,
        "Glass Mosaic grouped arrivals did not begin together");
      state.StartCascade(0, 1, 2);
      Assert(state.LastCascadeTileOrder.Length == topology.TileCount &&
          state.LastCascadeTileOrder.Distinct().Count() ==
            topology.TileCount,
        "Glass Mosaic randomized domino rule lost or duplicated tiles");
      state.Reset();
      Assert(Enumerable.Range(0, topology.TileCount)
          .All(tile => state.PulseAt(tile) == 0 &&
            state.PhaseAt(tile) == (tile & 7) &&
            state.PreviousPhaseAt(tile) == (tile & 7) &&
            state.FlipProgressAt(tile) == 1),
        "Glass Mosaic Clear did not restore its initial state");

      AssertClose(0.2,
        LEDDomeGlassMosaicVisualizer.TileBrightness(0.2, 0),
        "Glass Mosaic resting border brightness changed");
      AssertClose(1,
        LEDDomeGlassMosaicVisualizer.TileBrightness(0.2, 1),
        "Glass Mosaic arrival did not reach full brightness");
      Assert(LEDDomeGlassMosaicVisualizer.BlendColor(
          0xFF0000, 0x0000FF, 0.5) == 0x800080,
        "Glass Mosaic did not interpolate adjacent tile colors");

      var config = ConfigurationWithLayers(
        Layer("glass-mosaic", "glass-inputs"));
      SetPaletteColors(config, color => 0xFFFFFF - color * 0x10101);
      var runtime = new global::Spectrum.Operator(config);
      DomeLayerVisualizer mosaic = null;
      foreach (Visualizer visualizer in runtime.DomeOutput.GetVisualizers()) {
        if (visualizer is DomeLayerVisualizer layer &&
            layer.LayerKey == "glass-mosaic") {
          mosaic = layer;
          break;
        }
      }
      Assert(mosaic != null, "Glass Mosaic renderer was not created");
      Input[] inputs = mosaic.GetInputs();
      Assert(inputs.Length == 2 &&
          ReferenceEquals(inputs[0], runtime.AudioInput) &&
          ReferenceEquals(inputs[1], runtime.OrientationInput),
        "Glass Mosaic did not declare audio and wand inputs");
      ((Visualizer)mosaic).Visualize();
      Assert(mosaic.LayerBuffer.pixels.Any(pixel => pixel.color != 0),
        "Glass Mosaic did not render its resting stained-glass borders");
    }

    private static void CellularDomeUsesTriangleCells() {
      LayerDefinition definition = DomeLayerCatalog.Metadata.Get("cellular-dome");
      Assert(definition != null && definition.DisplayName == "Cellular Dome",
        "Cellular Dome was not registered");
      Assert(definition.FireAction?.Label == "Fire" &&
          definition.ClearAction?.Label == "Clear",
        "Cellular Dome Fire/Clear actions were not registered");

      CellularDomeLayerOptions defaults =
        BuiltInOptions<CellularDomeLayerOptions>(
          Layer("cellular-dome", "cellular-defaults"));
      Assert(defaults.Rule == 0 && defaults.Neighborhood == 0,
        "unexpected Cellular Dome rule or neighborhood");
      AssertClose(6, defaults.GenerationRate,
        "unexpected Cellular Dome generation rate");
      Assert(defaults.BirthColor == 0 && defaults.TriggerMode == 0 &&
          defaults.Palette == 0,
        "unexpected Cellular Dome color, trigger, or palette");
      AssertClose(2.5, defaults.AgeDecay,
        "unexpected Cellular Dome age decay");

      DomeLayerSettings configured = Layer(
        "cellular-dome", "cellular-clamped");
      configured.RendererParams = new Dictionary<string, double> {
        ["rule"] = 99,
        ["neighborhood"] = 99,
        ["generationRate"] = -1,
        ["birthColor"] = 99,
        ["ageDecay"] = 0,
        ["triggerMode"] = 99,
        ["palette"] = 99,
      };
      CellularDomeLayerOptions clamped =
        BuiltInOptions<CellularDomeLayerOptions>(configured);
      Assert(clamped.Rule == 3 && clamped.Neighborhood == 1 &&
          clamped.GenerationRate == 0 && clamped.BirthColor == 7 &&
          clamped.AgeDecay == 0.1 && clamped.TriggerMode == 2 &&
          clamped.Palette == PaletteService.MaxPalettes - 1,
        "Cellular Dome controls did not clamp");

      var topology = new GlassMosaicTopology(StrutLayoutFactory.lines);
      bool foundWiderNeighborhood = false;
      for (int tile = 0; tile < topology.TileCount; tile++) {
        ImmutableArray<int> edgeNeighbors = topology.NeighborsAt(tile);
        ImmutableArray<int> vertexNeighbors =
          topology.VertexNeighborsAt(tile);
        Assert(edgeNeighbors.All(vertexNeighbors.Contains),
          "Cellular Dome vertex neighborhood lost an edge neighbor");
        if (vertexNeighbors.Length > edgeNeighbors.Length) {
          foundWiderNeighborhood = true;
        }
        foreach (int neighbor in vertexNeighbors) {
          Assert(topology.VertexNeighborsAt(neighbor).Contains(tile),
            "Cellular Dome vertex adjacency was not symmetric");
        }
      }
      Assert(foundWiderNeighborhood,
        "Cellular Dome neighborhoods did not produce distinct topologies");

      Assert(CellularDomeState.NextAlive(0, false, 2) &&
          CellularDomeState.NextAlive(0, true, 1) &&
          !CellularDomeState.NextAlive(0, true, 3),
        "Cellular Dome colony rule did not implement B2/S12");
      Assert(CellularDomeState.NextAlive(1, false, 1) &&
          !CellularDomeState.NextAlive(1, true, 1),
        "Cellular Dome oscillator rule did not implement B1/S0");
      Assert(CellularDomeState.NextAlive(2, true, 0) &&
          CellularDomeState.NextAlive(2, false, 1),
        "Cellular Dome front rule did not persist and expand");
      Assert(CellularDomeState.NextAlive(3, false, 3) &&
          !CellularDomeState.NextAlive(3, false, 2),
        "Cellular Dome chaos rule did not implement B13 births");

      var state = new CellularDomeState(topology);
      state.Clear();
      state.SeedAt(0, 0, 0);
      Assert(state.AliveCount == 1 && state.AliveAt(0) &&
          state.BrightnessAt(0) == 1,
        "Cellular Dome did not seed one physical face");
      int edgeCount = topology.NeighborsAt(0).Length;
      state.Step(2, 0);
      Assert(state.AliveCount == edgeCount + 1 && state.AliveAt(0) &&
          topology.NeighborsAt(0).All(state.AliveAt),
        "Cellular Dome traveling front did not cross shared struts");

      state.Clear();
      state.SeedAt(0, 1, 1);
      Assert(state.AliveCount ==
          topology.VertexNeighborsAt(0).Length + 1,
        "Cellular Dome vertex brush did not use the selected neighborhood");
      state.EraseAt(0, 1, 1);
      Assert(state.AliveCount == 0 && state.BrightnessAt(0) == 0,
        "Cellular Dome erase did not remove the colony and its light");

      state.SeedAt(0, 0, 0);
      state.AdvanceAppearance(2, 2);
      AssertClose(0.59, state.BrightnessAt(0),
        "Cellular Dome live-cell age did not decay by one half-life");
      Assert(state.ColorPhaseAt(0, 3, 2) == 4,
        "Cellular Dome age did not advance from the birth color");
      state.MutateAt(0, 0, 0);
      Assert(!state.AliveAt(0) && state.BrightnessAt(0) > 0,
        "Cellular Dome mutation did not leave a dying afterimage");
      state.AdvanceAppearance(2, 2);
      AssertClose(0.295, state.BrightnessAt(0),
        "Cellular Dome dead-cell afterimage did not decay independently");

      state.Initialize();
      Assert(state.AliveCount > 0 &&
          state.AliveCount < topology.TileCount,
        "Cellular Dome initial colonies were empty or covered the dome");
      Assert(LEDDomeCellularDomeVisualizer.BlendColor(
          0xFF0000, 0x0000FF, 0.5) == 0x800080,
        "Cellular Dome did not blend adjacent cell colors");

      var config = ConfigurationWithLayers(
        Layer("cellular-dome", "cellular-inputs"));
      SetPaletteColors(config, color => 0xFFFFFF - color * 0x10101);
      var runtime = new global::Spectrum.Operator(config);
      DomeLayerVisualizer cellular = null;
      foreach (Visualizer visualizer in runtime.DomeOutput.GetVisualizers()) {
        if (visualizer is DomeLayerVisualizer layer &&
            layer.LayerKey == "cellular-dome") {
          cellular = layer;
          break;
        }
      }
      Assert(cellular != null, "Cellular Dome renderer was not created");
      Input[] inputs = cellular.GetInputs();
      Assert(inputs.Length == 1 &&
          ReferenceEquals(inputs[0], runtime.OrientationInput),
        "Cellular Dome did not declare its wand input");
      ((Visualizer)cellular).Visualize();
      Assert(cellular.LayerBuffer.pixels.Any(pixel => pixel.color != 0),
        "Cellular Dome did not render its initial colonies");
    }

    private static void FireflySwarmUsesCoherentFlock() {
      LayerDefinition definition = DomeLayerCatalog.Metadata.Get("firefly-swarm");
      Assert(definition != null && definition.DisplayName == "Firefly Swarm",
        "Firefly Swarm was not registered");

      FireflySwarmLayerOptions defaults =
        BuiltInOptions<FireflySwarmLayerOptions>(
          Layer("firefly-swarm", "firefly-defaults"));
      Assert(defaults.Population == 48,
        "unexpected Firefly Swarm population");
      AssertClose(1.2, defaults.Cohesion,
        "unexpected Firefly Swarm cohesion");
      AssertClose(1.8, defaults.Separation,
        "unexpected Firefly Swarm separation");
      AssertClose(0.65, defaults.Wander,
        "unexpected Firefly Swarm wander");
      Assert(defaults.InteractionMode == 0,
        "unexpected Firefly Swarm interaction mode");
      AssertClose(0.055, defaults.DotSize,
        "unexpected Firefly Swarm dot size");
      AssertClose(0.45, defaults.TrailLength,
        "unexpected Firefly Swarm trail length");
      Assert(defaults.Palette == 0,
        "unexpected Firefly Swarm palette");

      DomeLayerSettings configured = Layer(
        "firefly-swarm", "firefly-clamped");
      configured.RendererParams = new Dictionary<string, double> {
        ["population"] = 999,
        ["cohesion"] = -1,
        ["separation"] = 99,
        ["wander"] = 99,
        ["interactionMode"] = 99,
        ["dotSize"] = 0,
        ["trailLength"] = 99,
        ["palette"] = 99,
      };
      FireflySwarmLayerOptions clamped =
        BuiltInOptions<FireflySwarmLayerOptions>(configured);
      Assert(clamped.Population == 160 && clamped.Cohesion == 0 &&
          clamped.Separation == 4 && clamped.Wander == 4 &&
          clamped.InteractionMode == 1 && clamped.DotSize == 0.015 &&
          clamped.TrailLength == 3 &&
          clamped.Palette == PaletteService.MaxPalettes - 1,
        "Firefly Swarm controls did not clamp");

      var flock = new FireflySwarmState(24, 23);
      Assert(flock.Agents.Count == 24,
        "Firefly Swarm did not create its requested population");
      foreach (FireflyAgent agent in flock.Agents) {
        Assert(agent.Position.Z >= 0 &&
            Math.Abs(agent.Position.Length() - 1) < 0.000001,
          "Firefly Swarm initialized off the visible unit hemisphere");
        Assert(Math.Abs(Vector3.Dot(
            agent.Position, agent.Velocity)) < 0.000001,
          "Firefly Swarm initialized non-tangent velocity");
      }
      Vector3 beforeWander = flock.Agents[0].Position;
      flock.Step(
        0.1, 0, 0, 1, 0, Array.Empty<Vector3>());
      Assert(Vector3.Distance(beforeWander, flock.Agents[0].Position) > 0,
        "Firefly Swarm wander did not move a persistent agent");
      flock.Resize(37);
      Assert(flock.Agents.Count == 37,
        "Firefly Swarm did not grow its bounded population in place");
      flock.Resize(12);
      Assert(flock.Agents.Count == 12,
        "Firefly Swarm did not shrink its bounded population in place");

      Vector3 aim = Vector3.Normalize(new Vector3(0.9f, 0.1f, 0.3f));
      Func<FireflySwarmState, double> meanAimDistance = state =>
        state.Agents.Average(agent => Math.Acos(Math.Clamp(
          Vector3.Dot(agent.Position, aim), -1, 1)));
      var attracted = new FireflySwarmState(24, 31);
      var repelled = new FireflySwarmState(24, 31);
      double initialAimDistance = meanAimDistance(attracted);
      for (int step = 0; step < 12; step++) {
        attracted.Step(0.1, 0, 0, 0, 0, new[] { aim });
        repelled.Step(0.1, 0, 0, 0, 1, new[] { aim });
      }
      Assert(meanAimDistance(attracted) < initialAimDistance &&
          meanAimDistance(repelled) > initialAimDistance,
        "Firefly Swarm wand attract/repel modes did not diverge");

      var separating = new FireflySwarmState(32, 37);
      double initialNearest = NearestFireflyDistance(separating.Agents);
      for (int step = 0; step < 12; step++) {
        separating.Step(
          0.1, 0, 4, 0, 0, Array.Empty<Vector3>());
      }
      Assert(NearestFireflyDistance(separating.Agents) > initialNearest,
        "Firefly Swarm separation did not open close spacing");

      var startled = new FireflySwarmState(32, 41);
      double clusteredSpread = startled.MeanAngularSpread();
      startled.Startle();
      for (int step = 0; step < 8; step++) {
        startled.Step(
          0.1, 0, 0, 0, 0, Array.Empty<Vector3>());
      }
      double startledSpread = startled.MeanAngularSpread();
      Assert(startledSpread > clusteredSpread,
        "Firefly Swarm startle did not disperse the group");
      for (int step = 0; step < 40; step++) {
        startled.Step(
          0.1, 4, 0, 0, 0, Array.Empty<Vector3>());
      }
      Assert(startled.MeanAngularSpread() < startledSpread,
        "Firefly Swarm cohesion did not regroup a startled flock");
      Assert(startled.Agents.All(agent => agent.Position.Z >= 0 &&
          Math.Abs(agent.Position.Length() - 1) < 0.000001),
        "Firefly Swarm escaped the visible unit hemisphere");

      var detector = new FireflyStartleDetector();
      Assert(!detector.Sample(0.1, 0.1) && detector.Sample(0.8, 0.1),
        "Firefly Swarm did not detect a loud rising transient");
      Assert(!detector.Sample(0.8, 0.1),
        "Firefly Swarm retriggered on a sustained loud level");
      for (int sample = 0; sample < 12; sample++) {
        detector.Sample(0.05, 0.1);
      }
      Assert(detector.Sample(0.8, 0.1),
        "Firefly Swarm did not re-arm after the audio envelope settled");
      AssertClose(0,
        LEDDomeFireflySwarmVisualizer.TrailRetention(0, 0.1),
        "zero Firefly Swarm trail retained old light");
      AssertClose(0.5,
        LEDDomeFireflySwarmVisualizer.TrailRetention(0.45, 0.45),
        "Firefly Swarm trail length is not a brightness half-life");

      var config = ConfigurationWithLayers(
        Layer("firefly-swarm", "firefly-inputs"));
      SetPaletteColors(config, color => 0xFFFFFF - color * 0x10101);
      var runtime = new global::Spectrum.Operator(config);
      DomeLayerVisualizer fireflies = null;
      foreach (Visualizer visualizer in runtime.DomeOutput.GetVisualizers()) {
        if (visualizer is DomeLayerVisualizer layer &&
            layer.LayerKey == "firefly-swarm") {
          fireflies = layer;
          break;
        }
      }
      Assert(fireflies != null, "Firefly Swarm renderer was not created");
      Input[] inputs = fireflies.GetInputs();
      Assert(inputs.Length == 2 &&
          ReferenceEquals(inputs[0], runtime.AudioInput) &&
          ReferenceEquals(inputs[1], runtime.OrientationInput),
        "Firefly Swarm did not declare audio and wand inputs");
      ((Visualizer)fireflies).Visualize();
      Assert(fireflies.LayerBuffer.pixels.Any(pixel => pixel.color != 0),
        "Firefly Swarm did not render its persistent flock");
    }

    private static double NearestFireflyDistance(
      IReadOnlyList<FireflyAgent> agents
    ) {
      double nearest = double.MaxValue;
      for (int first = 0; first < agents.Count; first++) {
        for (int second = first + 1; second < agents.Count; second++) {
          nearest = Math.Min(nearest, Vector3.Distance(
            agents[first].Position, agents[second].Position));
        }
      }
      return nearest;
    }

    private static void RainChamberUsesSphericalRain() {
      LayerDefinition definition = DomeLayerCatalog.Metadata.Get("rain-chamber");
      Assert(definition != null && definition.DisplayName == "Rain Chamber",
        "Rain Chamber was not registered");

      RainChamberLayerOptions defaults =
        BuiltInOptions<RainChamberLayerOptions>(
          Layer("rain-chamber", "rain-defaults"));
      AssertClose(22, defaults.RainfallRate,
        "unexpected Rain Chamber rainfall rate");
      AssertClose(1.4, defaults.Gravity,
        "unexpected Rain Chamber gravity");
      AssertClose(0.045, defaults.DropletSize,
        "unexpected Rain Chamber droplet size");
      AssertClose(0.7, defaults.TrailRetention,
        "unexpected Rain Chamber trail retention");
      Assert(defaults.InteractionMode == 0,
        "unexpected Rain Chamber wand interaction mode");
      AssertClose(1.25, defaults.Wind,
        "unexpected Rain Chamber wand strength");
      AssertClose(0.9, defaults.SplashStrength,
        "unexpected Rain Chamber splash strength");
      Assert(defaults.Palette == 0,
        "unexpected Rain Chamber palette");

      AssertClose(0.065, RainChamberState.SpawnPolar(0.015),
        "small Rain Chamber droplets lost the crown spawn radius");
      AssertClose(0.099, RainChamberState.SpawnPolar(0.045),
        "default Rain Chamber spawn radius did not follow droplet size");
      AssertClose(0.308, RainChamberState.SpawnPolar(0.14),
        "large Rain Chamber spawn radius did not follow droplet size");

      var smallSpawn = new RainChamberState(13);
      var largeSpawn = new RainChamberState(13);
      smallSpawn.Step(
        0.1, 20, 0, 0, 0.015, 0, 1, Array.Empty<Vector3>());
      largeSpawn.Step(
        0.1, 20, 0, 0, 0.14, 0, 1, Array.Empty<Vector3>());
      double smallSpawnSeparation = Math.Acos(Math.Clamp(Vector3.Dot(
        smallSpawn.Droplets[0].Position,
        smallSpawn.Droplets[1].Position), -1, 1));
      double largeSpawnSeparation = Math.Acos(Math.Clamp(Vector3.Dot(
        largeSpawn.Droplets[0].Position,
        largeSpawn.Droplets[1].Position), -1, 1));
      Assert(smallSpawn.Droplets.Count == 2 &&
          largeSpawn.Droplets.Count == 2 &&
          largeSpawnSeparation > smallSpawnSeparation * 3,
        "larger Rain Chamber droplets did not spawn farther apart");

      var smallWarmStart = new RainChamberState(13, 1, 0.015);
      var largeWarmStart = new RainChamberState(13, 1, 0.14);
      Assert(largeWarmStart.Droplets[0].Position.Z <
          smallWarmStart.Droplets[0].Position.Z,
        "initial Rain Chamber droplets ignored configured droplet size");

      DomeLayerSettings configured = Layer(
        "rain-chamber", "rain-clamped");
      configured.RendererParams = new Dictionary<string, double> {
        ["rainfallRate"] = -1,
        ["gravity"] = 99,
        ["dropletSize"] = 0,
        ["trailRetention"] = 99,
        ["interactionMode"] = 99,
        ["wind"] = 99,
        ["splashStrength"] = -1,
        ["palette"] = 99,
      };
      RainChamberLayerOptions clamped =
        BuiltInOptions<RainChamberLayerOptions>(configured);
      Assert(clamped.RainfallRate == 0 && clamped.Gravity == 4 &&
          clamped.DropletSize == 0.015 &&
          clamped.TrailRetention == 3 && clamped.InteractionMode == 2 &&
          clamped.Wind == 4 &&
          clamped.SplashStrength == 0 &&
          clamped.Palette == PaletteService.MaxPalettes - 1,
        "Rain Chamber controls did not clamp");

      AssertClose(3.3,
        RainChamberState.EffectiveRainfallRate(22, 0),
        "silent Rain Chamber did not retain a light drizzle");
      AssertClose(22,
        RainChamberState.EffectiveRainfallRate(22, 1),
        "full audio did not reach the configured rainfall rate");
      var quiet = new RainChamberState(17);
      var loud = new RainChamberState(17);
      for (int step = 0; step < 10; step++) {
        quiet.Step(0.1, 40, 0, 0, 0, 0, 0, Array.Empty<Vector3>());
        loud.Step(0.1, 40, 0, 0, 0, 0, 1, Array.Empty<Vector3>());
      }
      Assert(loud.Droplets.Count > quiet.Droplets.Count &&
          loud.Droplets.Count == 40,
        "capture volume did not scale Rain Chamber spawning");

      Vector3 upper = Vector3.Normalize(new Vector3(0.25f, 0, 0.97f));
      var falling = new RainChamberState(23);
      falling.SeedDroplet(upper, Vector3.Zero);
      double initialHeight = falling.Droplets[0].Position.Z;
      for (int step = 0; step < 10; step++) {
        falling.Step(
          0.1, 0, 1.4, 0, 0, 0, 0, Array.Empty<Vector3>());
      }
      Assert(falling.Droplets.Count == 1 &&
          falling.Droplets[0].Position.Z < initialHeight,
        "spherical gravity did not pull a droplet toward the rim");
      Assert(Math.Abs(falling.Droplets[0].Position.Length() - 1) <
          0.000001 &&
          Math.Abs(Vector3.Dot(
            falling.Droplets[0].Position,
            falling.Droplets[0].Velocity)) < 0.000001,
        "Rain Chamber escaped the tangent unit hemisphere");

      var still = new RainChamberState(29);
      var deflected = new RainChamberState(29);
      still.SeedDroplet(upper, Vector3.Zero);
      deflected.SeedDroplet(upper, Vector3.Zero);
      for (int step = 0; step < 6; step++) {
        still.Step(0.1, 0, 0, 2, 0, 0, 0, Array.Empty<Vector3>());
        deflected.Step(0.1, 0, 0, 2, 0, 0, 0, new[] { upper });
      }
      Assert(Vector3.Distance(
          still.Droplets[0].Position,
          deflected.Droplets[0].Position) > 0.01,
        "Rain Chamber umbrella did not deflect a nearby droplet");

      Vector3 sweepTangent = Vector3.Normalize(Vector3.Cross(
        Vector3.UnitY, upper));
      Vector3 sweptAim = Vector3.Normalize(
        upper * (float)Math.Cos(0.1) +
        sweepTangent * (float)Math.Sin(0.1));
      Vector3 inferredMotion =
        LEDDomeRainChamberVisualizer.InferWandMotion(
          upper, sweptAim, 0.1);
      Assert(Math.Abs(inferredMotion.Length() - 0.5) < 0.00001,
        "Rain Chamber wand motion did not preserve bounded angular speed");
      Assert(Vector3.Dot(inferredMotion, sweepTangent) > 0.45,
        "Rain Chamber wand motion pointed against the wand sweep");
      Assert(LEDDomeRainChamberVisualizer.InferWandMotion(
          upper, upper, 0.1) == Vector3.Zero &&
          LEDDomeRainChamberVisualizer.InferWandMotion(
            upper, sweptAim, 0) == Vector3.Zero,
        "stationary or zero-time wand motion produced a gust");
      Assert(Math.Abs(LEDDomeRainChamberVisualizer.InferWandMotion(
          upper, sweptAim, 0.01).Length() - 1) < 0.00001,
        "Rain Chamber wand motion exceeded its speed bound");

      var noGust = new RainChamberState(29);
      var gust = new RainChamberState(29);
      noGust.SeedDroplet(sweptAim, Vector3.Zero);
      gust.SeedDroplet(sweptAim, Vector3.Zero);
      for (int step = 0; step < 6; step++) {
        noGust.Step(
          0.1, 0, 0, 2, 0, 0, 0, new[] { sweptAim },
          interactionMode: 2, wandMotions: new[] { Vector3.Zero });
        gust.Step(
          0.1, 0, 0, 2, 0, 0, 0, new[] { sweptAim },
          interactionMode: 2, wandMotions: new[] { inferredMotion });
      }
      Assert(Vector3.Distance(
          noGust.Droplets[0].Position,
          gust.Droplets[0].Position) > 0.01 &&
          Vector3.Dot(gust.Droplets[0].Velocity, inferredMotion) > 0,
        "Rain Chamber motion-driven wind did not carry a nearby droplet");

      double windRadius = RainChamberState.DryRadius(2);
      Vector3 outsideWind = Vector3.Normalize(
        sweptAim * (float)Math.Cos(windRadius * 1.2) +
        sweepTangent * (float)Math.Sin(windRadius * 1.2));
      var outsideNoGust = new RainChamberState(29);
      var outsideGust = new RainChamberState(29);
      outsideNoGust.SeedDroplet(outsideWind, Vector3.Zero);
      outsideGust.SeedDroplet(outsideWind, Vector3.Zero);
      outsideNoGust.Step(
        0.1, 0, 0, 2, 0, 0, 0, new[] { sweptAim },
        interactionMode: 2, wandMotions: new[] { Vector3.Zero });
      outsideGust.Step(
        0.1, 0, 0, 2, 0, 0, 0, new[] { sweptAim },
        interactionMode: 2, wandMotions: new[] { inferredMotion });
      Assert(Vector3.Distance(
          outsideNoGust.Droplets[0].Position,
          outsideGust.Droplets[0].Position) < 0.000001,
        "Rain Chamber wind field reached beyond its bounded radius");

      double dryRadius = RainChamberState.DryRadius(2);
      Vector3 dryTangent = Vector3.Normalize(Vector3.Cross(
        upper, Vector3.UnitY));
      Vector3 outsideDryRegion = Vector3.Normalize(
        upper * (float)Math.Cos(dryRadius * 1.2) +
        dryTangent * (float)Math.Sin(dryRadius * 1.2));
      var drying = new RainChamberState(30);
      drying.SeedDroplet(upper, Vector3.Zero, 1);
      drying.SeedDroplet(outsideDryRegion, Vector3.Zero, 2);
      drying.Step(
        0.01, 0, 0, 2, 0, 0, 0, new[] { upper },
        interactionMode: 1);
      Assert(drying.Droplets.Count == 1 &&
          Vector3.Dot(drying.Droplets[0].Position, outsideDryRegion) > 0.999,
        "Rain Chamber dry region did not remove only nearby droplets");

      var disabledDrying = new RainChamberState(30);
      disabledDrying.SeedDroplet(upper, Vector3.Zero);
      disabledDrying.Step(
        0.01, 0, 0, 0, 0, 0, 0, new[] { upper },
        interactionMode: 1);
      Assert(disabledDrying.Droplets.Count == 1,
        "zero wand strength still removed a Rain Chamber droplet");

      var impacting = new RainChamberState(31);
      Vector3 nearRim = Vector3.Normalize(
        new Vector3(0.999f, 0, 0.03f));
      impacting.SeedDroplet(nearRim, -Vector3.UnitZ, 3);
      impacting.Step(
        0.1, 0, 1, 0, 0.045, 1, 0, Array.Empty<Vector3>());
      Assert(impacting.Droplets.Count == 0 && impacting.Splashes.Count == 1,
        "rim impact did not replace a droplet with a splash ring");
      AssertClose(0, impacting.Splashes[0].Center.Z,
        "rim impact splash moved away from the dome rim");
      Assert(RainChamberState.SplashRadius(0.4) >
          RainChamberState.SplashRadius(0),
        "Rain Chamber splash rings did not expand");
      Assert(RainChamberState.SplashEnvelope(0.4, 1) <
          RainChamberState.SplashEnvelope(0, 1),
        "Rain Chamber splash rings did not decay");
      for (int step = 0; step < 10; step++) {
        impacting.Step(
          0.1, 0, 0, 0, 0.045, 1, 0, Array.Empty<Vector3>());
      }
      Assert(impacting.Splashes.Count == 0,
        "Rain Chamber retained an expired splash ring");

      double collisionDotSize = 0.06;
      double collisionRadius = RainChamberState.CollisionRadius(
        collisionDotSize, 0.5, 0.5);
      Vector3 collisionCenter = Vector3.Normalize(
        new Vector3(0.35f, 0.1f, 0.93f));
      Vector3 collisionTangent = Vector3.Normalize(Vector3.Cross(
        collisionCenter, Vector3.UnitZ));
      Vector3 closeNeighbor = Vector3.Normalize(
        collisionCenter * (float)Math.Cos(collisionRadius * 0.75) +
        collisionTangent * (float)Math.Sin(collisionRadius * 0.75));
      var colliding = new RainChamberState(37);
      colliding.SeedDroplet(
        collisionCenter, collisionTangent * 0.1f, 2, 0.5);
      colliding.SeedDroplet(
        closeNeighbor, -collisionTangent * 0.1f, 6, 0.5);
      colliding.Step(
        0.01, 0, 0, 0, collisionDotSize, 1, 0,
        Array.Empty<Vector3>());
      Assert(colliding.Droplets.Count == 1 &&
          colliding.Splashes.Count == 1,
        "intersecting droplets did not coalesce into one splash");
      Assert(colliding.Splashes[0].Center.Z > 0.8,
        "droplet collision splash was projected to the rim");
      colliding.Step(
        0.01, 0, 0, 0, collisionDotSize, 1, 0,
        Array.Empty<Vector3>());
      Assert(colliding.Splashes.Count == 1,
        "coalesced droplets retriggered their collision splash");

      Vector3 farNeighbor = Vector3.Normalize(
        collisionCenter * (float)Math.Cos(collisionRadius * 1.25) +
        collisionTangent * (float)Math.Sin(collisionRadius * 1.25));
      var separated = new RainChamberState(41);
      separated.SeedDroplet(collisionCenter, Vector3.Zero, 1, 0.5);
      separated.SeedDroplet(farNeighbor, Vector3.Zero, 4, 0.5);
      separated.Step(
        0.01, 0, 0, 0, collisionDotSize, 1, 0,
        Array.Empty<Vector3>());
      Assert(separated.Droplets.Count == 2 &&
          separated.Splashes.Count == 0,
        "Rain Chamber collision reach caught separated droplets");

      var splashDisabled = new RainChamberState(43);
      splashDisabled.SeedDroplet(collisionCenter, Vector3.Zero, 1, 0.5);
      splashDisabled.SeedDroplet(closeNeighbor, Vector3.Zero, 4, 0.5);
      splashDisabled.Step(
        0.01, 0, 0, 0, collisionDotSize, 0, 0,
        Array.Empty<Vector3>());
      Assert(splashDisabled.Droplets.Count == 2 &&
          splashDisabled.Splashes.Count == 0,
        "disabled splash strength still resolved droplet collisions");

      AssertClose(0,
        LEDDomeRainChamberVisualizer.TrailRetention(0, 0.1),
        "zero Rain Chamber trail retained old light");
      AssertClose(0.5,
        LEDDomeRainChamberVisualizer.TrailRetention(0.7, 0.7),
        "Rain Chamber trail retention is not a brightness half-life");

      var insideTrail = new DomeFrame(OnePixelTopology());
      insideTrail.pixels[0].color = 0xFFFFFF;
      LEDDomeRainChamberVisualizer.ApplyDryRegions(
        insideTrail, new[] { upper }, new[] { upper }, 2);
      Assert(insideTrail.pixels[0].color == 0 &&
          insideTrail.pixels[0].a == 0,
        "Rain Chamber dry region retained old trail light");

      var outsideTrail = new DomeFrame(OnePixelTopology());
      outsideTrail.pixels[0].color = 0xFFFFFF;
      LEDDomeRainChamberVisualizer.ApplyDryRegions(
        outsideTrail, new[] { outsideDryRegion }, new[] { upper }, 2);
      Assert(outsideTrail.pixels[0].color == 0xFFFFFF &&
          outsideTrail.pixels[0].a == 1,
        "Rain Chamber dry region cleared trail light beyond its reach");

      var config = ConfigurationWithLayers(
        Layer("rain-chamber", "rain-inputs"));
      SetPaletteColors(config, color => 0xF8FCFF - color * 0x030100);
      var runtime = new global::Spectrum.Operator(config);
      DomeLayerVisualizer rain = null;
      foreach (Visualizer visualizer in runtime.DomeOutput.GetVisualizers()) {
        if (visualizer is DomeLayerVisualizer layer &&
            layer.LayerKey == "rain-chamber") {
          rain = layer;
          break;
        }
      }
      Assert(rain != null, "Rain Chamber renderer was not created");
      Input[] inputs = rain.GetInputs();
      Assert(inputs.Length == 2 &&
          ReferenceEquals(inputs[0], runtime.AudioInput) &&
          ReferenceEquals(inputs[1], runtime.OrientationInput),
        "Rain Chamber did not declare audio and wand inputs");
      ((Visualizer)rain).Visualize();
      Assert(rain.LayerBuffer.pixels.Any(pixel => pixel.color != 0),
        "Rain Chamber did not render its established rainfall state");
    }

    private static void TopographicDreamUsesEvolvingContours() {
      LayerDefinition definition =
        DomeLayerCatalog.Metadata.Get("topographic-dream");
      Assert(definition != null &&
          definition.DisplayName == "Topographic Dream",
        "Topographic Dream was not registered");

      TopographicDreamLayerOptions defaults =
        BuiltInOptions<TopographicDreamLayerOptions>(
          Layer("topographic-dream", "topographic-defaults"));
      AssertClose(2.2, defaults.TerrainScale,
        "unexpected Topographic Dream terrain scale");
      AssertClose(0.12, defaults.EvolutionSpeed,
        "unexpected Topographic Dream evolution speed");
      AssertClose(0.11, defaults.ContourInterval,
        "unexpected Topographic Dream contour interval");
      AssertClose(0.14, defaults.LineWidth,
        "unexpected Topographic Dream line width");
      AssertClose(0.42, defaults.SeaLevel,
        "unexpected Topographic Dream sea level");
      Assert(!defaults.BindToOrientation && defaults.Palette == 0,
        "unexpected Topographic Dream orientation or palette default");

      DomeLayerSettings configured = Layer(
        "topographic-dream", "topographic-clamped");
      configured.RendererParams = new Dictionary<string, double> {
        ["terrainScale"] = 99,
        ["evolutionSpeed"] = -1,
        ["contourInterval"] = 0,
        ["lineWidth"] = 99,
        ["seaLevel"] = -1,
        ["bindOrientation"] = 1,
        ["palette"] = 99,
      };
      TopographicDreamLayerOptions clamped =
        BuiltInOptions<TopographicDreamLayerOptions>(configured);
      Assert(clamped.TerrainScale == 6 &&
          clamped.EvolutionSpeed == 0 &&
          clamped.ContourInterval == 0.04 &&
          clamped.LineWidth == 0.45 && clamped.SeaLevel == 0 &&
          clamped.BindToOrientation &&
          clamped.Palette == PaletteService.MaxPalettes - 1,
        "Topographic Dream controls did not clamp");

      AssertClose(0.42,
        LEDDomeTopographicDreamVisualizer.EffectiveSeaLevel(0.42, 0),
        "quiet audio changed Topographic Dream's configured sea level");
      AssertClose(0.70,
        LEDDomeTopographicDreamVisualizer.EffectiveSeaLevel(0.42, 1),
        "full audio did not raise Topographic Dream's sea level");
      AssertClose(1,
        LEDDomeTopographicDreamVisualizer.EffectiveSeaLevel(0.95, 4),
        "Topographic Dream sea level did not clamp");

      Vector3 sample = Vector3.Normalize(
        new Vector3(0.45f, -0.21f, 0.87f));
      double baseElevation =
        LEDDomeTopographicDreamVisualizer.ElevationField(
          sample, 2.2, 0);
      double scaledDirectionElevation =
        LEDDomeTopographicDreamVisualizer.ElevationField(
          sample * 5, 2.2, 0);
      AssertClose(baseElevation, scaledDirectionElevation,
        "Topographic Dream depended on vector length rather than direction");
      double evolvedElevation =
        LEDDomeTopographicDreamVisualizer.ElevationField(
          sample, 2.2, 2);
      double rescaledElevation =
        LEDDomeTopographicDreamVisualizer.ElevationField(
          sample, 4.4, 0);
      Assert(Math.Abs(baseElevation - evolvedElevation) > 0.005,
        "Topographic Dream terrain did not evolve");
      Assert(Math.Abs(baseElevation - rescaledElevation) > 0.005,
        "Topographic Dream terrain scale did not change its field");
      for (int index = 0; index < 32; index++) {
        double angle = 2 * Math.PI * index / 32;
        Vector3 direction = Vector3.Normalize(new Vector3(
          (float)Math.Cos(angle), (float)Math.Sin(angle),
          (float)(0.05 + 0.95 * ((index % 7) / 6.0))));
        double elevation =
          LEDDomeTopographicDreamVisualizer.ElevationField(
            direction, 6, index * 0.13);
        Assert(elevation >= 0 && elevation <= 1,
          "Topographic Dream elevation escaped its normalized range");
      }

      AssertClose(1,
        LEDDomeTopographicDreamVisualizer.ContourStrength(
          0.44, 0.11, 0.14),
        "Topographic Dream missed an exact contour interval");
      AssertClose(0,
        LEDDomeTopographicDreamVisualizer.ContourStrength(
          0.4675, 0.11, 0.14),
        "narrow Topographic Dream contour flooded an interline region");
      Assert(
        LEDDomeTopographicDreamVisualizer.ContourStrength(
          0.455, 0.11, 0.30) >
        LEDDomeTopographicDreamVisualizer.ContourStrength(
          0.455, 0.11, 0.08),
        "Topographic Dream line width did not broaden contours");
      AssertClose(1,
        LEDDomeTopographicDreamVisualizer.CoastlineStrength(
          0.42, 0.42, 0.02),
        "Topographic Dream missed its coastline");
      AssertClose(0,
        LEDDomeTopographicDreamVisualizer.CoastlineStrength(
          0.46, 0.42, 0.02),
        "Topographic Dream coastline flooded distant terrain");

      var config = ConfigurationWithLayers(
        Layer("topographic-dream", "topographic-inputs"));
      SetPaletteColors(config, color => 0x183050 + color * 0x181208);
      var runtime = new global::Spectrum.Operator(config);
      DomeLayerVisualizer topographic = null;
      foreach (Visualizer visualizer in runtime.DomeOutput.GetVisualizers()) {
        if (visualizer is DomeLayerVisualizer layer &&
            layer.LayerKey == "topographic-dream") {
          topographic = layer;
          break;
        }
      }
      Assert(topographic != null,
        "Topographic Dream renderer was not created");
      Input[] inputs = topographic.GetInputs();
      Assert(inputs.Length == 2 &&
          ReferenceEquals(inputs[0], runtime.AudioInput) &&
          ReferenceEquals(inputs[1], runtime.OrientationInput),
        "Topographic Dream did not declare audio and orientation inputs");
      ((Visualizer)topographic).Visualize();
      Assert(topographic.LayerBuffer.pixels.Any(pixel => pixel.color != 0),
        "Topographic Dream rendered an empty field");
      Assert(topographic.LayerBuffer.pixels
          .Select(pixel => pixel.color).Distinct().Count() > 8,
        "Topographic Dream did not produce varied terrain shading");
      Assert(topographic.LayerBuffer.pixels.Any(pixel => pixel.a > 0.5) &&
          topographic.LayerBuffer.pixels.Any(pixel => pixel.a < 0.25),
        "Topographic Dream did not separate contour and fill coverage");
    }

    private static void OrbitalGardenUsesSphericalOrbits() {
      LayerDefinition definition = DomeLayerCatalog.Metadata.Get("orbital-garden");
      Assert(definition != null && definition.DisplayName == "Orbital Garden",
        "Orbital Garden was not registered");

      OrbitalGardenLayerOptions defaults =
        BuiltInOptions<OrbitalGardenLayerOptions>(
          Layer("orbital-garden", "orbital-defaults"));
      Assert(defaults.BodyCount == 28,
        "unexpected Orbital Garden body count");
      AssertClose(1.6, defaults.Gravity,
        "unexpected Orbital Garden gravity");
      AssertClose(0.12, defaults.OrbitalDamping,
        "unexpected Orbital Garden damping");
      Assert(defaults.CollisionBehavior == 2,
        "unexpected Orbital Garden collision behavior");
      AssertClose(0.8, defaults.TrailLength,
        "unexpected Orbital Garden trail length");
      AssertClose(0.05, defaults.BodySize,
        "unexpected Orbital Garden body size");
      Assert(defaults.Palette == 0,
        "unexpected Orbital Garden palette");

      DomeLayerSettings configured = Layer(
        "orbital-garden", "orbital-clamped");
      configured.RendererParams = new Dictionary<string, double> {
        ["bodyCount"] = 999,
        ["gravity"] = -1,
        ["orbitalDamping"] = 99,
        ["collisionBehavior"] = 99,
        ["trailLength"] = 99,
        ["bodySize"] = 0,
        ["palette"] = 99,
      };
      OrbitalGardenLayerOptions clamped =
        BuiltInOptions<OrbitalGardenLayerOptions>(configured);
      Assert(clamped.BodyCount == 96 && clamped.Gravity == 0 &&
          clamped.OrbitalDamping == 3 &&
          clamped.CollisionBehavior == 2 &&
          clamped.TrailLength == 4 && clamped.BodySize == 0.015 &&
          clamped.Palette == PaletteService.MaxPalettes - 1,
        "Orbital Garden controls did not clamp");

      var garden = new OrbitalGardenState(20, 17);
      Assert(garden.Bodies.Count == 20,
        "Orbital Garden did not create its requested body count");
      foreach (OrbitalBody body in garden.Bodies) {
        Assert(body.Position.Z >= 0 &&
            Math.Abs(body.Position.Length() - 1) < 0.000001,
          "Orbital Garden initialized off the visible unit hemisphere");
        Assert(Math.Abs(Vector3.Dot(
            body.Position, body.Velocity)) < 0.000001,
          "Orbital Garden initialized non-tangent velocity");
      }
      Vector3 retainedBody = garden.Bodies[0].Position;
      garden.Resize(31);
      Assert(garden.Bodies.Count == 31 &&
          garden.Bodies[0].Position == retainedBody,
        "Orbital Garden did not grow its persistent body array in place");
      garden.Resize(9);
      Assert(garden.Bodies.Count == 9 &&
          garden.Bodies[0].Position == retainedBody,
        "Orbital Garden did not shrink its persistent body array in place");

      var orbit = new OrbitalGardenState(1, 23);
      Vector3 wellPosition = Vector3.UnitZ;
      Vector3 orbitStart = Vector3.Normalize(
        new Vector3(0.36f, 0, 0.93f));
      orbit.SeedBody(0, orbitStart, Vector3.UnitY * 0.28f, 0);
      var well = new[] { new OrbitalGravityWell(wellPosition, 5) };
      for (int step = 0; step < 40; step++) {
        orbit.Step(0.05, 1.6, 0.12, 0, 0.02, well);
      }
      OrbitalBody orbiter = orbit.Bodies[0];
      double finalWellDistance = Math.Acos(Math.Clamp(
        Vector3.Dot(orbiter.Position, wellPosition), -1, 1));
      Assert(Math.Abs(orbiter.Position.Y) > 0.02 &&
          finalWellDistance > 0.03 && finalWellDistance < 0.9,
        "Orbital Garden did not sustain a curved orbit around its well");
      Assert(orbiter.PaletteIndex == 5,
        "Orbital Garden body did not inherit its strongest well color");
      Assert(orbiter.Position.Z >= 0 &&
          Math.Abs(orbiter.Position.Length() - 1) < 0.000001 &&
          Math.Abs(Vector3.Dot(
            orbiter.Position, orbiter.Velocity)) < 0.000001,
        "Orbital Garden orbit escaped the tangent unit hemisphere");

      var falling = new OrbitalGardenState(1, 29);
      Vector3 fallingStart = Vector3.Normalize(
        new Vector3(0.72f, 0, 0.69f));
      falling.SeedBody(0, fallingStart, Vector3.Zero);
      double beforePull = Math.Acos(Math.Clamp(
        Vector3.Dot(fallingStart, wellPosition), -1, 1));
      for (int step = 0; step < 10; step++) {
        falling.Step(0.05, 2, 0, 0, 0.02, well);
      }
      double afterPull = Math.Acos(Math.Clamp(
        Vector3.Dot(falling.Bodies[0].Position, wellPosition), -1, 1));
      Assert(afterPull < beforePull,
        "Orbital Garden gravity did not pull a body toward a wand well");

      Vector3 collisionPoint = Vector3.Normalize(
        new Vector3(0.2f, -0.1f, 0.97f));
      var bounced = new OrbitalGardenState(2, 31);
      bounced.SeedBody(0, collisionPoint, Vector3.Zero, 1);
      bounced.SeedBody(1, collisionPoint, Vector3.Zero, 2);
      bounced.Step(
        0.01, 0, 0, 0, 0.1, Array.Empty<OrbitalGravityWell>());
      Assert(bounced.Blooms.Count == 0 && bounced.Fragments.Count == 0 &&
          Vector3.Distance(
            bounced.Bodies[0].Velocity,
            bounced.Bodies[1].Velocity) > 0.1,
        "Orbital Garden bounce mode did not separate colliding bodies");

      var bloomed = new OrbitalGardenState(2, 37);
      bloomed.SeedBody(0, collisionPoint, Vector3.Zero, 1);
      bloomed.SeedBody(1, collisionPoint, Vector3.Zero, 2);
      bloomed.Step(
        0.01, 0, 0, 1, 0.1, Array.Empty<OrbitalGravityWell>());
      Assert(bloomed.Blooms.Count == 1 && bloomed.Fragments.Count == 0,
        "Orbital Garden bloom mode did not emit only a bloom");

      var fragmented = new OrbitalGardenState(2, 41);
      fragmented.SeedBody(0, collisionPoint, Vector3.Zero, 3);
      fragmented.SeedBody(1, collisionPoint, Vector3.Zero, 4);
      fragmented.Step(
        0.01, 0, 0, 2, 0.1, Array.Empty<OrbitalGravityWell>());
      Assert(fragmented.Blooms.Count == 1 &&
          fragmented.Fragments.Count == 4,
        "Orbital Garden fragment mode did not launch collision debris");
      Assert(OrbitalGardenState.BloomRadius(0.4) >
          OrbitalGardenState.BloomRadius(0) &&
          OrbitalGardenState.BloomEnvelope(0.4) <
          OrbitalGardenState.BloomEnvelope(0) &&
          OrbitalGardenState.FragmentEnvelope(0.4) <
          OrbitalGardenState.FragmentEnvelope(0),
        "Orbital Garden collision effects did not expand and decay");
      fragmented.SeedBody(0, Vector3.UnitZ, Vector3.Zero);
      fragmented.SeedBody(1, Vector3.UnitX, Vector3.Zero);
      for (int step = 0; step < 10; step++) {
        fragmented.Step(
          0.1, 0, 0, 0, 0.02,
          Array.Empty<OrbitalGravityWell>());
      }
      Assert(fragmented.Blooms.Count == 0 &&
          fragmented.Fragments.Count == 0,
        "Orbital Garden retained expired collision effects");

      AssertClose(0,
        LEDDomeOrbitalGardenVisualizer.TrailRetention(0, 0.1),
        "zero Orbital Garden trail retained old light");
      AssertClose(0.5,
        LEDDomeOrbitalGardenVisualizer.TrailRetention(0.8, 0.8),
        "Orbital Garden trail length is not a brightness half-life");

      var config = ConfigurationWithLayers(
        Layer("orbital-garden", "orbital-inputs"));
      SetPaletteColors(config, color => 0xFFF0D0 - color * 0x0A0502);
      var runtime = new global::Spectrum.Operator(config);
      DomeLayerVisualizer orbital = null;
      foreach (Visualizer visualizer in runtime.DomeOutput.GetVisualizers()) {
        if (visualizer is DomeLayerVisualizer layer &&
            layer.LayerKey == "orbital-garden") {
          orbital = layer;
          break;
        }
      }
      Assert(orbital != null,
        "Orbital Garden renderer was not created");
      Input[] inputs = orbital.GetInputs();
      Assert(inputs.Length == 1 &&
          ReferenceEquals(inputs[0], runtime.OrientationInput),
        "Orbital Garden did not declare its wand input");
      ((Visualizer)orbital).Visualize();
      Assert(orbital.LayerBuffer.pixels.Any(pixel => pixel.color != 0),
        "Orbital Garden did not render its fallback solar system");
    }

    private static void LavaLampSkyUsesViscousThermalBlobs() {
      LayerDefinition definition = DomeLayerCatalog.Metadata.Get("lava-lamp-sky");
      Assert(definition != null && definition.DisplayName == "Lava Lamp Sky",
        "Lava Lamp Sky was not registered");

      LavaLampSkyLayerOptions defaults =
        BuiltInOptions<LavaLampSkyLayerOptions>(
          Layer("lava-lamp-sky", "lava-defaults"));
      Assert(defaults.BlobCount == 9,
        "unexpected Lava Lamp Sky blob count");
      AssertClose(1.8, defaults.Viscosity,
        "unexpected Lava Lamp Sky viscosity");
      AssertClose(0.8, defaults.Buoyancy,
        "unexpected Lava Lamp Sky buoyancy");
      AssertClose(1.35, defaults.SurfaceTension,
        "unexpected Lava Lamp Sky surface tension");
      AssertClose(0.35, defaults.Heat,
        "unexpected Lava Lamp Sky heat");
      Assert(defaults.BindGravity && defaults.Palette == 0,
        "unexpected Lava Lamp Sky gravity binding or palette");

      DomeLayerSettings configured = Layer(
        "lava-lamp-sky", "lava-clamped");
      configured.RendererParams = new Dictionary<string, double> {
        ["blobCount"] = 999,
        ["viscosity"] = -1,
        ["buoyancy"] = 99,
        ["surfaceTension"] = 99,
        ["heat"] = 99,
        ["bindGravity"] = 0,
        ["palette"] = 99,
      };
      LavaLampSkyLayerOptions clamped =
        BuiltInOptions<LavaLampSkyLayerOptions>(configured);
      Assert(clamped.BlobCount == 24 && clamped.Viscosity == 0.2 &&
          clamped.Buoyancy == 3 && clamped.SurfaceTension == 3 &&
          clamped.Heat == 1 && !clamped.BindGravity &&
          clamped.Palette == PaletteService.MaxPalettes - 1,
        "Lava Lamp Sky controls did not clamp");

      var state = new LavaLampSkyState(9, 17);
      Assert(state.Blobs.Count == 9,
        "Lava Lamp Sky did not create its requested blob count");
      foreach (LavaLampBlob blob in state.Blobs) {
        Assert(blob.Position.Z >= 0 &&
            Math.Abs(blob.Position.Length() - 1) < 0.000001,
          "Lava Lamp Sky initialized off the visible unit hemisphere");
        Assert(Math.Abs(Vector3.Dot(
            blob.Position, blob.Velocity)) < 0.000001,
          "Lava Lamp Sky initialized non-tangent velocity");
      }
      Vector3 retained = state.Blobs[0].Position;
      state.Resize(14);
      Assert(state.Blobs.Count == 14 && state.Blobs[0].Position == retained,
        "Lava Lamp Sky did not grow its persistent blob array in place");
      state.Resize(5);
      Assert(state.Blobs.Count == 5 && state.Blobs[0].Position == retained,
        "Lava Lamp Sky did not shrink its persistent blob array in place");

      AssertClose(1, LavaLampSkyState.EffectiveHeat(1, 0),
        "configured Lava Lamp Sky heat changed at quiet audio");
      Assert(LavaLampSkyState.EffectiveHeat(0.2, 1) >
          LavaLampSkyState.EffectiveHeat(0.2, 0) &&
          LavaLampSkyState.EffectiveBuoyancy(1, 1) >
          LavaLampSkyState.EffectiveBuoyancy(1, 0) &&
          LavaLampSkyState.SeparationResponse(0.2, 1) >
          LavaLampSkyState.SeparationResponse(0.2, 0),
        "audio did not raise Lava Lamp Sky heat, buoyancy, and separation");

      Assert(LEDDomeLavaLampSkyVisualizer.GravityAxis(
          Quaternion.Identity, false) == Vector3.UnitZ &&
          Vector3.Distance(
            LEDDomeLavaLampSkyVisualizer.GravityAxis(
              Quaternion.Identity, true),
            OrientationCenter.Spot) < 0.000001,
        "Lava Lamp Sky did not tilt its gravity axis with orientation");

      var rising = new LavaLampSkyState(1, 23);
      Vector3 riseStart = Vector3.Normalize(
        new Vector3(0.92f, 0, 0.39f));
      rising.SeedBlob(0, riseStart, Vector3.Zero, 1.12);
      for (int step = 0; step < 30; step++) {
        rising.Step(0.05, 0.2, 2.5, 0, 0.8, 0, Vector3.UnitZ);
      }
      Assert(rising.Blobs[0].Position.Z > riseStart.Z + 0.02,
        "a warm Lava Lamp Sky body did not rise through spherical buoyancy");

      Vector3 movingPosition = Vector3.Normalize(
        new Vector3(0.4f, 0, 0.9165f));
      var thin = new LavaLampSkyState(1, 29);
      var thick = new LavaLampSkyState(1, 29);
      thin.SeedBlob(0, movingPosition, Vector3.UnitY * 0.35f, 0.58);
      thick.SeedBlob(0, movingPosition, Vector3.UnitY * 0.35f, 0.58);
      thin.Step(0.1, 0.2, 0, 0, 0, 0, Vector3.UnitZ);
      thick.Step(0.1, 4, 0, 0, 0, 0, Vector3.UnitZ);
      Assert(thick.Blobs[0].Velocity.Length() <
          thin.Blobs[0].Velocity.Length(),
        "Lava Lamp Sky viscosity did not damp body motion");

      var merging = new LavaLampSkyState(2, 31);
      Vector3 mergeA = Vector3.Normalize(new Vector3(-0.28f, 0, 0.96f));
      Vector3 mergeB = Vector3.Normalize(new Vector3(0.28f, 0, 0.96f));
      merging.SeedBlob(0, mergeA, Vector3.Zero, 0.58, 0.4);
      merging.SeedBlob(1, mergeB, Vector3.Zero, 0.58, 0.4);
      double distanceBefore = Math.Acos(Math.Clamp(
        Vector3.Dot(mergeA, mergeB), -1, 1));
      for (int step = 0; step < 25; step++) {
        merging.Step(0.05, 0.2, 0, 2.5, 0, 0, Vector3.UnitZ);
      }
      double distanceAfter = Math.Acos(Math.Clamp(Vector3.Dot(
        merging.Blobs[0].Position, merging.Blobs[1].Position), -1, 1));
      Assert(distanceAfter < distanceBefore &&
          (merging.Blobs[0].Stretch > 0.05 ||
           merging.Blobs[1].Stretch > 0.05),
        "Lava Lamp Sky surface tension did not merge and stretch neighbors");

      var quiet = new LavaLampSkyState(1, 37);
      var loud = new LavaLampSkyState(1, 37);
      quiet.SeedBlob(0, riseStart, Vector3.Zero, 0.65);
      loud.SeedBlob(0, riseStart, Vector3.Zero, 0.65);
      for (int step = 0; step < 30; step++) {
        quiet.Step(0.05, 1, 0, 0.1, 0.2, 0, Vector3.UnitZ);
        loud.Step(0.05, 1, 0, 0.1, 0.2, 1, Vector3.UnitZ);
      }
      Assert(loud.Blobs[0].Split > quiet.Blobs[0].Split + 0.1,
        "audio heat did not divide a Lava Lamp Sky body");

      LavaLampBlob divided = loud.Blobs[0] with {
        Position = Vector3.UnitZ,
        ShapeAxis = Vector3.UnitX,
        Radius = 0.4,
        Stretch = 0,
        Split = 1,
      };
      double pinch = LEDDomeLavaLampSkyVisualizer.BlobStrength(
        Vector3.UnitZ, divided);
      Vector3 lobePoint = Vector3.Normalize(
        Vector3.UnitZ * (float)Math.Cos(0.31) +
        Vector3.UnitX * (float)Math.Sin(0.31));
      double lobe = LEDDomeLavaLampSkyVisualizer.BlobStrength(
        lobePoint, divided);
      Assert(lobe > 0.8 && lobe > pinch + 0.5,
        "Lava Lamp Sky division did not form two pinched soft lobes");

      var config = ConfigurationWithLayers(
        Layer("lava-lamp-sky", "lava-inputs"));
      SetPaletteColors(config, color => 0xFF8A22 + color * 0x000804);
      var runtime = new global::Spectrum.Operator(config);
      DomeLayerVisualizer lava = null;
      foreach (Visualizer visualizer in runtime.DomeOutput.GetVisualizers()) {
        if (visualizer is DomeLayerVisualizer layer &&
            layer.LayerKey == "lava-lamp-sky") {
          lava = layer;
          break;
        }
      }
      Assert(lava != null, "Lava Lamp Sky renderer was not created");
      Input[] inputs = lava.GetInputs();
      Assert(inputs.Length == 2 &&
          ReferenceEquals(inputs[0], runtime.AudioInput) &&
          ReferenceEquals(inputs[1], runtime.OrientationInput),
        "Lava Lamp Sky did not declare audio and orientation inputs");
      ((Visualizer)lava).Visualize();
      Assert(lava.LayerBuffer.pixels.Any(pixel => pixel.color != 0),
        "Lava Lamp Sky rendered an empty foundation");
      Assert(lava.LayerBuffer.pixels.Any(pixel => pixel.a > 0.8) &&
          lava.LayerBuffer.pixels.Any(pixel => pixel.a < 0.2),
        "Lava Lamp Sky did not render soft separated silhouettes");
    }

    private static void VortexUsesGlobalFade() {
      var config = new global::Spectrum.SpectrumConfiguration {
        domeGlobalFadeSpeed = 3,
        domeGlobalHueSpeed = 0,
      };
      config.ReplaceDomeLayerStack(new[] {
        Layer("vortex", "vortex-trail"),
      });
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
      Assert(renderer.GetInputs().Length == 1 &&
          ReferenceEquals(renderer.GetInputs()[0], runtime.AudioInput),
        "vortex did not declare its audio input");
      AssertClose(0, LEDDomeVortexVisualizer.AudioResponseLevel(-1),
        "vortex audio response did not clamp negative levels");
      AssertClose(.5, LEDDomeVortexVisualizer.AudioResponseLevel(.25),
        "vortex audio response did not expand quiet levels");
      AssertClose(1, LEDDomeVortexVisualizer.AudioResponseLevel(2),
        "vortex audio response did not clamp hot levels");
      AssertClose(0, LEDDomeVortexVisualizer.BeatPulseAdvance(.9, .1, false),
        "disabled Vortex beat speed advanced the field");
      AssertClose(0, LEDDomeVortexVisualizer.BeatPulseAdvance(-1, .1, true),
        "Vortex beat speed fired before establishing a baseline");
      AssertClose(0, LEDDomeVortexVisualizer.BeatPulseAdvance(.1, .9, true),
        "Vortex beat speed fired without a beat wrap");
      AssertClose(10 / FrameClock.NominalFps,
        LEDDomeVortexVisualizer.BeatPulseAdvance(.9, .1, true),
        "Vortex beat speed did not apply its forward pulse");
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
      (bool saved, string saveError) = scenes.Save("duplicates");
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

      (bool applied, string applyError) = scenes.Apply("duplicates");
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

      (bool ambiguousFire, string fireError) =
        controller.FireAsync("wave").GetAwaiter().GetResult();
      Assert(!ambiguousFire && fireError.Contains("unknown layer instance"),
        "renderer-key fire was accepted");
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
      Assert(!ambiguousClear && clearError.Contains("unknown layer instance"),
        "renderer-key clear was accepted");
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
          operation.Id + " changed the destination");
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
        AssertColors(operation.Id + " at 0.5", half,
          expectedHalf[operation]);
        AssertColors(operation.Id + " at 1", full,
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
          operation.Id + " alpha " + i);
        AssertClose(expectedHue[i], frame.pixels[i].hue,
          operation.Id + " hue " + i);
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

      HalftoneOptions halftone = (HalftoneOptions)
        CompileOptions(DomeBlend.Halftone, new Dictionary<string, double> {
          ["cellType"] = 99,
          ["scale"] = double.NaN,
          ["threshold"] = double.PositiveInfinity,
          ["dotMin"] = -1,
          ["dotMax"] = double.NaN,
          ["rotation"] = double.PositiveInfinity,
          ["palette"] = 99,
        });
      Assert(halftone.CellType == 2 &&
          halftone.Palette == PaletteService.MaxPalettes - 1,
        "halftone enum clamp failed");
      AssertClose(.14, halftone.Scale, "halftone scale default");
      AssertClose(.95, halftone.Threshold, "halftone threshold clamp");
      AssertClose(0, halftone.DotMinimum, "halftone minimum clamp");
      AssertClose(.94, halftone.DotMaximum, "halftone maximum default");
      AssertClose(Math.PI, halftone.Rotation, "halftone rotation clamp");
    }

    private static void KaleidoscopeFoldsCompositeCoordinates() {
      Assert(ReferenceEquals(
          DomeBlend.FromId("Kaleidoscope"), DomeBlend.Kaleidoscope),
        "Kaleidoscope was not registered");
      Assert(DomeBlend.Kaleidoscope.Params.Count == 6 &&
          DomeBlend.Kaleidoscope.Params.All(p => p.CompositorConsumed),
        "Kaleidoscope controls are not compositor-owned");

      KaleidoscopeOptions defaults = (KaleidoscopeOptions)
        CompileOptions(DomeBlend.Kaleidoscope, null);
      Assert(defaults.SectorCount == 8 && defaults.MirrorSectors &&
          defaults.Spin == .05 && defaults.FocalAngle == 0 &&
          defaults.FocalDistance == 0 && !defaults.FollowOrientation,
        "unexpected Kaleidoscope defaults");

      KaleidoscopeOptions clamped = (KaleidoscopeOptions)
        CompileOptions(DomeBlend.Kaleidoscope,
          new Dictionary<string, double> {
            ["sectors"] = 99,
            ["mirror"] = -1,
            ["spin"] = double.PositiveInfinity,
            ["focalAngle"] = double.NegativeInfinity,
            ["focalDistance"] = 99,
            ["follow"] = -1,
          });
      Assert(clamped.SectorCount == 24 && !clamped.MirrorSectors &&
          clamped.Spin == 2 && clamped.FocalAngle == -Math.PI &&
          clamped.FocalDistance == .8 && clamped.FollowOrientation,
        "Kaleidoscope controls did not clamp or coerce");

      DomeTopology topology = RingTopology(16, .6);
      var lookupFrame = new DomeFrame(topology);
      Assert(lookupFrame.NearestTopDownPixel(.8, .5) == 0,
        "top-down lookup missed an exact projected pixel");
      Assert(lookupFrame.NearestTopDownPixel(.79, .49) == 0,
        "top-down lookup did not return the nearest projected pixel");

      var repeatOptions = new KaleidoscopeOptions(
        4, false, 0, 0, 0, false);
      DomeFrame repeat = ExecuteKaleidoscope(
        topology, repeatOptions, 1, 0, null);
      var repeatExpected = new int[16];
      for (int i = 0; i < repeatExpected.Length; i++) {
        repeatExpected[i] = ((i % 4) + 1) << 16;
      }
      AssertColors("Kaleidoscope repeat", repeat, repeatExpected);

      var mirrorOptions = repeatOptions with { MirrorSectors = true };
      DomeFrame mirror = ExecuteKaleidoscope(
        topology, mirrorOptions, 1, 0, null);
      var mirrorExpected = new int[16];
      for (int i = 0; i < mirrorExpected.Length; i++) {
        int sector = i / 4;
        int local = i % 4;
        int sample = (sector & 1) == 0 ? local : 4 - local;
        mirrorExpected[i] = (sample + 1) << 16;
      }
      AssertColors("Kaleidoscope mirror", mirror, mirrorExpected);
      AssertClose(.25, mirror.pixels[7].a,
        "Kaleidoscope changed destination coverage");
      AssertClose(7 / 16d, mirror.pixels[7].hue,
        "Kaleidoscope changed destination hue");

      DomeFrame half = ExecuteKaleidoscope(
        topology, repeatOptions, .5, 0, null);
      for (int i = 0; i < half.pixels.Length; i++) {
        double original = (i + 1) << 16;
        double transformed = repeatExpected[i];
        AssertClose(
          (((int)original >> 16) + ((int)transformed >> 16)) / 2d,
          half.pixels[i].r,
          "Kaleidoscope opacity did not interpolate pixel " + i);
      }

      DomeFrame spun = ExecuteKaleidoscope(
        topology, repeatOptions with { Spin = 1d / 16 }, 1, 1, null);
      Assert(ColorSignature(spun) != ColorSignature(repeat),
        "Kaleidoscope spin did not rotate the sectors");

      var fixedFocal = repeatOptions with {
        FocalAngle = Math.PI / 2, FocalDistance = .35,
      };
      DomeFrame fixedFocalFrame = ExecuteKaleidoscope(
        topology, fixedFocal, 1, 0, null);
      DomeFrame followedFocalFrame = ExecuteKaleidoscope(
        topology,
        fixedFocal with { FocalAngle = 0, FollowOrientation = true },
        1, 0, new FixedOrientation(Math.PI / 2));
      Assert(ColorSignature(fixedFocalFrame) ==
          ColorSignature(followedFocalFrame),
        "Kaleidoscope orientation did not replace the focal angle");
      Assert(ColorSignature(fixedFocalFrame) != ColorSignature(repeat),
        "Kaleidoscope focal point did not affect coordinate sampling");

      var liveOrientation = new FixedOrientation(Math.PI / 2);
      var bottom = new DomeFrame(topology);
      var mask = new DomeFrame(topology);
      for (int i = 0; i < bottom.pixels.Length; i++) {
        bottom.pixels[i].color = (i + 1) << 16;
        mask.pixels[i].color = 0xFFFFFF;
      }
      var livePlan = new RenderPlan(ImmutableArray.Create(
        Compiled(
          new FakeRenderer("kaleidoscope-bottom", bottom),
          DomeBlend.Add, 1,
          ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(
          new FakeRenderer("kaleidoscope-mask", mask),
          DomeBlend.Kaleidoscope, 1,
          Parameters(
            ("sectors", 4), ("mirror", 0), ("spin", 0),
            ("focalAngle", 0), ("focalDistance", .35),
            ("follow", 1)))));
      var liveCompositor = new DomeCompositor(
        () => new DomeFrame(topology), liveOrientation,
        elapsedSeconds: () => 0);
      liveCompositor.Publish(livePlan);
      liveCompositor.Compose();
      Assert(liveOrientation.UpdateCount == 1,
        "Kaleidoscope did not refresh standalone orientation state");
    }

    private static DomeFrame ExecuteKaleidoscope(
      DomeTopology topology, KaleidoscopeOptions options,
      double opacity, double seconds, OrientationAngleProvider orientation
    ) {
      var dest = new DomeFrame(topology);
      var source = new DomeFrame(topology);
      var snapshot = new DomeFrame(topology);
      for (int i = 0; i < dest.pixels.Length; i++) {
        dest.pixels[i].color = (i + 1) << 16;
        dest.pixels[i].SetAlpha(.25);
        dest.pixels[i].hue = i / 16d;
        source.pixels[i].color = 0xFFFFFF;
      }
      snapshot.CopyFrom(dest);
      DomeBlend.Kaleidoscope.Execute(new DomeBlendContext(
        dest, source, snapshot, options, opacity, seconds, orientation));
      return dest;
    }

    private static void EchoRetainsDelayedTransformedComposites() {
      Assert(ReferenceEquals(DomeBlend.FromId("Echo"), DomeBlend.Echo),
        "Echo was not registered");
      Assert(DomeBlend.Echo.Params.Count == 9 &&
          DomeBlend.Echo.Params.All(p => p.CompositorConsumed),
        "Echo controls are not compositor-owned");
      Assert((DomeBlend.Echo.Requirements &
          CompositeRequirements.ReadsHistory) != 0,
        "Echo did not declare retained history");

      EchoOptions defaults = (EchoOptions)CompileOptions(DomeBlend.Echo, null);
      Assert(defaults.CopyCount == 4 && defaults.Delay == .2 &&
          defaults.Rotation == .12 && defaults.Scale == .94 &&
          defaults.Drift == .025 && defaults.DriftDirection == 0 &&
          defaults.Decay == .65 && defaults.HueShift == .04 &&
          defaults.Saturation == 1,
        "unexpected Echo defaults");
      EchoOptions clamped = (EchoOptions)CompileOptions(
        DomeBlend.Echo, new Dictionary<string, double> {
          ["copies"] = double.NaN,
          ["delay"] = double.NegativeInfinity,
          ["rotation"] = double.PositiveInfinity,
          ["scale"] = 99,
          ["drift"] = -1,
          ["direction"] = double.NegativeInfinity,
          ["decay"] = 99,
          ["hueShift"] = double.PositiveInfinity,
          ["saturation"] = -1,
        });
      Assert(clamped.CopyCount == 4 && clamped.Delay == .05 &&
          clamped.Rotation == Math.PI && clamped.Scale == 1.3 &&
          clamped.Drift == 0 && clamped.DriftDirection == -Math.PI &&
          clamped.Decay == 1 && clamped.HueShift == .5 &&
          clamped.Saturation == 0,
        "Echo controls did not clamp or coerce");

      ImmutableDictionary<string, ParameterValue> neutral = Parameters(
        ("copies", 1), ("delay", .1), ("rotation", 0),
        ("scale", 1), ("drift", 0), ("direction", 0),
        ("decay", 1), ("hueShift", 0));
      Assert(((EchoOptions)DomeBlend.Echo.CompileOptions(neutral)).Saturation == 1,
        "Echo changed legacy parameter bags that predate saturation control");
      DomeTopology one = OnePixelTopology();
      var bottom = new DomeFrame(one);
      var mask = new DomeFrame(one);
      bottom.pixels[0].color = 0xC00000;
      bottom.pixels[0].hue = .37;
      mask.pixels[0].color = 0xFFFFFF;
      var firstPlan = new RenderPlan(ImmutableArray.Create(
        Compiled(new FakeRenderer("echo-bottom", bottom), DomeBlend.Over, 1,
          ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(new FakeRenderer("echo-mask", mask), DomeBlend.Echo, 1,
          neutral)));
      var retained = new DomeCompositor(
        () => new DomeFrame(one), elapsedSeconds: () => .1);
      retained.Publish(firstPlan);
      retained.Compose();
      bottom.pixels[0].color = 0;
      bottom.pixels[0].hue = .81;
      // Recompile/publish the same stable layer IDs: history must belong to the
      // compositor layer instance, not the transient compiled-plan objects.
      var replacementPlan = new RenderPlan(ImmutableArray.Create(
        Compiled(new FakeRenderer("echo-bottom", bottom), DomeBlend.Over, 1,
          ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(new FakeRenderer("echo-mask", mask), DomeBlend.Echo, 1,
          neutral)));
      retained.Publish(replacementPlan);
      DomeFrame recalled = retained.Compose();
      Assert(recalled.pixels[0].r == 192,
        "Echo lost its delayed frame across plan replacement");
      AssertClose(1, recalled.pixels[0].a,
        "Echo changed destination coverage");
      AssertClose(.81, recalled.pixels[0].hue,
        "Echo changed destination published hue");

      // Removing the operation must release its delay line; reusing the same
      // ID later starts clean rather than resurrecting an old composition.
      retained.Publish(new RenderPlan(ImmutableArray.Create(
        Compiled(new FakeRenderer("echo-bottom", bottom), DomeBlend.Over, 1,
          ImmutableDictionary<string, ParameterValue>.Empty))));
      retained.Compose();
      Assert(retained.HistoryStateCount == 0,
        "Echo retained state after its layer was removed");
      retained.Publish(replacementPlan);
      Assert(retained.Compose().pixels[0].color == 0,
        "Echo resurrected history after layer removal");

      // Each duplicate Echo layer sees and retains the composite at its own
      // stack position. A singleton-owned history would cross-contaminate them.
      DomeTopology pair = TwoPixelTopology();
      var pairBottom = new DomeFrame(pair);
      var firstMask = new DomeFrame(pair);
      var middle = new DomeFrame(pair);
      var secondMask = new DomeFrame(pair);
      pairBottom.pixels[0].color = 0xFF0000;
      pairBottom.pixels[1].color = 0x0000FF;
      firstMask.pixels[0].color = 0xFFFFFF;
      middle.pixels[0].color = 0x00FF00;
      middle.pixels[1].color = 0x00FF00;
      secondMask.pixels[1].color = 0xFFFFFF;
      var isolatedPlan = new RenderPlan(ImmutableArray.Create(
        Compiled(new FakeRenderer("echo-pair-bottom", pairBottom),
          DomeBlend.Add, 1, ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(new FakeRenderer("echo-first-mask", firstMask),
          DomeBlend.Echo, 1, neutral),
        Compiled(new FakeRenderer("echo-middle", middle),
          DomeBlend.Add, 1, ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(new FakeRenderer("echo-second-mask", secondMask),
          DomeBlend.Echo, 1, neutral)));
      var isolated = new DomeCompositor(
        () => new DomeFrame(pair), elapsedSeconds: () => .1);
      isolated.Publish(isolatedPlan);
      isolated.Compose();
      pairBottom.ResetComposite();
      middle.ResetComposite();
      DomeFrame isolatedResult = isolated.Compose();
      Assert(isolatedResult.pixels[0].color == 0xFF0000 &&
          isolatedResult.pixels[1].color == 0x00FFFF,
        "duplicate Echo layers shared or captured the wrong history");
      Assert(isolated.HistoryStateCount == 2,
        "duplicate Echo layers did not receive isolated history state");

      // Rotation, scaling, and drift are cumulative per copy and resolve
      // through the shared arbitrary top-down lookup.
      AssertEchoMovesPoint(
        RingTopology(4, .5), 0, 1,
        Parameters(
          ("copies", 1), ("delay", .1),
          ("rotation", Math.PI / 2), ("scale", 1),
          ("drift", 0), ("direction", 0),
          ("decay", 1), ("hueShift", 0)),
        "rotation");
      AssertEchoMovesPoint(
        ProjectedTopology((.5, 0), (.4, 0)), 0, 1,
        Parameters(
          ("copies", 1), ("delay", .1),
          ("rotation", 0), ("scale", .8),
          ("drift", 0), ("direction", 0),
          ("decay", 1), ("hueShift", 0)),
        "scale");
      AssertEchoMovesPoint(
        ProjectedTopology((0, 0), (.2, 0)), 0, 1,
        Parameters(
          ("copies", 1), ("delay", .1),
          ("rotation", 0), ("scale", 1),
          ("drift", .2), ("direction", 0),
          ("decay", 1), ("hueShift", 0)),
        "drift");

      var colored = new DomeFrame(one);
      var coloredMask = new DomeFrame(one);
      coloredMask.pixels[0].color = 0xFFFFFF;
      var coloredPlan = new RenderPlan(ImmutableArray.Create(
        Compiled(new FakeRenderer("echo-colored-bottom", colored),
          DomeBlend.Add, 1, ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(new FakeRenderer("echo-colored-mask", coloredMask),
          DomeBlend.Echo, 1,
          Parameters(
            ("copies", 2), ("delay", .1), ("rotation", 0),
            ("scale", 1), ("drift", 0), ("direction", 0),
            ("decay", .5), ("hueShift", 0)))));
      var coloredEcho = new DomeCompositor(
        () => new DomeFrame(one), elapsedSeconds: () => .1);
      coloredEcho.Publish(coloredPlan);
      colored.pixels[0].color = 0x640000;
      coloredEcho.Compose();
      colored.pixels[0].color = 0x006400;
      coloredEcho.Compose();
      colored.pixels[0].color = 0;
      DomeFrame decayed = coloredEcho.Compose();
      AssertClose(50, decayed.pixels[0].r,
        "Echo did not decay the older copy");
      AssertClose(100, decayed.pixels[0].g,
        "Echo did not retain the newest delayed copy");

      var hueOptions = Parameters(
        ("copies", 1), ("delay", .1), ("rotation", 0),
        ("scale", 1), ("drift", 0), ("direction", 0),
        ("decay", 1), ("hueShift", 1d / 3));
      DomeFrame hueShifted = TwoFrameEcho(
        one, 0xFF0000, 0, hueOptions, 1, 1);
      Assert(hueShifted.pixels[0].color == 0x00FF00,
        "Echo did not apply cumulative hue shift");
      var saturationOptions = Parameters(
        ("copies", 1), ("delay", .1), ("rotation", 0),
        ("scale", 1), ("drift", 0), ("direction", 0),
        ("decay", 1), ("hueShift", 0), ("saturation", .5));
      DomeFrame desaturated = TwoFrameEcho(
        one, 0xFF0000, 0, saturationOptions, 1, 1);
      AssertClose(255, desaturated.pixels[0].r,
        "Echo saturation scaling changed HSV value");
      AssertClose(127.5, desaturated.pixels[0].g,
        "Echo did not scale the delayed copy's saturation");
      AssertClose(127.5, desaturated.pixels[0].b,
        "Echo saturation scaling did not preserve hue");

      var compoundBottom = new DomeFrame(one);
      var compoundMask = new DomeFrame(one);
      compoundMask.pixels[0].color = 0xFFFFFF;
      var compoundPlan = new RenderPlan(ImmutableArray.Create(
        Compiled(new FakeRenderer("echo-compound-bottom", compoundBottom),
          DomeBlend.Over, 1, ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(new FakeRenderer("echo-compound-mask", compoundMask),
          DomeBlend.Echo, 1,
          Parameters(
            ("copies", 2), ("delay", .1), ("rotation", 0),
            ("scale", 1), ("drift", 0), ("direction", 0),
            ("decay", 1), ("hueShift", 0), ("saturation", .5)))));
      var compoundEcho = new DomeCompositor(
        () => new DomeFrame(one), elapsedSeconds: () => .1);
      compoundEcho.Publish(compoundPlan);
      compoundBottom.pixels[0].color = 0xFF0000;
      compoundEcho.Compose();
      compoundBottom.pixels[0].color = 0;
      compoundEcho.Compose();
      compoundBottom.pixels[0].color = 0;
      compoundBottom.pixels[0].hue = .73;
      DomeFrame compounded = compoundEcho.Compose();
      AssertClose(191.25, compounded.pixels[0].g,
        "Echo did not compound saturation loss across older copies");
      AssertClose(191.25, compounded.pixels[0].b,
        "Echo compounded saturation unevenly");
      AssertClose(1, compounded.pixels[0].a,
        "Echo saturation scaling changed destination coverage");
      AssertClose(.73, compounded.pixels[0].hue,
        "Echo saturation scaling changed destination published hue");
      DomeFrame masked = TwoFrameEcho(
        one, 0xFF0000, 0, saturationOptions, .5, .5);
      AssertClose(63.75, masked.pixels[0].r,
        "Echo did not combine source alpha with layer opacity");
      AssertClose(31.875, masked.pixels[0].g,
        "Echo saturation scaling bypassed the combined mask");

      // The RGB delay line stays bounded to the configured temporal horizon.
      for (int i = 0; i < 100; i++) {
        coloredEcho.Compose();
      }
      Assert(coloredEcho.RetainedHistoryFrameCount <= 4,
        "Echo history grew beyond its configured delay horizon");
    }

    private static void AssertEchoMovesPoint(
      DomeTopology topology, int sourceIndex, int targetIndex,
      ImmutableDictionary<string, ParameterValue> options, string transform
    ) {
      var bottom = new DomeFrame(topology);
      var mask = new DomeFrame(topology);
      bottom.pixels[sourceIndex].color = 0xFF0000;
      for (int i = 0; i < mask.pixels.Length; i++) {
        mask.pixels[i].color = 0xFFFFFF;
      }
      var plan = new RenderPlan(ImmutableArray.Create(
        Compiled(new FakeRenderer("echo-move-bottom-" + transform, bottom),
          DomeBlend.Add, 1, ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(new FakeRenderer("echo-move-mask-" + transform, mask),
          DomeBlend.Echo, 1, options)));
      var compositor = new DomeCompositor(
        () => new DomeFrame(topology), elapsedSeconds: () => .1);
      compositor.Publish(plan);
      compositor.Compose();
      bottom.ResetComposite();
      DomeFrame moved = compositor.Compose();
      Assert(moved.pixels[targetIndex].r == 255,
        "Echo " + transform + " transformed the delayed copy incorrectly");
    }

    private static DomeFrame TwoFrameEcho(
      DomeTopology topology, int firstColor, int secondColor,
      ImmutableDictionary<string, ParameterValue> options,
      double opacity, double maskAlpha
    ) {
      var bottom = new DomeFrame(topology);
      var mask = new DomeFrame(topology);
      bottom.pixels[0].color = firstColor;
      mask.pixels[0].color = 0xFFFFFF;
      mask.pixels[0].SetAlpha(maskAlpha);
      var plan = new RenderPlan(ImmutableArray.Create(
        Compiled(new FakeRenderer("echo-two-bottom", bottom),
          DomeBlend.Add, 1, ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(new FakeRenderer("echo-two-mask", mask),
          DomeBlend.Echo, opacity, options)));
      var compositor = new DomeCompositor(
        () => new DomeFrame(topology), elapsedSeconds: () => .1);
      compositor.Publish(plan);
      compositor.Compose();
      bottom.pixels[0].color = secondColor;
      return compositor.Compose();
    }

    private static ICompositeOptions CompileOptions(
      DomeBlend operation, Dictionary<string, double> parameters
    ) {
      DomeLayerSettings layer = Layer("background", "options-fixture");
      layer.BlendMode = operation.Id;
      layer.OperationParams = parameters;
      (LayerStackSnapshot snapshot, string error) =
        new LayerStackService(DomeLayerCatalog.Metadata).CreateSnapshot(new[] { layer });
      Assert(error == null, error);
      return operation.CompileOptions(snapshot.Layers[0].OperationParameters);
    }

    private static void HalftoneBuildsPaletteCells() {
      Assert(ReferenceEquals(
          DomeBlend.FromId("Halftone"), DomeBlend.Halftone),
        "Halftone was not registered");
      Assert(DomeBlend.Halftone.Params.Count == 7 &&
          DomeBlend.Halftone.Params.All(p => p.CompositorConsumed),
        "Halftone controls are not compositor-owned");

      DomeTopology topology = ProjectedTopology(
        (.10, .10), (.13, .10), (.19, .10));
      var dest = new DomeFrame(topology);
      var mask = new DomeFrame(topology);
      for (int i = 0; i < dest.pixels.Length; i++) {
        dest.pixels[i].color = 0x808080;
        dest.pixels[i].SetAlpha(.25);
        dest.pixels[i].hue = i / 10d;
        mask.pixels[i].color = 0xFFFFFF;
      }
      var snapshot = new DomeFrame(topology);
      snapshot.CopyFrom(dest);
      var options = new HalftoneOptions(
        0, .2, 0, 0, 1, 0, 3);
      int paletteCalls = 0;
      DomeBlend.Halftone.Execute(new DomeBlendContext(
        dest, mask, snapshot, options, 1, 0, null,
        paletteColor: (palette, position) => {
          Assert(palette == 3, "Halftone used the wrong palette");
          AssertClose(128d / 255, position,
            "Halftone did not palette-map sampled brightness");
          paletteCalls++;
          return 0xFF0000;
        }));
      Assert(dest.pixels[0].r == 255 && dest.pixels[0].g == 0 &&
          dest.pixels[0].b == 0,
        "Halftone did not light the center of a dot");
      Assert(dest.pixels[2].color == 0,
        "Halftone did not replace the gap between dots with black");
      Assert(dest.pixels[0].a == .25 && dest.pixels[0].hue == 0 &&
          dest.pixels[1].hue == .1,
        "Halftone changed destination side channels");
      Assert(paletteCalls == 3,
        "Halftone did not resolve one palette color per masked pixel");

      DomeTopology triangleTopology = ProjectedTopology(
        (.10, .2 / 3 * Math.Sqrt(3) / 2), (.19, .01));
      var triangleDest = new DomeFrame(triangleTopology);
      var triangleMask = new DomeFrame(triangleTopology);
      for (int i = 0; i < triangleDest.pixels.Length; i++) {
        triangleDest.pixels[i].color = 0x808080;
        triangleMask.pixels[i].color = 0xFFFFFF;
      }
      var triangleSnapshot = new DomeFrame(triangleTopology);
      triangleSnapshot.CopyFrom(triangleDest);
      DomeBlend.Halftone.Execute(new DomeBlendContext(
        triangleDest, triangleMask, triangleSnapshot,
        options with { CellType = 1 }, 1, 0, null));
      Assert(triangleDest.pixels[0].color == 0xFFFFFF &&
          triangleDest.pixels[1].color == 0,
        "Halftone did not size an equilateral triangle around its centroid");

      // Exercise the topology-native mode on a short final segment: its sample
      // must clamp to the physical strut rather than indexing beyond the frame.
      DomeTopology strutTopology = LinearTopology(5);
      var strutDest = new DomeFrame(strutTopology);
      var strutMask = new DomeFrame(strutTopology);
      for (int i = 0; i < strutDest.pixels.Length; i++) {
        strutDest.pixels[i].color = 0x404040;
        strutMask.pixels[i].color = 0xFFFFFF;
      }
      var strutSnapshot = new DomeFrame(strutTopology);
      strutSnapshot.CopyFrom(strutDest);
      DomeBlend.Halftone.Execute(new DomeBlendContext(
        strutDest, strutMask, strutSnapshot,
        options with { CellType = 2, Scale = .052 },
        1, 0, null));
      Assert(strutDest.pixels.Any(p => p.color != 0),
        "Halftone strut segments produced no luminous cells");
    }

    private static void SpatialRequirements() {
      foreach (DomeBlend operation in new[] {
        DomeBlend.ChromaticFringe, DomeBlend.EdgeSpectrum, DomeBlend.Refract,
        DomeBlend.Kaleidoscope, DomeBlend.Echo, DomeBlend.Halftone,
      }) {
        Assert((operation.Requirements &
          CompositeRequirements.ReadsDestinationNeighbors) != 0,
          operation.Id + " omitted neighbor requirement");
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
        operation.Id + " fixture expected " + expectedFull +
        " but got " + actual);

      DomeFrame half = ComposeFixture(operation, parameters, .5);
      int[] bottomColors = FixtureBottomColors();
      for (int i = 0; i < bottomColors.Length; i++) {
        double br = (bottomColors[i] >> 16) & 0xFF;
        double bg = (bottomColors[i] >> 8) & 0xFF;
        double bb = bottomColors[i] & 0xFF;
        AssertClose((br + full.pixels[i].r) / 2, half.pixels[i].r,
          operation.Id + " half-opacity red " + i);
        AssertClose((bg + full.pixels[i].g) / 2, half.pixels[i].g,
          operation.Id + " half-opacity green " + i);
        AssertClose((bb + full.pixels[i].b) / 2, half.pixels[i].b,
          operation.Id + " half-opacity blue " + i);
        AssertClose(0, full.pixels[i].a,
          operation.Id + " changed the blank destination alpha " + i);
        AssertClose(i / 10d, full.pixels[i].hue,
          operation.Id + " changed destination hue " + i);
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
      DomeLayerSettings serializedLayer = Layer(
        "background", "serialize-background");
      serializedLayer.RendererParams = new Dictionary<string, double> {
        ["color"] = 0x123456,
      };
      serializedLayer.OperationParams = new Dictionary<string, double> {
        ["amount"] = .5,
      };
      var config = new global::Spectrum.SpectrumConfiguration {
        audioDeviceID = "test-device",
      };
      config.ReplaceDomeLayerStack(new[] { serializedLayer });
      config.ReplaceMidiLevelDriverChannels(
        new Dictionary<int, MidiLevelDriverPreset> {
          [2] = new MidiLevelDriverPreset {
            AttackTime = 10,
            PeakLevel = 0.9,
            DecayTime = 20,
            SustainLevel = 0.7,
            ReleaseTime = 30,
          },
        });
      using var stream = new MemoryStream();
      new XmlSerializer<global::Spectrum.SpectrumConfigurationDocument>()
        .Serialize(
          stream,
          global::Spectrum.SpectrumConfigurationDocument.FromConfiguration(
            config));
      Assert(stream.Length > 0, "serializer produced no XML");
      string xml = System.Text.Encoding.UTF8.GetString(stream.ToArray());
      Assert(xml.Contains("<SpectrumConfiguration") &&
          !xml.Contains("<SpectrumConfigurationDocument"),
        "the configuration document emitted the wrong XML root");
      stream.Position = 0;
      var restored =
        new XmlSerializer<global::Spectrum.SpectrumConfigurationDocument>()
          .Deserialize(stream).ToConfiguration();
      Assert(restored.audioDeviceID == "test-device", "round trip lost config");
      Assert(restored.domeLayerStack.Length == 1 &&
        restored.domeLayerStack[0].InstanceId == "serialize-background",
        "round trip lost layer identity");
      Assert(restored.domeLayerStack[0].RendererParams["color"] == 0x123456 &&
          restored.domeLayerStack[0].OperationParams["amount"] == .5,
        "round trip merged or lost parameter namespaces");
      BeatSettingsSnapshot beat =
        ((IRuntimeSettingsConfiguration)restored).BeatSettingsSnapshot;
      Assert(beat.TryGetMidiPreset(
          2, out MidiLevelDriverSettingsSnapshot envelope) &&
          envelope.AttackTime == 10 && envelope.PeakLevel == 0.9 &&
          envelope.DecayTime == 20 && envelope.SustainLevel == 0.7 &&
          envelope.ReleaseTime == 30,
        "round trip lost the MIDI level-driver channel");
    }

    private static void ConfigurationCollectionsIsolateNestedAliases() {
      var layer = Layer("background", "alias-layer");
      layer.RendererParams = new Dictionary<string, double> {
        ["color"] = 0x123456,
      };
      var sceneLayer = Layer("background", "alias-scene-layer");
      sceneLayer.OperationParams = new Dictionary<string, double> {
        ["amount"] = 0.25,
      };
      var paletteColor = new LEDColor(0x112233, 0x445566);
      var binding = new ContinuousKnobMidiBindingConfig {
        BindingName = "alias-binding",
        knobIndex = 7,
        configPropertyName = nameof(Configuration.domeBrightness),
        startValue = 0,
        endValue = 1,
      };
      var preset = new MidiPreset {
        id = 3,
        Name = "Alias preset",
        Bindings = new List<IMidiBindingConfig> { binding },
      };
      var config = new global::Spectrum.SpectrumConfiguration();
      config.ReplaceDomeLayerStack(new[] { layer });
      config.ReplaceDomeScenes(new[] {
        new DomeScene {
          Name = "Alias scene",
          Layers = new List<DomeLayerSettings> { sceneLayer },
        },
      });
      config.ReplaceDomePalettes(new[] {
        new DomePalette {
          Name = "Alias palette",
          Colors = new[] { paletteColor },
        },
      });
      config.ReplaceMidiPresets(new Dictionary<int, MidiPreset> {
        [3] = preset,
      });

      layer.RendererParams["color"] = 0;
      sceneLayer.OperationParams["amount"] = 1;
      paletteColor.color1 = 0;
      binding.endValue = 0;
      preset.Bindings.Clear();
      Assert(config.domeLayerStack[0].RendererParams["color"] == 0x123456 &&
          config.domeScenes[0].Layers[0].OperationParams["amount"] == 0.25 &&
          config.domePalettes[0].Colors[0].Value.Color1 == 0x112233 &&
          config.midiPresets[3].Bindings.Length == 1 &&
          ((ContinuousKnobMidiBindingView)
            config.midiPresets[3].Bindings[0]).EndValue == 1,
        "a collection edit retained a nested source alias");

      global::Spectrum.SpectrumConfigurationDocument document =
        global::Spectrum.SpectrumConfigurationDocument.FromConfiguration(
          config);
      document.domeLayerStack[0].RendererParams["color"] = 7;
      document.domeScenes[0].Layers[0].OperationParams["amount"] = 7;
      document.domePalettes[0].Colors[0].color1 = 7;
      document.midiPresets[3].Bindings.Clear();
      Assert(config.domeLayerStack[0].RendererParams["color"] == 0x123456 &&
          config.domeScenes[0].Layers[0].OperationParams["amount"] == 0.25 &&
          config.domePalettes[0].Colors[0].Value.Color1 == 0x112233 &&
          config.midiPresets[3].Bindings.Length == 1,
        "the persistence document retained a live configuration alias");
    }

    private static void ConfigurationCollectionNotificationsAreExact() {
      var config = new global::Spectrum.SpectrumConfiguration();
      var names = new List<string>();
      config.PropertyChanged += (_, e) => names.Add(e.PropertyName);

      config.ReplaceDomeLayerStack(new[] {
        Layer("background", "notification-layer"),
      });
      Assert(names.SequenceEqual(new[] {
          nameof(config.domeLayerStack),
          DomeShowStateSnapshot.NotificationPropertyName,
        }),
        "a layer replacement published extra or missing notifications");

      names.Clear();
      config.ReplaceDomePalettes(new[] {
        new DomePalette { Name = "Notification palette" },
      });
      Assert(names.SequenceEqual(new[] {
          nameof(config.domePalettes),
          DomeShowStateSnapshot.NotificationPropertyName,
        }),
        "a palette replacement published extra or missing notifications");

      names.Clear();
      config.UpsertMidiPreset(4, new MidiPreset {
        id = 4,
        Name = "Notification MIDI",
      });
      Assert(names.SequenceEqual(new[] { nameof(config.midiPresets) }),
        "a MIDI preset edit published extra or missing notifications");
    }

    private static void ConfigurationSurfaceRejectsMutableCollections() {
      Type[] mutableDefinitions = {
        typeof(List<>),
        typeof(Dictionary<,>),
        typeof(IList<>),
        typeof(IDictionary<,>),
        typeof(ICollection<>),
      };
      foreach (Type surface in new[] {
          typeof(Configuration),
          typeof(global::Spectrum.SpectrumConfiguration),
        }) {
        foreach (System.Reflection.PropertyInfo property in
            surface.GetProperties()) {
          Type type = property.PropertyType;
          bool mutable = type.IsArray ||
            type.IsGenericType && mutableDefinitions.Contains(
              type.GetGenericTypeDefinition());
          Assert(!mutable,
            surface.Name + "." + property.Name +
            " exposes mutable collection type " + type.Name);
        }
      }
    }

    private static void EventStreamSubscribersAreBounded() {
      var config = ConfigurationWithLayers(
        Layer("background", "coalesced-show"));
      var telemetry = new RuntimeTelemetry();
      using var stream = new global::Spectrum.Web.ConfigEventStream(
        global::Spectrum.Web.SpectrumParameters.BuildRegistry(),
        config, null, telemetry, null);
      global::Spectrum.Web.ConfigEventStream.Subscriber subscriber =
        stream.Subscribe(ControlRole.Maintenance, out Guid id);
      config.domeGlobalFadeSpeed = 0.75;
      int writes =
        global::Spectrum.Web.ConfigEventStream.SubscriberCapacity + 37;
      for (int value = 1; value <= writes; value++) {
        telemetry.OperatorFPS = value;
      }

      var retained = new List<string>();
      while (subscriber.Reader.TryRead(out string frame)) {
        retained.Add(frame);
      }
      Assert(retained.Count == 2 &&
          retained.Any(frame =>
            frame.Contains("\"kind\":\"show\"") &&
            frame.Contains("coalesced-show")) &&
          retained.Any(frame =>
            frame.Contains("\"key\":\"operatorFPS\"") &&
            frame.Contains("\"value\":" + writes)),
        "an unrelated telemetry flood displaced infrequent show state");

      for (int key = 0;
          key <= global::Spectrum.Web.ConfigEventStream.SubscriberCapacity;
          key++) {
        subscriber.Write("test", "distinct-" + key, "{}");
      }
      retained.Clear();
      while (subscriber.Reader.TryRead(out string frame)) {
        retained.Add(frame);
      }
      Assert(retained.Count <=
          global::Spectrum.Web.ConfigEventStream.SubscriberCapacity &&
          retained.Any(frame =>
            frame.Contains("\"kind\":\"reset\"")),
        "distinct SSE state overflow was not bounded by a resync marker");
      stream.Unsubscribe(id);
    }

    private static void WebLayerContract() {
      DomeLayerSettings layer = Layer("background", "web-background");
      layer.RendererParams = new Dictionary<string, double> {
        ["color"] = 0xABCDEF,
      };
      layer.BlendMode = DomeBlend.ChromaticFringe.Id;
      layer.OperationParams = new Dictionary<string, double> {
        ["offset"] = .125,
      };
      var config = ConfigurationWithLayers(layer);
      var controller = new global::Spectrum.Web.LayersController(
        new InlineGateway(), config);
      global::Spectrum.Web.LayersController.LayersState state =
        controller.State();
      Assert(state.layers[0].rendererParams["color"] == 0xABCDEF &&
        state.layers[0].operationParams["offset"] == .125,
        "web contract merged parameter namespaces");
      global::Spectrum.Web.LayersController.OperationOptionDto operation =
        null;
      foreach (
        global::Spectrum.Web.LayersController.OperationOptionDto candidate
        in state.operations
      ) {
        if (candidate.id == DomeBlend.ChromaticFringe.Id) {
          operation = candidate;
          break;
        }
      }
      Assert(operation != null &&
        operation.label == DomeBlend.ChromaticFringe.DisplayName &&
        operation.@params.Count > 0,
        "web operation descriptor is incomplete");
      global::Spectrum.Web.LayersController.VisualizerOptionDto astronomy =
        state.visualizers.FirstOrDefault(v => v.key == "astronomy");
      global::Spectrum.Web.LayersController.VisualizerOptionDto background =
        state.visualizers.FirstOrDefault(v => v.key == "background");
      global::Spectrum.Web.LayersController.ParamDto startDate =
        astronomy?.@params.FirstOrDefault(p => p.key == "startDate");
      global::Spectrum.Web.LayersController.ParamDto showDaytimeSky =
        astronomy?.@params.FirstOrDefault(
          p => p.key == "showDaytimeSky");
      global::Spectrum.Web.LayersController.ParamDto showNighttimeSky =
        astronomy?.@params.FirstOrDefault(
          p => p.key == "showNighttimeSky");
      Assert(startDate != null && startDate.type == "Date" &&
          DomeLayerDate.TryDecode(startDate.@default, out _),
        "web astronomy start-date descriptor is incomplete");
      Assert(showDaytimeSky != null && showDaytimeSky.type == "Bool" &&
          showDaytimeSky.@default == 1,
        "web astronomy daytime-sky checkbox descriptor is incomplete");
      Assert(showNighttimeSky != null &&
          showNighttimeSky.type == "Bool" &&
          showNighttimeSky.@default == 1,
        "web astronomy nighttime-sky checkbox descriptor is incomplete");
      Assert(astronomy?.fireAction?.label == "Play" &&
          astronomy.clearAction?.label == "Stop" &&
          background?.fireAction == null && background?.clearAction == null,
        "web layer action descriptors are incomplete");
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

    private static RenderPlan Compile(
      RenderPlanCompiler compiler, LayerRendererStore store,
      LayerStackSnapshot snapshot
    ) => compiler.Compile(
      snapshot, layer => store.Resolve(layer).Renderer);

    private static T BuiltInOptions<T>(DomeLayerSettings layer)
      where T : class, ILayerRendererOptions {
      (LayerStackSnapshot snapshot, string error) =
        new LayerStackService(DomeLayerCatalog.Metadata).CreateSnapshot(new[] { layer });
      if (error != null) {
        throw new InvalidOperationException(error);
      }
      LayerDefinition definition = DomeLayerCatalog.Metadata.Get(
        layer.VisualizerKey);
      ILayerRendererOptions options = definition.CompileOptions(
        snapshot.Layers[0].RendererParameters);
      return options as T ?? throw new InvalidOperationException(
        "Unexpected options type " + options.GetType().Name + ".");
    }

    private static global::Spectrum.SpectrumConfiguration
      ConfigurationWithLayers(params DomeLayerSettings[] layers) {
      var config = new global::Spectrum.SpectrumConfiguration();
      config.ReplaceDomeLayerStack(layers);
      return config;
    }

    private static DomeLayerSettings Layer(string key, string id) => new() {
      InstanceId = id,
      VisualizerKey = key,
      BlendMode = DomeBlend.Add.Id,
      Opacity = 1,
      Enabled = true,
    };

    private static LayerSnapshot SnapshotWithParameter(
      string id, string key, double value
    ) {
      var parameters = ImmutableDictionary<string, ParameterValue>.Empty.Add(
        key, new ParameterValue(DomeLayerParamType.Double, value));
      return new LayerSnapshot(
        new LayerInstanceId(id), "test", DomeBlend.Add.Id, 1, true,
        parameters, ImmutableDictionary<string, ParameterValue>.Empty, null);
    }

    private static LayerSnapshot SnapshotForRenderer(string id, string renderer) =>
      new LayerSnapshot(
        new LayerInstanceId(id), renderer, DomeBlend.Add.Id, 1, true,
        ImmutableDictionary<string, ParameterValue>.Empty,
        ImmutableDictionary<string, ParameterValue>.Empty, null);

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

    private static DomeTopology GridTopology(
      int width, int height, double spacing
    ) {
      var pixels = new DomeTopologyPixel[width * height];
      double left = 0.5 - (width - 1) * spacing / 2;
      double top = 0.5 - (height - 1) * spacing / 2;
      for (int y = 0; y < height; y++) {
        for (int x = 0; x < width; x++) {
          int i = y * width + x;
          pixels[i] = new DomeTopologyPixel(
            0, i, left + x * spacing, top + y * spacing);
        }
      }
      return new DomeTopology(pixels);
    }

    private static DomeTopology RingTopology(int count, double radius) {
      var pixels = new DomeTopologyPixel[count];
      for (int i = 0; i < count; i++) {
        double angle = 2 * Math.PI * i / count;
        double x = radius * Math.Cos(angle);
        double y = radius * Math.Sin(angle);
        pixels[i] = new DomeTopologyPixel(
          0, i, (x + 1) * .5, (1 - y) * .5);
      }
      return new DomeTopology(pixels);
    }

    private static DomeTopology ProjectedTopology(
      params (double X, double Y)[] points
    ) {
      var pixels = new DomeTopologyPixel[points.Length];
      for (int i = 0; i < points.Length; i++) {
        double topDownX = (points[i].X + 1) * .5;
        double topDownY = (1 - points[i].Y) * .5;
        pixels[i] = new DomeTopologyPixel(
          i, 0, topDownX, topDownY, topDownX, topDownY);
      }
      return new DomeTopology(pixels);
    }

    private static void SetPaletteColors(
      global::Spectrum.SpectrumConfiguration config,
      Func<int, int> colorAt
    ) {
      var colors = new LEDColor[DomePalette.SlotCount];
      for (int color = 0; color < colors.Length; color++) {
        colors[color] = new LEDColor(colorAt(color));
      }
      config.ReplaceDomePalettes(new List<DomePalette> {
        new DomePalette { Name = "Test", Colors = colors },
      });
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

    private sealed class DisposableFakeRenderer : ILayerRenderer, IDisposable {
      public string RendererId { get; }
      public DomeFrame Frame { get; }
      public bool IsAvailable => true;
      public IReadOnlyList<Input> RequiredInputs => Array.Empty<Input>();
      public bool Disposed { get; private set; }

      public DisposableFakeRenderer(string id, DomeTopology topology) {
        this.RendererId = id;
        this.Frame = new DomeFrame(topology);
      }

      public void Dispose() => this.Disposed = true;
    }

    private sealed class FakeInput : Input {
      public bool Active { get; set; }
      public bool AlwaysActive => false;
      public bool Enabled => true;
      public void OperatorUpdate() { }
    }

    private sealed class FakeAudioLevelInput : IAudioLevelInput {
      public bool Active { get; set; }
      public bool AlwaysActive => true;
      public bool Enabled => true;
      public float Volume => 0.25f;
      public void OperatorUpdate() { }
    }

    private sealed class FakeMidiControlInput : IMidiControlInput {
      public bool Active { get; set; }
      public bool AlwaysActive => true;
      public bool Enabled => true;
      public ObservableMidiLog MidiLog { get; } = new ObservableMidiLog();
      public long AppliedDeviceGeneration => 0;
      public Task DispatchBindingsAsync(MidiCommand command) =>
        Task.CompletedTask;
      public void OperatorUpdate() { }
    }

    private sealed class FakeSpectrumInputFactory : ISpectrumInputFactory {
      public FakeAudioLevelInput Audio { get; } =
        new FakeAudioLevelInput();
      public FakeMidiControlInput Midi { get; } =
        new FakeMidiControlInput();

      public IAudioLevelInput CreateAudioInput(
        Configuration config,
        BeatBroadcaster beat
      ) => this.Audio;

      public IMidiControlInput CreateMidiInput(
        Configuration config,
        BeatBroadcaster beat,
        ApplicationStateDispatcher stateDispatcher
      ) => this.Midi;
    }


    private sealed class InlineGateway : ApplicationStateDispatcher {
      public bool CheckAccess() => true;
      public void Post(Action mutation) => mutation();
      public Task InvokeAsync(Action mutation) {
        mutation();
        return Task.CompletedTask;
      }
      public Task<T> InvokeAsync<T>(Func<T> read) =>
        Task.FromResult(read());
    }

    private sealed class QueuedStateDispatcher :
      ApplicationStateDispatcher {
      private readonly int ownerThreadId =
        Environment.CurrentManagedThreadId;
      private readonly Queue<Action> pending = new Queue<Action>();
      private readonly AutoResetEvent pendingQueued = new AutoResetEvent(false);

      public bool CheckAccess() =>
        Environment.CurrentManagedThreadId == this.ownerThreadId;

      public int PendingCount {
        get {
          lock (this.pending) {
            return this.pending.Count;
          }
        }
      }

      public void Post(Action mutation) {
        lock (this.pending) {
          this.pending.Enqueue(mutation);
        }
        this.pendingQueued.Set();
      }

      public bool WaitForPending(TimeSpan timeout) =>
        this.pendingQueued.WaitOne(timeout);

      public Task InvokeAsync(Action mutation) {
        if (this.CheckAccess()) {
          mutation();
          return Task.CompletedTask;
        }
        var completion = new TaskCompletionSource(
          TaskCreationOptions.RunContinuationsAsynchronously);
        this.Post(() => {
          try {
            mutation();
            completion.SetResult();
          } catch (Exception error) {
            completion.SetException(error);
          }
        });
        return completion.Task;
      }

      public async Task<T> InvokeAsync<T>(Func<T> read) {
        T result = default;
        await this.InvokeAsync((Action)(() => { result = read(); }));
        return result;
      }

      public void Drain() {
        Assert(this.CheckAccess(),
          "state dispatcher drained from a non-owner thread");
        while (true) {
          Action mutation;
          lock (this.pending) {
            if (this.pending.Count == 0) {
              return;
            }
            mutation = this.pending.Dequeue();
          }
          mutation();
        }
      }
    }

    private sealed class FixedOrientation : OrientationAngleProvider {
      private readonly double angle;
      public int UpdateCount { get; private set; }

      public FixedOrientation(double angle) {
        this.angle = angle;
      }

      public bool TryGetAngle(out double angle) {
        angle = this.angle;
        return true;
      }

      public void Update() => this.UpdateCount++;
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

    private sealed record ScalarOptions(double Value)
      : ILayerRendererOptions;
  }
}
