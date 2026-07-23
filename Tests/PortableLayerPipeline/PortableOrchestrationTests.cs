using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Visualizers;
using static Spectrum.LayerPipeline.Tests.LayerPipelineTestFixtures;
using static Spectrum.LayerPipeline.Tests.TestAssertions;

namespace Spectrum.LayerPipeline.Tests {

  public static class PortableOrchestrationTests {
    public static void Register(Action<string, Action> run) {
      run(nameof(BuiltInFeaturesLiveInPortableCore), BuiltInFeaturesLiveInPortableCore);
      run(nameof(AstronomyOptionsAndHeading), AstronomyOptionsAndHeading);
      run(nameof(ControlStormAvoidsPlanWork), ControlStormAvoidsPlanWork);
      run(nameof(MidiBindingsPublishStateCommands), MidiBindingsPublishStateCommands);
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
      string radialOptionsName = typeof(RadialLayerOptions).FullName ??
        throw new InvalidOperationException(
          "the radial options type has no full name");
      Assert(catalogType.Assembly.GetType(radialOptionsName) == null,
        "Base still contains built-in renderer option types");
      Assert(DomeLayerCatalog.Metadata.Definitions.Count > 0 &&
          DomeLayerCatalog.Metadata.Definitions.All(
            definition => definition.CreateRenderer == null),
        "the metadata catalog unexpectedly owns runtime factories");
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
      LEDDomeAstronomyVisualizer? playbackVisualizer = null;
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
      string playbackInstanceId = playbackLayer.InstanceId ??
        throw new InvalidOperationException(
          "the astronomy playback layer has no instance ID");
      playbackConfig.ReplaceDomeLayerFireCounters(
        new Dictionary<string, int> {
        [playbackInstanceId] = 1,
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
          [layers[i % layers.Count].InstanceId ??
            throw new InvalidOperationException("layer has no instance ID")] =
              i + 1,
        });
        config.ReplaceDomeLayerClearCounters(new Dictionary<string, int> {
          [layers[(i + 1) % layers.Count].InstanceId ??
            throw new InvalidOperationException("layer has no instance ID")] =
              i + 1,
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
        layer => new LayerInstanceId(
          layer.InstanceId ?? throw new InvalidOperationException(
            "layer has no instance ID"))).ToArray();
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
      Assert(binding.callback != null,
        "the MIDI binding did not compile a callback");

      BindingInvocation invocation = RunOnDedicatedThread(
        () => binding.callback(7, 0.6));
      Assert(Math.Abs(config.domeBrightness - 0.1) < 0.000001 &&
          dispatcher.PendingCount == 1,
        "a MIDI binding assigned configuration on its callback thread; " +
        "brightness=" + config.domeBrightness +
        ", pending=" + dispatcher.PendingCount);
      dispatcher.Drain();
      invocation.Completion?.GetAwaiter().GetResult();
      Assert(Math.Abs(config.domeBrightness - 0.6) < 0.000001,
        "the queued MIDI state command was not applied");
    }


  }
}
