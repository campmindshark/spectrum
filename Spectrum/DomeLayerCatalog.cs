using System;
using System.Collections.Generic;
using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.MIDI;
using Spectrum.Visualizers;

namespace Spectrum {

  // The host composition point for built-in layer renderers. LayerCatalog owns
  // the stable descriptors; this binds every descriptor to the concrete runtime
  // dependencies in one exhaustively validated operation.
  internal static class DomeLayerCatalog {
    public static LayerCatalog Create(
      ConfigurationDomeLayerEnvironment environment,
      AudioInput audio,
      MidiInput midi,
      OrientationInput orientation,
      OrientationCenter orientationCenter,
      BeatBroadcaster beats,
      LEDDomeOutput dome
    ) => LayerCatalog.Default.BindFactories(new Dictionary<
      string, Func<LayerRendererRuntime, ILayerRenderer>>(
        StringComparer.Ordinal) {
      ["volume"] = runtime => new LEDDomeVolumeVisualizer(
        environment, runtime, audio, beats, dome),
      ["radial"] = runtime => new LEDDomeRadialVisualizer(
        environment, runtime, audio, beats, dome),
      ["splat"] = runtime => new LEDDomeSplatVisualizer(
        runtime, audio, beats, dome),
      ["quaternion-test"] = runtime => new LEDDomeQuaternionTestVisualizer(
        environment, orientation, dome),
      ["quaternion-paintbrush"] = runtime =>
        new LEDDomeQuaternionPaintbrushVisualizer(
          environment, runtime, audio, orientation, orientationCenter,
          beats, dome),
      ["race"] = runtime => new LEDDomeRaceVisualizer(
        runtime, audio, midi, beats, dome),
      ["snakes"] = runtime => new LEDDomeSnakesVisualizer(runtime, dome),
      ["tv-static"] = runtime => new LEDDomeTVStaticVisualizer(
        environment, dome),
      ["twinkle"] = runtime => new LEDDomeTwinkleVisualizer(
        environment, runtime, dome),
      ["background"] = runtime => new LEDDomeBackgroundVisualizer(
        runtime, dome),
      ["noise-cloud"] = runtime => new LEDDomeNoiseCloudVisualizer(
        runtime, dome),
      ["vortex"] = runtime => new LEDDomeVortexVisualizer(
        environment, runtime, dome),
      ["caustics"] = runtime => new LEDDomeCausticsVisualizer(
        runtime, dome),
      ["ripple-tank"] = runtime => new LEDDomeRippleTankVisualizer(
        environment, runtime, orientation, orientationCenter, dome),
      ["flash"] = runtime => new LEDDomeFlashVisualizer(
        environment, runtime, audio, orientation, beats, dome),
      ["wave"] = runtime => new LEDDomeWaveVisualizer(
        environment, runtime, orientation, dome),
      ["gyroscope"] = runtime => new LEDDomeGyroscopeVisualizer(
        environment, runtime, orientation, orientationCenter, dome),
      ["ripple"] = runtime => new LEDDomeRippleVisualizer(
        environment, runtime, audio, orientation, orientationCenter,
        beats, dome),
      ["stamp"] = runtime => new LEDDomeStampVisualizer(
        environment, runtime, audio, orientation, orientationCenter,
        beats, dome),
      ["metaball"] = runtime => new LEDDomeMetaballVisualizer(
        environment, runtime, audio, orientation, orientationCenter, dome),
      ["point-cloud"] = runtime => new LEDDomePointCloudVisualizer(
        environment, runtime, orientation, dome),
      ["shooting-star"] = runtime => new LEDDomeShootingStarVisualizer(
        environment, runtime, audio, orientation, orientationCenter,
        beats, dome),
      ["sparkler"] = runtime => new LEDDomeSparklerVisualizer(
        environment, runtime, audio, orientation, orientationCenter,
        beats, dome),
    });
  }
}
