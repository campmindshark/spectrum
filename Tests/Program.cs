using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Visualizers;
using XSerializer;

namespace Spectrum.LayerPipeline.Tests {

  internal static class Program {
    private static int failures;

    private static void Main() {
      Run("catalog metadata is unique", CatalogIsUnique);
      Run("tunnel parameters compile and clamp", TunnelParametersCompile);
      Run("Ripple and Stamp rings use angular surface distance",
        OrientationRingsUseAngularDistance);
      Run("Ripple desaturation reduces its color saturation",
        RippleDesaturationReducesSaturation);
      Run("Point Cloud stays on the visible dome hemisphere",
        PointCloudUsesVisibleHemisphere);
      Run("Quaternion Test is a modal dome diagnostic",
        QuaternionTestIsDiagnostic);
      Run("duplicate renderer kinds get stable instance IDs", DuplicateKinds);
      Run("parameters compile into separate namespaces", ParameterNamespaces);
      Run("compiled renderer runtime updates in place", RuntimeUpdatesInPlace);
      Run("renderer runtime swaps immutable options", RuntimeOptionsSwap);
      Run("renderer store replaces and evicts instance state",
        RendererStoreLifecycle);
      Run("typed renderer options preserve numeric casts",
        TypedOptionsPreserveNumericCasts);
      Run("Earth texture follows spotlight poles and spins",
        EarthTextureFollowsSpotlight);
      Run("astronomy options and north heading are deterministic",
        AstronomyOptionsAndHeading);
      Run("simulator real view foreshortens the dome from above",
        SimulatorTopDownProjection);
      Run("dome topology stores both projections and unit normals",
        DomeTopologyUsesTopDownNormals);
      Run("sphere directions project into strip-extents coordinates",
        SphereDirectionsProjectToStripExtents);
      Run("targeted planar coordinates round-trip real dome normals",
        TargetedPlanarCoordinatesRoundTripNormals);
      Run("Tunnel fixed mode matches a crown-bound angular field",
        TunnelFixedModeMatchesCrownAxis);
      Run("compiled plan freezes renderer inputs", PlanFreezesRendererInputs);
      Run("configuration publishes immutable layer snapshots",
        ConfigurationPublishesSnapshot);
      Run("compositor executes every operation in stack order", StackOrder);
      Run("scratch copies only mutable channels", ScratchCopiesChannelsOnly);
      Run("frame operations require shared topology", FramesRequireTopology);
      Run("operator creates independent duplicate renderers", DuplicateRenderers);
      Run("operator reboot notifications do not hold the renderer lock",
        RebootNotificationsAreUnlocked);
      Run("layer renderers do not receive persisted configuration",
        LayerRenderersAvoidConfiguration);
      Run("Magnetic Field models signed orientation charges",
        MagneticFieldUsesSignedCharges);
      Run("Ripple Tank is a standalone orientation-speed layer",
        RippleTankIsOrientationOnly);
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
      Run("web layer contract exposes namespaced bags and operation descriptors",
        WebLayerContract);
      StackValidatorTests.Register(Run);
      WandProtocolTests.Register(Run);
      PaletteServiceTests.Register(Run);
      AdvisoryLockTests.Register(Run);
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
      }
    }

    private static void TunnelParametersCompile() {
      LayerDefinition definition = LayerCatalog.Default.Get("tunnel");
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
      LayerDefinition ripple = LayerCatalog.Default.Get("ripple");
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

    private static void QuaternionTestIsDiagnostic() {
      Assert(LayerCatalog.Default.Get("quaternion-test") == null,
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
        new LayerStackService().Normalize(input);
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
        new LayerStackService().CreateSnapshot(new[] { first });
      Assert(initialError == null, initialError);
      LayerDefinition definition = LayerCatalog.Default.Get("wave");
      var runtime = new LayerRendererRuntime(
        initial.Layers[0], definition.CompileOptions);
      WaveLayerOptions original = runtime.GetOptions<WaveLayerOptions>();
      Assert(original.Speed == .25, "initial typed option missing");

      DomeLayerSettings second = Layer("wave", "runtime-wave");
      second.RendererParams = new Dictionary<string, double> { ["speed"] = 1.25 };
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
      LayerDefinition definition = LayerCatalog.Default.Get("earth");
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

      var config = new global::Spectrum.SpectrumConfiguration {
        domeLayerStack = new List<DomeLayerSettings> { layer },
      };
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
        ["playbackSpeed"] = 999,
        ["loop"] = 1,
      };
      AstronomyLayerOptions options =
        BuiltInOptions<AstronomyLayerOptions>(layer);
      Assert(options.NorthHeading == 359 &&
        options.StartDate == 20260715 && options.TimeOffsetHours == 168 &&
        !options.ShowDaytimeSky && options.PlaybackSpeed == 8 && options.Loop,
        "astronomy controls were not clamped by their schema");
      AstronomyLayerOptions defaultOptions =
        BuiltInOptions<AstronomyLayerOptions>(
          Layer("astronomy", "default-astronomy"));
      Assert(defaultOptions.ShowDaytimeSky &&
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
      Assert(LEDDomeAstronomyVisualizer.SkyColor(0, true) == 0x082040 &&
          LEDDomeAstronomyVisualizer.SkyColor(0, false) == 0x000006 &&
          LEDDomeAstronomyVisualizer.SkyColor(1, true) == 0x000006,
        "astronomy daytime-sky toggle did not suppress the sky effect");

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

    private static void MagneticFieldUsesSignedCharges() {
      LayerDefinition definition = LayerCatalog.Default.Get("magnetic-field");
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
      LayerDefinition caustics = LayerCatalog.Default.Get("caustics");
      LayerDefinition rippleTank = LayerCatalog.Default.Get("ripple-tank");
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

      var config = new global::Spectrum.SpectrumConfiguration {
        domeLayerStack = new List<DomeLayerSettings> {
          Layer("caustics", "caustics-inputs"),
          Layer("ripple-tank", "tank-inputs"),
        },
      };
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

      config.domeLayerStack = new List<DomeLayerSettings> {
        Layer("background", "temporary-background"),
      };
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
    }

    private static ICompositeOptions CompileOptions(
      DomeBlend operation, Dictionary<string, double> parameters
    ) {
      DomeLayerSettings layer = Layer("background", "options-fixture");
      layer.BlendMode = operation.Id;
      layer.OperationParams = parameters;
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
        domeLayerStack = new List<DomeLayerSettings> {
          serializedLayer,
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
      Assert(restored.domeLayerStack[0].RendererParams["color"] == 0x123456 &&
        restored.domeLayerStack[0].OperationParams["amount"] == .5,
        "round trip merged or lost parameter namespaces");
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
      var config = new global::Spectrum.SpectrumConfiguration {
        domeLayerStack = new List<DomeLayerSettings> { layer },
      };
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
      global::Spectrum.Web.LayersController.ParamDto startDate =
        astronomy?.@params.FirstOrDefault(p => p.key == "startDate");
      global::Spectrum.Web.LayersController.ParamDto showDaytimeSky =
        astronomy?.@params.FirstOrDefault(
          p => p.key == "showDaytimeSky");
      Assert(startDate != null && startDate.type == "Date" &&
          DomeLayerDate.TryDecode(startDate.@default, out _),
        "web astronomy start-date descriptor is incomplete");
      Assert(showDaytimeSky != null && showDaytimeSky.type == "Bool" &&
          showDaytimeSky.@default == 1,
        "web astronomy daytime-sky checkbox descriptor is incomplete");
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

    private sealed record ScalarOptions(double Value)
      : ILayerRendererOptions;
  }
}
