using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.MIDI;
using Spectrum.Visualizers;

namespace Spectrum {

  // One application-level feature manifest. A layer's stable descriptor,
  // parameter schema, typed options compiler, actions, and concrete factory are
  // declared in the same registration below. Base owns only the contracts.
  internal static class DomeLayerCatalog {
    private sealed record Dependencies(
      ConfigurationDomeLayerEnvironment Environment,
      AudioInput Audio,
      MidiInput Midi,
      OrientationInput Orientation,
      OrientationCenter OrientationCenter,
      BeatBroadcaster Beats,
      DomeRenderContext RenderContext
    );

    private sealed record Feature(
      string Id,
      string DisplayName,
      IReadOnlyList<DomeLayerParam> Parameters,
      Func<ImmutableDictionary<string, ParameterValue>,
        ILayerRendererOptions> CompileOptions,
      Func<Dependencies, LayerRendererRuntime, ILayerRenderer> CreateRenderer,
      LayerActionDefinition FireAction = null,
      LayerActionDefinition ClearAction = null
    ) {
      public LayerDefinition Bind(Dependencies dependencies) =>
        new LayerDefinition(
          this.Id,
          this.DisplayName,
          dependencies == null
            ? null
            : runtime => this.CreateRenderer(dependencies, runtime),
          this.Parameters,
          this.CompileOptions,
          this.FireAction,
          this.ClearAction);
    }

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

    // Stable ordering is part of both picker contracts.
    private static readonly ImmutableArray<Feature> Features =
      ImmutableArray.Create(
        new Feature(
          "volume", "Volume (OG)", LayerParameterSchemas.VolumeParams,
          LayerRendererOptionsCompiler.Volume,
          (d, r) => new LEDDomeVolumeVisualizer(
            d.Environment, r, d.Audio, d.Beats, d.RenderContext)),
        new Feature(
          "radial", "Radial Effects", LayerParameterSchemas.RadialParams,
          LayerRendererOptionsCompiler.Radial,
          (d, r) => new LEDDomeRadialVisualizer(
            d.Environment, r, d.Audio, d.Beats, d.RenderContext)),
        new Feature(
          "race", "Race", LayerParameterSchemas.RaceParams,
          LayerRendererOptionsCompiler.Race,
          (d, r) => new LEDDomeRaceVisualizer(
            r, d.Audio, d.Midi, d.Beats, d.RenderContext)),
        new Feature(
          "snakes", "Snakes", LayerParameterSchemas.SnakesParams,
          LayerRendererOptionsCompiler.Palette,
          (d, r) => new LEDDomeSnakesVisualizer(r, d.RenderContext)),
        new Feature(
          "splat", "Splat Effect", LayerParameterSchemas.SplatParams,
          LayerRendererOptionsCompiler.Palette,
          (d, r) => new LEDDomeSplatVisualizer(
            r, d.Audio, d.Beats, d.RenderContext)),
        new Feature(
          "quaternion-paintbrush", "Quaternion Paintbrush",
          LayerParameterSchemas.PaintbrushParams,
          LayerRendererOptionsCompiler.Paintbrush,
          (d, r) => new LEDDomeQuaternionPaintbrushVisualizer(
            d.Environment, r, d.Audio, d.Orientation, d.OrientationCenter,
            d.Beats, d.RenderContext)),
        new Feature(
          "tv-static", "TV Static", LayerParameterSchemas.NoParams,
          LayerRendererOptionsCompiler.Empty,
          (d, r) => new LEDDomeTVStaticVisualizer(
            d.Environment, d.RenderContext)),
        new Feature(
          "twinkle", "Twinkle", LayerParameterSchemas.TwinkleParams,
          LayerRendererOptionsCompiler.Twinkle,
          (d, r) => new LEDDomeTwinkleVisualizer(
            d.Environment, r, d.RenderContext)),
        new Feature(
          "flash", "Flash", LayerParameterSchemas.FlashParams,
          LayerRendererOptionsCompiler.Flash,
          (d, r) => new LEDDomeFlashVisualizer(
            d.Environment, r, d.Audio, d.Orientation, d.Beats,
            d.RenderContext),
          FireAction),
        new Feature(
          "background", "Background", LayerParameterSchemas.BackgroundParams,
          LayerRendererOptionsCompiler.Background,
          (d, r) => new LEDDomeBackgroundVisualizer(r, d.RenderContext)),
        new Feature(
          "earth", "Earth", LayerParameterSchemas.EarthParams,
          LayerRendererOptionsCompiler.Earth,
          (d, r) => new LEDDomeEarthVisualizer(
            r, d.Orientation, d.OrientationCenter, d.RenderContext)),
        new Feature(
          "astronomy", "Astronomy", LayerParameterSchemas.AstronomyParams,
          LayerRendererOptionsCompiler.Astronomy,
          (d, r) => new LEDDomeAstronomyVisualizer(
            d.Environment, r, d.RenderContext),
          PlayAction, StopAction),
        new Feature(
          "wave", "Wave", LayerParameterSchemas.WaveParams,
          LayerRendererOptionsCompiler.Wave,
          (d, r) => new LEDDomeWaveVisualizer(
            d.Environment, r, d.Orientation, d.RenderContext),
          FireAction),
        new Feature(
          "ripple", "Ripple", LayerParameterSchemas.RippleParams,
          LayerRendererOptionsCompiler.Ripple,
          (d, r) => new LEDDomeRippleVisualizer(
            d.Environment, r, d.Audio, d.Orientation, d.OrientationCenter,
            d.Beats, d.RenderContext),
          FireAction),
        new Feature(
          "stamp", "Stamp", LayerParameterSchemas.StampParams,
          LayerRendererOptionsCompiler.Stamp,
          (d, r) => new LEDDomeStampVisualizer(
            d.Environment, r, d.Audio, d.Orientation, d.OrientationCenter,
            d.Beats, d.RenderContext),
          FireAction),
        new Feature(
          "tunnel", "Tunnel", LayerParameterSchemas.TunnelParams,
          LayerRendererOptionsCompiler.Tunnel,
          (d, r) => new LEDDomeTunnelVisualizer(
            d.Environment, r, d.Orientation, d.OrientationCenter,
            d.RenderContext)),
        new Feature(
          "metaball", "Metaball", LayerParameterSchemas.MetaballParams,
          LayerRendererOptionsCompiler.Metaball,
          (d, r) => new LEDDomeMetaballVisualizer(
            d.Environment, r, d.Audio, d.Orientation, d.OrientationCenter,
            d.RenderContext),
          FireAction),
        new Feature(
          "magnetic-field", "Magnetic Field",
          LayerParameterSchemas.MagneticFieldParams,
          LayerRendererOptionsCompiler.MagneticField,
          (d, r) => new LEDDomeMagneticFieldVisualizer(
            r, d.Orientation, d.OrientationCenter, d.RenderContext)),
        new Feature(
          "point-cloud", "Point Cloud", LayerParameterSchemas.PointCloudParams,
          LayerRendererOptionsCompiler.PointCloud,
          (d, r) => new LEDDomePointCloudVisualizer(
            d.Environment, r, d.Orientation, d.OrientationCenter,
            d.RenderContext)),
        new Feature(
          "gyroscope", "Gyroscope", LayerParameterSchemas.GyroscopeParams,
          LayerRendererOptionsCompiler.Gyroscope,
          (d, r) => new LEDDomeGyroscopeVisualizer(
            d.Environment, r, d.Orientation, d.OrientationCenter,
            d.RenderContext)),
        new Feature(
          "watchful-iris", "Watchful Iris",
          LayerParameterSchemas.WatchfulIrisParams,
          LayerRendererOptionsCompiler.WatchfulIris,
          (d, r) => new LEDDomeWatchfulIrisVisualizer(
            d.Environment, r, d.Audio, d.Orientation, d.OrientationCenter,
            d.Beats, d.RenderContext),
          BlinkAction),
        new Feature(
          "shooting-star", "Shooting Star",
          LayerParameterSchemas.ShootingStarParams,
          LayerRendererOptionsCompiler.ShootingStar,
          (d, r) => new LEDDomeShootingStarVisualizer(
            d.Environment, r, d.Audio, d.Orientation, d.OrientationCenter,
            d.Beats, d.RenderContext),
          FireAction, ClearAction),
        new Feature(
          "sparkler", "Sparkler", LayerParameterSchemas.SparklerParams,
          LayerRendererOptionsCompiler.Sparkler,
          (d, r) => new LEDDomeSparklerVisualizer(
            d.Environment, r, d.Audio, d.Orientation, d.OrientationCenter,
            d.Beats, d.RenderContext),
          FireAction, ClearAction),
        new Feature(
          "noise-cloud", "Noise Cloud", LayerParameterSchemas.NoiseCloudParams,
          LayerRendererOptionsCompiler.NoiseCloud,
          (d, r) => new LEDDomeNoiseCloudVisualizer(r, d.RenderContext)),
        new Feature(
          "caustics", "Caustics", LayerParameterSchemas.CausticsParams,
          LayerRendererOptionsCompiler.Caustics,
          (d, r) => new LEDDomeCausticsVisualizer(r, d.RenderContext)),
        new Feature(
          "ripple-tank", "Ripple Tank",
          LayerParameterSchemas.RippleTankParams,
          LayerRendererOptionsCompiler.RippleTank,
          (d, r) => new LEDDomeRippleTankVisualizer(
            d.Environment, r, d.Orientation, d.OrientationCenter,
            d.RenderContext),
          ClearAction: ClearAction),
        new Feature(
          "vortex", "Vortex", LayerParameterSchemas.VortexParams,
          LayerRendererOptionsCompiler.Vortex,
          (d, r) => new LEDDomeVortexVisualizer(
            d.Environment, r, d.Audio, d.Beats, d.RenderContext)),
        new Feature(
          "living-skin", "Living Skin",
          LayerParameterSchemas.LivingSkinParams,
          LayerRendererOptionsCompiler.LivingSkin,
          (d, r) => new LEDDomeLivingSkinVisualizer(
            d.Environment, r, d.Orientation, d.Beats, d.RenderContext),
          FireAction, ClearAction),
        new Feature(
          "arc-lightning", "Arc Lightning",
          LayerParameterSchemas.ArcLightningParams,
          LayerRendererOptionsCompiler.ArcLightning,
          (d, r) => new LEDDomeArcLightningVisualizer(
            d.Environment, r, d.Audio, d.Orientation, d.Beats,
            d.RenderContext),
          FireAction, ClearAction),
        new Feature(
          "glass-mosaic", "Glass Mosaic",
          LayerParameterSchemas.GlassMosaicParams,
          LayerRendererOptionsCompiler.GlassMosaic,
          (d, r) => new LEDDomeGlassMosaicVisualizer(
            d.Environment, r, d.Audio, d.Orientation, d.Beats,
            d.RenderContext),
          FireAction, ClearAction),
        new Feature(
          "cellular-dome", "Cellular Dome",
          LayerParameterSchemas.CellularDomeParams,
          LayerRendererOptionsCompiler.CellularDome,
          (d, r) => new LEDDomeCellularDomeVisualizer(
            d.Environment, r, d.Orientation, d.Beats, d.RenderContext),
          FireAction, ClearAction),
        new Feature(
          "firefly-swarm", "Firefly Swarm",
          LayerParameterSchemas.FireflySwarmParams,
          LayerRendererOptionsCompiler.FireflySwarm,
          (d, r) => new LEDDomeFireflySwarmVisualizer(
            r, d.Audio, d.Orientation, d.RenderContext)),
        new Feature(
          "rain-chamber", "Rain Chamber",
          LayerParameterSchemas.RainChamberParams,
          LayerRendererOptionsCompiler.RainChamber,
          (d, r) => new LEDDomeRainChamberVisualizer(
            r, d.Audio, d.Orientation, d.RenderContext)),
        new Feature(
          "topographic-dream", "Topographic Dream",
          LayerParameterSchemas.TopographicDreamParams,
          LayerRendererOptionsCompiler.TopographicDream,
          (d, r) => new LEDDomeTopographicDreamVisualizer(
            r, d.Audio, d.Orientation, d.OrientationCenter,
            d.RenderContext)),
        new Feature(
          "orbital-garden", "Orbital Garden",
          LayerParameterSchemas.OrbitalGardenParams,
          LayerRendererOptionsCompiler.OrbitalGarden,
          (d, r) => new LEDDomeOrbitalGardenVisualizer(
            r, d.Orientation, d.RenderContext)),
        new Feature(
          "lava-lamp-sky", "Lava Lamp Sky",
          LayerParameterSchemas.LavaLampSkyParams,
          LayerRendererOptionsCompiler.LavaLampSky,
          (d, r) => new LEDDomeLavaLampSkyVisualizer(
            r, d.Audio, d.Orientation, d.OrientationCenter,
            d.RenderContext)));

    public static LayerCatalog Metadata { get; } =
      new LayerCatalog(Features.Select(feature => feature.Bind(null)));

    public static LayerCatalog Create(
      ConfigurationDomeLayerEnvironment environment,
      AudioInput audio,
      MidiInput midi,
      OrientationInput orientation,
      OrientationCenter orientationCenter,
      BeatBroadcaster beats,
      LEDDomeOutput dome
    ) {
      var dependencies = new Dependencies(
        environment, audio, midi, orientation, orientationCenter, beats, dome);
      return new LayerCatalog(
        Features.Select(feature => feature.Bind(dependencies)));
    }
  }
}
