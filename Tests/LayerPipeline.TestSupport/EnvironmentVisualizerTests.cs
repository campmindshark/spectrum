using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Visualizers;
using static Spectrum.LayerPipeline.Tests.TestAssertions;
using static Spectrum.LayerPipeline.Tests.LayerPipelineTestFixtures;

namespace Spectrum.LayerPipeline.Tests {

  public static class EnvironmentVisualizerTests {
    public static void Register(Action<string, Action> run) {
      run(nameof(TopographicDreamUsesEvolvingContours), TopographicDreamUsesEvolvingContours);
      run(nameof(OrbitalGardenUsesSphericalOrbits), OrbitalGardenUsesSphericalOrbits);
      run(nameof(LavaLampSkyUsesViscousThermalBlobs), LavaLampSkyUsesViscousThermalBlobs);
      run(nameof(VortexUsesGlobalFade), VortexUsesGlobalFade);
    }
    private static void TopographicDreamUsesEvolvingContours() {
      LayerDefinition? definition =
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
      DomeLayerVisualizer? topographic = null;
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
      LayerDefinition? definition = DomeLayerCatalog.Metadata.Get("orbital-garden");
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
      DomeLayerVisualizer? orbital = null;
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
      LayerDefinition? definition = DomeLayerCatalog.Metadata.Get("lava-lamp-sky");
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
      DomeLayerVisualizer? lava = null;
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
      DomeLayerVisualizer? vortex = null;
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

  }
}