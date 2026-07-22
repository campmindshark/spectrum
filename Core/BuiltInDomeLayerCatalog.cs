using Spectrum.Base;

namespace Spectrum {

  /**
   * Platform-neutral manifest for every built-in dome layer.
   *
   * Concrete renderer factories are bound by the runtime composition root;
   * configuration validation, UIs, and headless services can consume the same
   * stable IDs, schemas, and option compilers without loading those factories.
   */
  public static class BuiltInDomeLayerCatalog {
    private static readonly LayerActionDefinition FireAction =
      new LayerActionDefinition("Fire", "Fire manual trigger");
    private static readonly LayerActionDefinition BlinkAction =
      new LayerActionDefinition("Blink", "Blink the eye");
    private static readonly LayerActionDefinition ClearAction =
      new LayerActionDefinition("Clear", "Clear this layer's live state");
    private static readonly LayerActionDefinition PlayAction =
      new LayerActionDefinition(
        "Play", "Play the one-week astronomy timeline");
    private static readonly LayerActionDefinition StopAction =
      new LayerActionDefinition(
        "Stop", "Stop astronomy playback at the current time");

    // Stable ordering is part of both picker contracts and the serialized
    // configuration's visualizer identity contract.
    public static LayerCatalog Metadata { get; } = new LayerCatalog(new[] {
      Definition(
        "volume", "Volume (OG)", LayerParameterSchemas.VolumeParams,
        LayerRendererOptionsCompiler.Volume),
      Definition(
        "radial", "Radial Effects", LayerParameterSchemas.RadialParams,
        LayerRendererOptionsCompiler.Radial),
      Definition(
        "race", "Race", LayerParameterSchemas.RaceParams,
        LayerRendererOptionsCompiler.Race),
      Definition(
        "snakes", "Snakes", LayerParameterSchemas.SnakesParams,
        LayerRendererOptionsCompiler.Palette),
      Definition(
        "splat", "Splat Effect", LayerParameterSchemas.SplatParams,
        LayerRendererOptionsCompiler.Palette),
      Definition(
        "quaternion-paintbrush", "Quaternion Paintbrush",
        LayerParameterSchemas.PaintbrushParams,
        LayerRendererOptionsCompiler.Paintbrush),
      Definition(
        "tv-static", "TV Static", LayerParameterSchemas.NoParams,
        LayerRendererOptionsCompiler.Empty),
      Definition(
        "twinkle", "Twinkle", LayerParameterSchemas.TwinkleParams,
        LayerRendererOptionsCompiler.Twinkle),
      Definition(
        "flash", "Flash", LayerParameterSchemas.FlashParams,
        LayerRendererOptionsCompiler.Flash, FireAction),
      Definition(
        "background", "Background", LayerParameterSchemas.BackgroundParams,
        LayerRendererOptionsCompiler.Background),
      Definition(
        "earth", "Earth", LayerParameterSchemas.EarthParams,
        LayerRendererOptionsCompiler.Earth),
      Definition(
        "astronomy", "Astronomy", LayerParameterSchemas.AstronomyParams,
        LayerRendererOptionsCompiler.Astronomy, PlayAction, StopAction),
      Definition(
        "wave", "Wave", LayerParameterSchemas.WaveParams,
        LayerRendererOptionsCompiler.Wave, FireAction),
      Definition(
        "ripple", "Ripple", LayerParameterSchemas.RippleParams,
        LayerRendererOptionsCompiler.Ripple, FireAction),
      Definition(
        "stamp", "Stamp", LayerParameterSchemas.StampParams,
        LayerRendererOptionsCompiler.Stamp, FireAction),
      Definition(
        "tunnel", "Tunnel", LayerParameterSchemas.TunnelParams,
        LayerRendererOptionsCompiler.Tunnel),
      Definition(
        "metaball", "Metaball", LayerParameterSchemas.MetaballParams,
        LayerRendererOptionsCompiler.Metaball, FireAction),
      Definition(
        "magnetic-field", "Magnetic Field",
        LayerParameterSchemas.MagneticFieldParams,
        LayerRendererOptionsCompiler.MagneticField),
      Definition(
        "point-cloud", "Point Cloud", LayerParameterSchemas.PointCloudParams,
        LayerRendererOptionsCompiler.PointCloud),
      Definition(
        "gyroscope", "Gyroscope", LayerParameterSchemas.GyroscopeParams,
        LayerRendererOptionsCompiler.Gyroscope),
      Definition(
        "watchful-iris", "Watchful Iris",
        LayerParameterSchemas.WatchfulIrisParams,
        LayerRendererOptionsCompiler.WatchfulIris, BlinkAction),
      Definition(
        "shooting-star", "Shooting Star",
        LayerParameterSchemas.ShootingStarParams,
        LayerRendererOptionsCompiler.ShootingStar, FireAction, ClearAction),
      Definition(
        "sparkler", "Sparkler", LayerParameterSchemas.SparklerParams,
        LayerRendererOptionsCompiler.Sparkler, FireAction, ClearAction),
      Definition(
        "noise-cloud", "Noise Cloud", LayerParameterSchemas.NoiseCloudParams,
        LayerRendererOptionsCompiler.NoiseCloud),
      Definition(
        "caustics", "Caustics", LayerParameterSchemas.CausticsParams,
        LayerRendererOptionsCompiler.Caustics),
      Definition(
        "ripple-tank", "Ripple Tank",
        LayerParameterSchemas.RippleTankParams,
        LayerRendererOptionsCompiler.RippleTank,
        clearAction: ClearAction),
      Definition(
        "vortex", "Vortex", LayerParameterSchemas.VortexParams,
        LayerRendererOptionsCompiler.Vortex),
      Definition(
        "living-skin", "Living Skin",
        LayerParameterSchemas.LivingSkinParams,
        LayerRendererOptionsCompiler.LivingSkin, FireAction, ClearAction),
      Definition(
        "arc-lightning", "Arc Lightning",
        LayerParameterSchemas.ArcLightningParams,
        LayerRendererOptionsCompiler.ArcLightning, FireAction, ClearAction),
      Definition(
        "glass-mosaic", "Glass Mosaic",
        LayerParameterSchemas.GlassMosaicParams,
        LayerRendererOptionsCompiler.GlassMosaic, FireAction, ClearAction),
      Definition(
        "cellular-dome", "Cellular Dome",
        LayerParameterSchemas.CellularDomeParams,
        LayerRendererOptionsCompiler.CellularDome, FireAction, ClearAction),
      Definition(
        "firefly-swarm", "Firefly Swarm",
        LayerParameterSchemas.FireflySwarmParams,
        LayerRendererOptionsCompiler.FireflySwarm),
      Definition(
        "rain-chamber", "Rain Chamber",
        LayerParameterSchemas.RainChamberParams,
        LayerRendererOptionsCompiler.RainChamber),
      Definition(
        "topographic-dream", "Topographic Dream",
        LayerParameterSchemas.TopographicDreamParams,
        LayerRendererOptionsCompiler.TopographicDream),
      Definition(
        "orbital-garden", "Orbital Garden",
        LayerParameterSchemas.OrbitalGardenParams,
        LayerRendererOptionsCompiler.OrbitalGarden),
      Definition(
        "lava-lamp-sky", "Lava Lamp Sky",
        LayerParameterSchemas.LavaLampSkyParams,
        LayerRendererOptionsCompiler.LavaLampSky),
    });

    private static LayerDefinition Definition(
      string id,
      string displayName,
      System.Collections.Generic.IReadOnlyList<DomeLayerParam> parameters,
      System.Func<
        System.Collections.Immutable.ImmutableDictionary<
          string, ParameterValue>,
        ILayerRendererOptions> compileOptions,
      LayerActionDefinition fireAction = null,
      LayerActionDefinition clearAction = null
    ) => new LayerDefinition(
      id, displayName, null, parameters, compileOptions,
      fireAction, clearAction);
  }
}
