using System;
using System.Collections.Generic;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Visualizers;

namespace Spectrum {

  // The application composition root binds Windows/runtime input dependencies
  // and concrete renderers to the platform-neutral built-in layer manifest.
  internal static class DomeLayerCatalog {
    private sealed record Dependencies(
      ConfigurationDomeLayerEnvironment Environment,
      IAudioLevelInput Audio,
      OrientationInput Orientation,
      OrientationCenter OrientationCenter,
      BeatBroadcaster Beats,
      DomeRenderContext RenderContext
    );

    public static LayerCatalog Metadata =>
      BuiltInDomeLayerCatalog.Metadata;

    public static LayerCatalog Create(
      ConfigurationDomeLayerEnvironment environment,
      IAudioLevelInput audio,
      OrientationInput orientation,
      OrientationCenter orientationCenter,
      BeatBroadcaster beats,
      LEDDomeOutput dome
    ) {
      var d = new Dependencies(
        environment, audio, orientation, orientationCenter, beats, dome);
      var factories = new Dictionary<
        string, Func<LayerRendererRuntime, ILayerRenderer>> {
        ["volume"] = r => new LEDDomeVolumeVisualizer(
          d.Environment, r, d.Audio, d.Beats, d.RenderContext),
        ["radial"] = r => new LEDDomeRadialVisualizer(
          d.Environment, r, d.Audio, d.Beats, d.RenderContext),
        ["race"] = r => new LEDDomeRaceVisualizer(
          r, d.Audio, d.Beats, d.RenderContext),
        ["snakes"] = r => new LEDDomeSnakesVisualizer(
          r, d.RenderContext),
        ["splat"] = r => new LEDDomeSplatVisualizer(
          r, d.Audio, d.Beats, d.RenderContext),
        ["quaternion-paintbrush"] = r =>
          new LEDDomeQuaternionPaintbrushVisualizer(
            d.Environment, r, d.Audio, d.Orientation, d.OrientationCenter,
            d.Beats, d.RenderContext),
        ["tv-static"] = r => new LEDDomeTVStaticVisualizer(
          d.Environment, d.RenderContext),
        ["twinkle"] = r => new LEDDomeTwinkleVisualizer(
          d.Environment, r, d.RenderContext),
        ["flash"] = r => new LEDDomeFlashVisualizer(
          d.Environment, r, d.Audio, d.Orientation, d.Beats,
          d.RenderContext),
        ["background"] = r => new LEDDomeBackgroundVisualizer(
          r, d.RenderContext),
        ["earth"] = r => new LEDDomeEarthVisualizer(
          r, d.Orientation, d.OrientationCenter, d.RenderContext),
        ["astronomy"] = r => new LEDDomeAstronomyVisualizer(
          d.Environment, r, d.RenderContext),
        ["wave"] = r => new LEDDomeWaveVisualizer(
          d.Environment, r, d.Orientation, d.RenderContext),
        ["ripple"] = r => new LEDDomeRippleVisualizer(
          d.Environment, r, d.Audio, d.Orientation, d.OrientationCenter,
          d.Beats, d.RenderContext),
        ["stamp"] = r => new LEDDomeStampVisualizer(
          d.Environment, r, d.Audio, d.Orientation, d.OrientationCenter,
          d.Beats, d.RenderContext),
        ["tunnel"] = r => new LEDDomeTunnelVisualizer(
          d.Environment, r, d.Orientation, d.OrientationCenter,
          d.RenderContext),
        ["metaball"] = r => new LEDDomeMetaballVisualizer(
          d.Environment, r, d.Audio, d.Orientation, d.OrientationCenter,
          d.RenderContext),
        ["magnetic-field"] = r => new LEDDomeMagneticFieldVisualizer(
          r, d.Orientation, d.OrientationCenter, d.RenderContext),
        ["point-cloud"] = r => new LEDDomePointCloudVisualizer(
          d.Environment, r, d.Orientation, d.OrientationCenter,
          d.RenderContext),
        ["gyroscope"] = r => new LEDDomeGyroscopeVisualizer(
          d.Environment, r, d.Orientation, d.OrientationCenter,
          d.RenderContext),
        ["watchful-iris"] = r => new LEDDomeWatchfulIrisVisualizer(
          d.Environment, r, d.Audio, d.Orientation, d.OrientationCenter,
          d.Beats, d.RenderContext),
        ["shooting-star"] = r => new LEDDomeShootingStarVisualizer(
          d.Environment, r, d.Audio, d.Orientation, d.OrientationCenter,
          d.Beats, d.RenderContext),
        ["sparkler"] = r => new LEDDomeSparklerVisualizer(
          d.Environment, r, d.Audio, d.Orientation, d.OrientationCenter,
          d.Beats, d.RenderContext),
        ["noise-cloud"] = r => new LEDDomeNoiseCloudVisualizer(
          r, d.RenderContext),
        ["caustics"] = r => new LEDDomeCausticsVisualizer(
          r, d.RenderContext),
        ["ripple-tank"] = r => new LEDDomeRippleTankVisualizer(
          d.Environment, r, d.Orientation, d.OrientationCenter,
          d.RenderContext),
        ["vortex"] = r => new LEDDomeVortexVisualizer(
          d.Environment, r, d.Audio, d.Beats, d.RenderContext),
        ["living-skin"] = r => new LEDDomeLivingSkinVisualizer(
          d.Environment, r, d.Orientation, d.Beats, d.RenderContext),
        ["arc-lightning"] = r => new LEDDomeArcLightningVisualizer(
          d.Environment, r, d.Audio, d.Orientation, d.Beats,
          d.RenderContext),
        ["glass-mosaic"] = r => new LEDDomeGlassMosaicVisualizer(
          d.Environment, r, d.Audio, d.Orientation, d.Beats,
          d.RenderContext),
        ["cellular-dome"] = r => new LEDDomeCellularDomeVisualizer(
          d.Environment, r, d.Orientation, d.Beats, d.RenderContext),
        ["firefly-swarm"] = r => new LEDDomeFireflySwarmVisualizer(
          r, d.Audio, d.Orientation, d.RenderContext),
        ["rain-chamber"] = r => new LEDDomeRainChamberVisualizer(
          r, d.Audio, d.Orientation, d.RenderContext),
        ["topographic-dream"] = r =>
          new LEDDomeTopographicDreamVisualizer(
            r, d.Audio, d.Orientation, d.OrientationCenter,
            d.RenderContext),
        ["orbital-garden"] = r => new LEDDomeOrbitalGardenVisualizer(
          r, d.Orientation, d.RenderContext),
        ["lava-lamp-sky"] = r => new LEDDomeLavaLampSkyVisualizer(
          r, d.Audio, d.Orientation, d.OrientationCenter,
          d.RenderContext),
      };
      return Metadata.BindFactories(factories);
    }
  }
}
