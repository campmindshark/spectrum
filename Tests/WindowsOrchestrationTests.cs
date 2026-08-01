using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.MIDI;
using static Spectrum.LayerPipeline.Tests.LayerPipelineTestFixtures;

namespace Spectrum.LayerPipeline.Tests {

  [TestClass]
  [DoNotParallelize]
  public sealed class WindowsOrchestrationTests {

    [TestMethod]
    public void PortableCoreStaysOutsideWindowsApplication() {
      Type coreType = typeof(global::Spectrum.BuiltInDomeLayerCatalog);
      Type applicationType = typeof(global::Spectrum.MainWindow);
      Assert.IsTrue(coreType.Assembly != applicationType.Assembly,
        "portable runtime still compiles into the Windows application");
    }

    [TestMethod]
    public void AstronomyLayerActionsUseWindowsLabels() {
      var astronomyRow = new global::Spectrum.DomeLayerRowViewModel {
        VisualizerKey = "astronomy",
      };
      Assert.IsTrue(astronomyRow.FireLabel == "Play" &&
          astronomyRow.ClearLabel == "Stop" &&
          astronomyRow.HasFireAction && astronomyRow.HasClearAction,
        "astronomy layer actions were not labeled Play and Stop");
      astronomyRow.VisualizerKey = "earth";
      Assert.IsTrue(!astronomyRow.HasFireAction && !astronomyRow.HasClearAction &&
          astronomyRow.FireLabel == null && astronomyRow.ClearLabel == null,
        "non-triggerable layer retained action controls");
      astronomyRow.VisualizerKey = "shooting-star";
      Assert.IsTrue(astronomyRow.HasFireAction && astronomyRow.HasClearAction &&
          astronomyRow.FireLabel == "Fire" &&
          astronomyRow.ClearLabel == "Clear",
        "triggerable layer actions were not exposed");
    }

    [TestMethod]
    public void AstronomyPlaybackDisplayUpdatesStayTransient() {
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
      Assert.IsTrue(param.Value == 13.5 && param.StoredValue == 12 &&
          edits == 0 && valueChanged,
        "astronomy playback display persisted a timer tick");

      param.Value = 14;
      Assert.IsTrue(param.Value == 14 && param.StoredValue == 14 && edits == 1,
        "astronomy time slider edit was not persisted");
    }

    [TestMethod]
    public void EnabledOperatorConcurrentSettingsAreIsolated() {
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
            Assert.IsTrue(frame.FireGenerations.All(pair =>
                pair.Key.StartsWith("enabled-storm-") && pair.Value > 0),
              "a reader observed an invalid fire-counter generation");

            AudioSettingsSnapshot audio = settings.AudioSettingsSnapshot;
            Assert.IsTrue(audio.DeviceId == null || audio.DeviceId == "fake-a" ||
                audio.DeviceId == "fake-b",
              "a reader observed a torn Audio generation");

            MidiSettingsSnapshot midi = settings.MidiSettingsSnapshot;
            Assert.IsTrue(midi.Devices.Count == 1 &&
                midi.Devices.TryGetValue(42, out int preset) &&
                (preset == 9 || preset == 10),
              "a reader observed a torn MIDI device generation");

            DomeOutputSettingsSnapshot output =
              settings.DomeOutputSettingsSnapshot;
            Assert.IsTrue(IsIdentityOrReverse(output.CableMapping),
              "a reader observed a torn cable-mapping generation");
            foreach (ImmutableArray<int> ports in output.PortMappings) {
              Assert.IsTrue(IsIdentityOrReverse(ports),
                "a reader observed a torn port-mapping generation");
            }

            ImmutableArray<DomeLayerView> stack = config.domeLayerStack;
            Assert.IsTrue(stack.Length == StackValidator.MaxLayers &&
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
        Assert.IsTrue(readerFailure == null,
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
        Assert.IsTrue(midiSettingsApplied.Wait(TimeSpan.FromSeconds(3)) &&
            outputSettingsApplied.Wait(TimeSpan.FromSeconds(3)),
          "the enabled operator did not reconcile the latest device generations");
        runtimeMidi.SettingsApplied -= ObserveMidiSettings;
        runtime.DomeOutput.OutputSettingsApplied -= ObserveOutputSettings;
        Assert.IsTrue(runtime.DomeOutput.AppliedTransportGeneration ==
            transportGeneration,
          "a wiring-only update reconciled the OPC transport");
        Assert.IsTrue(runtime.LayerPlanReconciliationCount == reconciliations,
          "control/device traffic reconciled the layer plan");
        Assert.IsTrue(ReferenceEquals(runtime.DomeOutput.RenderPlan, acceptedPlan),
          "control/device traffic replaced the accepted render plan");

        Assert.IsTrue(firstFpsPublished.Wait(TimeSpan.FromSeconds(4)),
          "the enabled operator did not complete its first FPS window");
        Task measurementCompleted = runtime.BeginAllocationMeasurement(30);
        measurementCompleted
          .WaitAsync(TimeSpan.FromSeconds(4))
          .GetAwaiter().GetResult();
        var allocation = runtime.EndAllocationMeasurement();
        Assert.IsTrue(allocation.Frames >= 30,
          "too few enabled operator frames were measured: " +
          allocation.Frames);
        // The CLR can charge one 64-byte thread/runtime bookkeeping object to
        // this window nondeterministically. Bound fixed noise tightly enough
        // that any recurring per-frame allocation still fails the test.
        const long maxFixedMeasurementNoise = 128;
        Assert.IsTrue(allocation.Bytes <= maxFixedMeasurementNoise,
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

    [TestMethod]
    public void MidiBindingFailuresAreContained() {
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
      Assert.IsTrue(dispatcher.WaitForPending(TimeSpan.FromSeconds(2)) &&
          dispatcher.PendingCount == 1,
        "the valid MIDI mutation was not queued");
      dispatcher.Drain();
      invocation.GetAwaiter().GetResult();

      MidiLogMessage[] messages = midi.MidiLog.DequeueAllMessages();
      Assert.IsTrue(messages.Any(message =>
          message.message != null &&
          message.message.Contains("wrong numeric type") &&
          message.message.Contains("has type Int32")),
        "an incompatible existing MIDI target was not rejected at compile time");
      Assert.IsTrue(messages.Any(message =>
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
