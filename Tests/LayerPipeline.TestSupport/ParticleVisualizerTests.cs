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

  public static class ParticleVisualizerTests {
    public static void Register(Action<string, Action> run) {
      run(nameof(FireflySwarmUsesCoherentFlock), FireflySwarmUsesCoherentFlock);
      run(nameof(RainChamberUsesSphericalRain), RainChamberUsesSphericalRain);
    }
    private static void FireflySwarmUsesCoherentFlock() {
      LayerDefinition? definition = DomeLayerCatalog.Metadata.Get("firefly-swarm");
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
      DomeLayerVisualizer? fireflies = null;
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
      LayerDefinition? definition = DomeLayerCatalog.Metadata.Get("rain-chamber");
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
      DomeLayerVisualizer? rain = null;
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

  }
}