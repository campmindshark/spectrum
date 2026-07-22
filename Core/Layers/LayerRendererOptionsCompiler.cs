using System;
using System.Collections.Immutable;

namespace Spectrum.Base {

  internal static class LayerRendererOptionsCompiler {
    private static ParameterValue Value(
      ImmutableDictionary<string, ParameterValue> values, string key
    ) {
      if (values != null && values.TryGetValue(key, out ParameterValue value)) {
        return value;
      }
      throw new InvalidOperationException(
        "Compiled renderer options are missing parameter " + key + ".");
    }

    private static double Double(
      ImmutableDictionary<string, ParameterValue> values, string key
    ) => Value(values, key).Value;

    private static int Integer(
      ImmutableDictionary<string, ParameterValue> values, string key
    ) => Value(values, key).AsInteger;

    // A few historical numeric sliders feed integer algorithms even though
    // their persisted schema is Double. Preserve their old C# cast semantics
    // instead of changing fractional values from truncation to rounding.
    private static int TruncatedInteger(
      ImmutableDictionary<string, ParameterValue> values, string key
    ) => (int)Value(values, key).Value;

    private static bool Boolean(
      ImmutableDictionary<string, ParameterValue> values, string key
    ) => Value(values, key).AsBoolean;

    internal static ILayerRendererOptions Empty(
      ImmutableDictionary<string, ParameterValue> values
    ) => EmptyLayerRendererOptions.Instance;

    internal static ILayerRendererOptions Radial(
      ImmutableDictionary<string, ParameterValue> v
    ) => new RadialLayerOptions(
      Integer(v, "effect"), Double(v, "size"), Double(v, "frequency"),
      Double(v, "centerAngle"), Double(v, "centerDistance"),
      Double(v, "centerSpeed"), Double(v, "rotationSpeed"),
      Double(v, "gradientSpeed"), Integer(v, "palette"));

    internal static ILayerRendererOptions Volume(
      ImmutableDictionary<string, ParameterValue> v
    ) => new VolumeLayerOptions(
      TruncatedInteger(v, "animationSize"), Double(v, "rotationSpeed"),
      Double(v, "gradientSpeed"), Integer(v, "palette"));

    internal static ILayerRendererOptions Race(
      ImmutableDictionary<string, ParameterValue> v
    ) => new RaceLayerOptions(
      Double(v, "speed"), Double(v, "spacing"), Integer(v, "palette"));

    internal static ILayerRendererOptions Palette(
      ImmutableDictionary<string, ParameterValue> v
    ) => new PaletteLayerOptions(Integer(v, "palette"));

    internal static ILayerRendererOptions Twinkle(
      ImmutableDictionary<string, ParameterValue> v
    ) => new TwinkleLayerOptions(Double(v, "density"));

    internal static ILayerRendererOptions Paintbrush(
      ImmutableDictionary<string, ParameterValue> v
    ) => new PaintbrushLayerOptions(
      Double(v, "size"), Double(v, "twinkleDensity"),
      Double(v, "rippleCDStep"), Double(v, "rippleStep"));

    internal static ILayerRendererOptions Ripple(
      ImmutableDictionary<string, ParameterValue> v
    ) => new RippleLayerOptions(
      Double(v, "rippleStep"), Double(v, "desaturation"),
      Integer(v, "trigger"), Integer(v, "button"), Double(v, "level"),
      Double(v, "interval"));

    internal static ILayerRendererOptions Stamp(
      ImmutableDictionary<string, ParameterValue> v
    ) => new StampLayerOptions(
      Integer(v, "trigger"), Integer(v, "button"), Double(v, "level"),
      Double(v, "interval"));

    internal static ILayerRendererOptions Tunnel(
      ImmutableDictionary<string, ParameterValue> v
    ) => new TunnelLayerOptions(
      TruncatedInteger(v, "count"), Double(v, "speed"),
      Double(v, "thickness"), Double(v, "brightness"),
      Double(v, "variation"), Boolean(v, "bindOrientation"),
      Integer(v, "color"));

    internal static ILayerRendererOptions Metaball(
      ImmutableDictionary<string, ParameterValue> v
    ) => new MetaballLayerOptions(
      Double(v, "size"), Boolean(v, "contours"), Integer(v, "button"));

    internal static ILayerRendererOptions MagneticField(
      ImmutableDictionary<string, ParameterValue> v
    ) => new MagneticFieldLayerOptions(
      Double(v, "strength"), Integer(v, "positiveColor"),
      Integer(v, "negativeColor"), TruncatedInteger(v, "lineCount"),
      Double(v, "lineWidth"));

    internal static ILayerRendererOptions Background(
      ImmutableDictionary<string, ParameterValue> v
    ) => new BackgroundLayerOptions(Integer(v, "color"));

    internal static ILayerRendererOptions Earth(
      ImmutableDictionary<string, ParameterValue> v
    ) => new EarthLayerOptions(Double(v, "spinSpeed"));

    internal static ILayerRendererOptions Astronomy(
      ImmutableDictionary<string, ParameterValue> v
    ) => new AstronomyLayerOptions(
      Double(v, "northHeading"), Integer(v, "startDate"),
      Double(v, "timeOffsetHours"), Boolean(v, "showDaytimeSky"),
      Boolean(v, "showNighttimeSky"), Double(v, "playbackSpeed"),
      Boolean(v, "loop"));

    internal static ILayerRendererOptions Flash(
      ImmutableDictionary<string, ParameterValue> v
    ) => new FlashLayerOptions(
      Integer(v, "color"), Integer(v, "trigger"), Integer(v, "button"),
      Double(v, "level"), Double(v, "interval"));

    internal static ILayerRendererOptions Wave(
      ImmutableDictionary<string, ParameterValue> v
    ) => new WaveLayerOptions(
      Double(v, "bandWidth"), Double(v, "speed"), Double(v, "centerAngle"),
      Double(v, "centerDistance"), Integer(v, "color"),
      Integer(v, "mode") == 1, Integer(v, "button"));

    internal static ILayerRendererOptions PointCloud(
      ImmutableDictionary<string, ParameterValue> v
    ) => new PointCloudLayerOptions(
      TruncatedInteger(v, "count"), Double(v, "spotSize"),
      Double(v, "pushRadius"), Double(v, "pushStrength"),
      Double(v, "springStrength"), Double(v, "damping"));

    internal static ILayerRendererOptions ShootingStar(
      ImmutableDictionary<string, ParameterValue> v
    ) => new ShootingStarLayerOptions(
      Double(v, "spawnRate"), Double(v, "accel"), Double(v, "maxSpeed"),
      Double(v, "size"), Boolean(v, "homing"), Integer(v, "trigger"),
      Integer(v, "button"), Double(v, "level"), Double(v, "interval"),
      Integer(v, "palette"));

    internal static ILayerRendererOptions Sparkler(
      ImmutableDictionary<string, ParameterValue> v
    ) => new SparklerLayerOptions(
      Double(v, "emissionRate"), Double(v, "speed"), Double(v, "size"),
      Integer(v, "trigger"), Integer(v, "button"), Double(v, "level"),
      Double(v, "interval"), Integer(v, "palette"));

    internal static ILayerRendererOptions Gyroscope(
      ImmutableDictionary<string, ParameterValue> v
    ) => new GyroscopeLayerOptions(
      Double(v, "ringWidth"), Double(v, "rotorRate"),
      Integer(v, "palette"));

    internal static ILayerRendererOptions WatchfulIris(
      ImmutableDictionary<string, ParameterValue> v
    ) => new WatchfulIrisLayerOptions(
      TruncatedInteger(v, "irisComplexity"), Double(v, "pupilSize"),
      Double(v, "dilationGain"), Integer(v, "blinkTrigger"),
      Double(v, "eyelidSoftness"), Double(v, "scleraBrightness"),
      Integer(v, "palette"));

    internal static ILayerRendererOptions LivingSkin(
      ImmutableDictionary<string, ParameterValue> v
    ) => new LivingSkinLayerOptions(
      Double(v, "feedRate"), Double(v, "killRate"),
      Double(v, "diffusionScale"), Double(v, "simulationSpeed"),
      Integer(v, "seedSource"), Double(v, "edgeContrast"),
      Integer(v, "feedButton"), Integer(v, "poisonButton"),
      Integer(v, "eraseButton"), TruncatedInteger(v, "brushRadius"),
      Double(v, "brushStrength"), Integer(v, "palette"));

    internal static ILayerRendererOptions ArcLightning(
      ImmutableDictionary<string, ParameterValue> v
    ) => new ArcLightningLayerOptions(
      TruncatedInteger(v, "branchCount"), Double(v, "jaggedness"),
      TruncatedInteger(v, "width"), Double(v, "afterglow"),
      Double(v, "duration"), Integer(v, "trigger"),
      Integer(v, "button"), Double(v, "level"),
      Double(v, "interval"), Integer(v, "palette"));

    internal static ILayerRendererOptions GlassMosaic(
      ImmutableDictionary<string, ParameterValue> v
    ) => new GlassMosaicLayerOptions(
      TruncatedInteger(v, "tileGrouping"), Double(v, "cascadeSpeed"),
      Integer(v, "propagationRule"), Double(v, "borderBrightness"),
      Integer(v, "tileTransition"), Integer(v, "trigger"),
      Integer(v, "button"), Double(v, "level"), Double(v, "interval"),
      Integer(v, "palette"));

    internal static ILayerRendererOptions CellularDome(
      ImmutableDictionary<string, ParameterValue> v
    ) => new CellularDomeLayerOptions(
      Integer(v, "rule"), Integer(v, "neighborhood"),
      Double(v, "generationRate"), Integer(v, "birthColor"),
      Double(v, "ageDecay"), Integer(v, "triggerMode"),
      Integer(v, "palette"));

    internal static ILayerRendererOptions FireflySwarm(
      ImmutableDictionary<string, ParameterValue> v
    ) => new FireflySwarmLayerOptions(
      TruncatedInteger(v, "population"), Double(v, "cohesion"),
      Double(v, "separation"), Double(v, "wander"),
      Integer(v, "interactionMode"), Double(v, "dotSize"),
      Double(v, "trailLength"), Integer(v, "palette"));

    internal static ILayerRendererOptions RainChamber(
      ImmutableDictionary<string, ParameterValue> v
    ) => new RainChamberLayerOptions(
      Double(v, "rainfallRate"), Double(v, "gravity"),
      Double(v, "dropletSize"), Double(v, "trailRetention"),
      Integer(v, "interactionMode"), Double(v, "wind"),
      Double(v, "splashStrength"),
      Integer(v, "palette"));

    internal static ILayerRendererOptions TopographicDream(
      ImmutableDictionary<string, ParameterValue> v
    ) => new TopographicDreamLayerOptions(
      Double(v, "terrainScale"), Double(v, "evolutionSpeed"),
      Double(v, "contourInterval"), Double(v, "lineWidth"),
      Double(v, "seaLevel"), Boolean(v, "bindOrientation"),
      Integer(v, "palette"));

    internal static ILayerRendererOptions OrbitalGarden(
      ImmutableDictionary<string, ParameterValue> v
    ) => new OrbitalGardenLayerOptions(
      TruncatedInteger(v, "bodyCount"), Double(v, "gravity"),
      Double(v, "orbitalDamping"), Integer(v, "collisionBehavior"),
      Double(v, "trailLength"), Double(v, "bodySize"),
      Integer(v, "palette"));

    internal static ILayerRendererOptions LavaLampSky(
      ImmutableDictionary<string, ParameterValue> v
    ) => new LavaLampSkyLayerOptions(
      TruncatedInteger(v, "blobCount"), Double(v, "viscosity"),
      Double(v, "buoyancy"), Double(v, "surfaceTension"),
      Double(v, "heat"), Boolean(v, "bindGravity"),
      Integer(v, "palette"));

    internal static ILayerRendererOptions NoiseCloud(
      ImmutableDictionary<string, ParameterValue> v
    ) => new NoiseCloudLayerOptions(
      Double(v, "scale"), Double(v, "speed"),
      TruncatedInteger(v, "octaves"), Double(v, "contrast"),
      Integer(v, "color"));

    internal static ILayerRendererOptions Vortex(
      ImmutableDictionary<string, ParameterValue> v
    ) => new VortexLayerOptions(
      Integer(v, "style"), Double(v, "speed"),
      Boolean(v, "audioBrightness"), Boolean(v, "audioSpeed"),
      Double(v, "twist"), Double(v, "scale"), Double(v, "density"),
      Double(v, "coreSize"), Double(v, "inflow"),
      Double(v, "turbulence"), Integer(v, "color"));

    internal static ILayerRendererOptions Caustics(
      ImmutableDictionary<string, ParameterValue> v
    ) => new CausticsLayerOptions(
      Integer(v, "method"), Double(v, "scale"), Double(v, "speed"),
      Double(v, "sharpness"), Double(v, "brightness"), Integer(v, "color"));

    internal static ILayerRendererOptions RippleTank(
      ImmutableDictionary<string, ParameterValue> v
    ) => new RippleTankLayerOptions(
      Double(v, "speed"), Double(v, "damping"), Double(v, "sharpness"),
      Double(v, "brightness"), Integer(v, "color"));
  }

}
