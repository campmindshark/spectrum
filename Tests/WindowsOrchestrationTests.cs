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
using Spectrum.MIDI;
using Spectrum.Visualizers;
using static Spectrum.LayerPipeline.Tests.LayerPipelineTestFixtures;
using static Spectrum.LayerPipeline.Tests.TestAssertions;

namespace Spectrum.LayerPipeline.Tests {

  public static class WindowsOrchestrationTests {
    public static void Register(Action<string, Action> run) {
      run(nameof(BuiltInFeaturesLiveInPortableCore), BuiltInFeaturesLiveInPortableCore);
      run(nameof(AstronomyOptionsAndHeading), AstronomyOptionsAndHeading);
      run(nameof(AstronomyPlaybackDisplayUpdatesStayTransient), AstronomyPlaybackDisplayUpdatesStayTransient);
      run(nameof(ControlStormAvoidsPlanWork), ControlStormAvoidsPlanWork);
      run(nameof(EnabledOperatorConcurrentSettingsAreIsolated), EnabledOperatorConcurrentSettingsAreIsolated);
      run(nameof(MidiBindingsPublishStateCommands), MidiBindingsPublishStateCommands);
      run(nameof(MidiBindingFailuresAreContained), MidiBindingFailuresAreContained);
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
      Type applicationType = typeof(global::Spectrum.MainWindow);
      Assert(coreType.Assembly != applicationType.Assembly,
        "portable runtime still compiles into the Windows application");
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
      var astronomyRow = new global::Spectrum.DomeLayerRowViewModel {
        VisualizerKey = "astronomy",
      };
      Assert(astronomyRow.FireLabel == "Play" &&
          astronomyRow.ClearLabel == "Stop" &&
          astronomyRow.HasFireAction && astronomyRow.HasClearAction,
        "astronomy layer actions were not labeled Play and Stop");
      astronomyRow.VisualizerKey = "earth";
      Assert(!astronomyRow.HasFireAction && !astronomyRow.HasClearAction &&
          astronomyRow.FireLabel == null && astronomyRow.ClearLabel == null,
        "non-triggerable layer retained action controls");
      astronomyRow.VisualizerKey = "shooting-star";
      Assert(astronomyRow.HasFireAction && astronomyRow.HasClearAction &&
          astronomyRow.FireLabel == "Fire" &&
          astronomyRow.ClearLabel == "Clear",
        "triggerable layer actions were not exposed");
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

    private static void AstronomyPlaybackDisplayUpdatesStayTransient() {
      var descriptor = new DomeLayerParam {
        Key = "timeOffsetHours",
        Label = "Time (hours from start)",
        Type = DomeLayerParamType.Double,
        Min = 0,
        Max = 168,
        Step = 1,
        Default = 0,
      };
      var param = new global::Spectrum.LayerParamViewModel(
        descriptor, 12, false);
      int edits = 0;
      bool valueChanged = false;
      param.Changed += () => edits++;
      param.PropertyChanged += (sender, e) => {
        if (e.PropertyName == nameof(param.Value)) {
          valueChanged = true;
        }
      };

      param.SetDisplayedValue(13.5);
      Assert(param.Value == 13.5 && param.StoredValue == 12 &&
          edits == 0 && valueChanged,
        "astronomy playback display persisted a timer tick");

      param.Value = 14;
      Assert(param.Value == 14 && param.StoredValue == 14 && edits == 1,
        "astronomy time slider edit was not persisted");
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

    private static void EnabledOperatorConcurrentSettingsAreIsolated() {
      var layers = new List<DomeLayerSettings>();
      for (int i = 0; i < StackValidator.MaxLayers; i++) {
        layers.Add(Layer("background", "enabled-storm-" + i));
      }
      var config = new global::Spectrum.SpectrumConfiguration();
      config.ReplaceDomeLayerStack(layers);
      config.domeSimulationEnabled = true;
      config.midiInputEnabled = true;
      config.ReplaceMidiPresets(new Dictionary<int, MidiPreset> {
        [9] = ConcurrentTestMidiPreset(9),
        [10] = ConcurrentTestMidiPreset(10),
      });
      config.ReplaceMidiDevices(new Dictionary<int, int> { [42] = 9 });

      var dispatcher = new QueuedStateDispatcher();
      config.AttachMutationDispatcher(dispatcher);
      var controller = new global::Spectrum.Web.LayersController(
        dispatcher, config);
      var runtime = new global::Spectrum.Operator(
        config, dispatcher,
        new DisconnectedWindowsMidiInputFactory(),
        connectHardware: false);
      var runtimeMidi = (MidiInput)runtime.MidiInput;
      var settings = (IRuntimeSettingsConfiguration)config;
      long transportGeneration =
        settings.DomeOutputSettingsSnapshot.TransportGeneration;
      Exception? readerFailure = null;
      using var stopReader = new CancellationTokenSource();
      using var firstFpsPublished = new ManualResetEventSlim();
      runtime.Telemetry.PropertyChanged += (_, change) => {
        if (change.PropertyName == nameof(RuntimeTelemetry.OperatorFPS) &&
            runtime.Telemetry.OperatorFPS > 0) {
          firstFpsPublished.Set();
        }
      };

      Task reader = Task.Run(() => {
        try {
          while (!stopReader.IsCancellationRequested) {
            DomeRuntimeFrameSnapshot frame =
              settings.DomeRuntimeFrameSnapshot;
            Assert(frame.FireGenerations.All(pair =>
                pair.Key.StartsWith("enabled-storm-") && pair.Value > 0),
              "a reader observed an invalid fire-counter generation");

            AudioSettingsSnapshot audio = settings.AudioSettingsSnapshot;
            Assert(audio.DeviceId == null || audio.DeviceId == "fake-a" ||
                audio.DeviceId == "fake-b",
              "a reader observed a torn Audio generation");

            MidiSettingsSnapshot midi = settings.MidiSettingsSnapshot;
            Assert(midi.Devices.Count == 1 &&
                midi.Devices.TryGetValue(42, out int preset) &&
                (preset == 9 || preset == 10),
              "a reader observed a torn MIDI device generation");

            DomeOutputSettingsSnapshot output =
              settings.DomeOutputSettingsSnapshot;
            Assert(IsIdentityOrReverse(output.CableMapping),
              "a reader observed a torn cable-mapping generation");
            foreach (ImmutableArray<int> ports in output.PortMappings) {
              Assert(IsIdentityOrReverse(ports),
                "a reader observed a torn port-mapping generation");
            }

            ImmutableArray<DomeLayerView> stack = config.domeLayerStack;
            Assert(stack.Length == StackValidator.MaxLayers &&
                stack.All(layer => layer.RendererParams != null &&
                  layer.OperationParams != null),
              "a reader observed a partial immutable layer view");
          }
        } catch (Exception error) {
          readerFailure = error;
        }
      });

      runtime.Enabled = true;
      int reconciliations = runtime.LayerPlanReconciliationCount;
      RenderPlan acceptedPlan = runtime.DomeOutput.RenderPlan;
      try {
        Task webUpdates = Task.Run(async () => {
          for (int i = 0; i < 120; i++) {
            string instanceId = layers[i % layers.Count].InstanceId ??
              throw new InvalidOperationException(
                "layer has no instance ID");
            (bool ok, string? error) = await controller.FireAsync(instanceId);
            if (!ok) {
              throw new InvalidOperationException(error);
            }
          }
        });
        Task deviceUpdates = Task.Run(async () => {
          for (int i = 0; i < 80; i++) {
            int generation = i;
            await dispatcher.InvokeAsync(() => {
              config.audioDeviceID = (generation & 1) == 0
                ? "fake-a" : "fake-b";
              config.ReplaceMidiDevices(new Dictionary<int, int> {
                [42] = (generation & 1) == 0 ? 9 : 10,
              });
              int[] mapping = Enumerable.Range(
                  0, LEDDomeOutput.NumCables).ToArray();
              if ((generation & 1) != 0) {
                Array.Reverse(mapping);
              }
              config.ReplaceDomeCableMapping(mapping);

              int[] ports = Enumerable.Range(
                  0, LEDDomeOutput.NumPortsPerBox).ToArray();
              if ((generation & 1) != 0) {
                Array.Reverse(ports);
              }
              config.ReplaceDomePortMappings(Enumerable.Range(
                0, LEDDomeOutput.NumDomeBoxes).Select(
                  _ => new DomePortMapping(ports)).ToArray());
            });
          }
        });
        Task inputUpdates = Task.Run(async () => {
          for (int i = 1; i <= 120; i++) {
            byte[] datagram = new byte[15];
            datagram[0] = 7;
            Array.Copy(BitConverter.GetBytes(i), 0, datagram, 1, 4);
            datagram[5] = 3;
            datagram[7] = 0x40;
            runtime.OrientationInput.ProcessDatagram(datagram);
            await runtime.MidiInput.DispatchBindingsAsync(new MidiCommand {
              deviceIndex = 42,
              type = MidiCommandType.Knob,
              index = 7,
              value = (i % 101) / 100.0,
            });
          }
        });

        Task updates = Task.WhenAll(
          webUpdates, deviceUpdates, inputUpdates);
        var spin = new SpinWait();
        while (!updates.IsCompleted) {
          dispatcher.Drain();
          spin.SpinOnce();
        }
        dispatcher.Drain();
        updates.GetAwaiter().GetResult();

        stopReader.Cancel();
        reader.GetAwaiter().GetResult();
        Assert(readerFailure == null,
          "concurrent reader failed: " + readerFailure);
        long expectedMidiGeneration =
          settings.MidiSettingsSnapshot.DeviceGeneration;
        long expectedMappingGeneration =
          settings.DomeOutputSettingsSnapshot.MappingGeneration;
        using var midiSettingsApplied = new ManualResetEventSlim();
        using var outputSettingsApplied = new ManualResetEventSlim();
        void ObserveMidiSettings() {
          if (runtime.MidiInput.AppliedDeviceGeneration ==
              expectedMidiGeneration) {
            midiSettingsApplied.Set();
          }
        }
        void ObserveOutputSettings() {
          if (runtime.DomeOutput.AppliedMappingGeneration ==
              expectedMappingGeneration) {
            outputSettingsApplied.Set();
          }
        }
        runtimeMidi.SettingsApplied += ObserveMidiSettings;
        runtime.DomeOutput.OutputSettingsApplied += ObserveOutputSettings;
        ObserveMidiSettings();
        ObserveOutputSettings();
        Assert(midiSettingsApplied.Wait(TimeSpan.FromSeconds(3)) &&
            outputSettingsApplied.Wait(TimeSpan.FromSeconds(3)),
          "the enabled operator did not reconcile the latest device generations");
        runtimeMidi.SettingsApplied -= ObserveMidiSettings;
        runtime.DomeOutput.OutputSettingsApplied -= ObserveOutputSettings;
        Assert(runtime.DomeOutput.AppliedTransportGeneration ==
            transportGeneration,
          "a wiring-only update reconciled the OPC transport");
        Assert(runtime.LayerPlanReconciliationCount == reconciliations,
          "control/device traffic reconciled the layer plan");
        Assert(ReferenceEquals(runtime.DomeOutput.RenderPlan, acceptedPlan),
          "control/device traffic replaced the accepted render plan");

        Assert(firstFpsPublished.Wait(TimeSpan.FromSeconds(4)),
          "the enabled operator did not complete its first FPS window");
        Task measurementCompleted = runtime.BeginAllocationMeasurement(30);
        measurementCompleted
          .WaitAsync(TimeSpan.FromSeconds(4))
          .GetAwaiter().GetResult();
        var allocation = runtime.EndAllocationMeasurement();
        Assert(allocation.Frames >= 30,
          "too few enabled operator frames were measured: " +
          allocation.Frames);
        // The CLR can charge one 64-byte thread/runtime bookkeeping object to
        // this window nondeterministically. Bound fixed noise tightly enough
        // that any recurring per-frame allocation still fails the test.
        const long maxFixedMeasurementNoise = 128;
        Assert(allocation.Bytes <= maxFixedMeasurementNoise,
          "the steady-state enabled operator exceeded fixed measurement " +
          "noise with " +
          allocation.Bytes + " managed bytes across " +
          allocation.Frames + " frames");
      } finally {
        stopReader.Cancel();
        reader.GetAwaiter().GetResult();
        runtime.Enabled = false;
      }
    }

    private static MidiPreset ConcurrentTestMidiPreset(int id) => new() {
      id = id,
      Name = "concurrent " + id,
      Bindings = new List<IMidiBindingConfig> {
        new ContinuousKnobMidiBindingConfig {
          BindingName = "concurrent brightness",
          knobIndex = 7,
          configPropertyName = nameof(Configuration.domeBrightness),
          startValue = 0,
          endValue = 1,
        },
      },
    };

    private static bool IsIdentityOrReverse(IReadOnlyList<int> values) {
      if (values == null || values.Count == 0) {
        return true;
      }
      bool identity = true;
      bool reverse = true;
      for (int i = 0; i < values.Count; i++) {
        identity &= values[i] == i;
        reverse &= values[i] == values.Count - 1 - i;
      }
      return identity || reverse;
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

    private static void MidiBindingFailuresAreContained() {
      Configuration config = new ThrowingBrightnessConfiguration();
      ConfigurationEditor editor = (ConfigurationEditor)config;
      editor.ReplaceMidiDevices(new Dictionary<int, int> { [42] = 9 });
      editor.ReplaceMidiPresets(new Dictionary<int, MidiPreset> {
        [9] = new MidiPreset {
          id = 9,
          Name = "fault containment",
          Bindings = new List<IMidiBindingConfig> {
            new ContinuousKnobMidiBindingConfig {
              BindingName = "wrong numeric type",
              knobIndex = 6,
              configPropertyName = nameof(config.domeTestPattern),
              startValue = 0,
              endValue = 1,
            },
            new ContinuousKnobMidiBindingConfig {
              BindingName = "throwing setter",
              knobIndex = 7,
              configPropertyName = nameof(config.domeBrightness),
              startValue = 0,
              endValue = 1,
            },
          },
        },
      });
      var dispatcher = new QueuedStateDispatcher();
      var midi = new MidiInput(
        config, new BeatBroadcaster(config), dispatcher);

      Task invocation = Task.Run(() => midi.DispatchBindingsAsync(
        new MidiCommand {
          deviceIndex = 42,
          type = MidiCommandType.Knob,
          index = 7,
          value = 0.5,
        }));
      Assert(dispatcher.WaitForPending(TimeSpan.FromSeconds(2)) &&
          dispatcher.PendingCount == 1,
        "the valid MIDI mutation was not queued");
      dispatcher.Drain();
      invocation.GetAwaiter().GetResult();

      MidiLogMessage[] messages = midi.MidiLog.DequeueAllMessages();
      Assert(messages.Any(message =>
          message.message != null &&
          message.message.Contains("wrong numeric type") &&
          message.message.Contains("has type Int32")),
        "an incompatible existing MIDI target was not rejected at compile time");
      Assert(messages.Any(message =>
          message.message != null &&
          message.message.Contains("throwing setter") &&
          message.message.Contains("setter exploded")),
        "a deferred MIDI setter failure was not contained in the MIDI log");
    }

    /**
     * Keeps the Windows MIDI orchestration in the Windows-only integration
     * suite without opening Sanford or NAudio hardware handles. The portable
     * runtime itself no longer constructs these adapters.
     */
    private sealed class DisconnectedWindowsMidiInputFactory :
      ISpectrumInputFactory {
      private readonly DisabledSpectrumInputFactory disabled =
        new DisabledSpectrumInputFactory();

      public IAudioLevelInput CreateAudioInput(
        Configuration config,
        BeatBroadcaster beat
      ) => this.disabled.CreateAudioInput(config, beat);

      public IMidiControlInput CreateMidiInput(
        Configuration config,
        BeatBroadcaster beat,
        ApplicationStateDispatcher stateDispatcher
      ) => new global::Spectrum.MIDI.MidiInput(
        config, beat, stateDispatcher, connectHardware: false);
    }

    private sealed class ThrowingBrightnessConfiguration :
      global::Spectrum.SpectrumConfiguration, Configuration {
      double Configuration.domeBrightness {
        get => base.domeBrightness;
        set => throw new InvalidOperationException("setter exploded");
      }
    }

  }
}
