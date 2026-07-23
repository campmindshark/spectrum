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

  public static class StateOrchestrationTests {
    public static void Register(Action<string, Action> run) {
      run(nameof(ConfigurationPublishesSnapshot), ConfigurationPublishesSnapshot);
      run(nameof(ShowStateTransactionsAreAtomic), ShowStateTransactionsAreAtomic);
      run(nameof(ShowStateSseIsAtomic), ShowStateSseIsAtomic);
      run(nameof(InitialShowStateSseIsAtomic), InitialShowStateSseIsAtomic);
      run(nameof(ConfigurationMutationsUseStateDispatcher), ConfigurationMutationsUseStateDispatcher);
      run(nameof(RuntimeSettingsPublishCompleteGenerations), RuntimeSettingsPublishCompleteGenerations);
      run(nameof(WebReadsUseStateDispatcher), WebReadsUseStateDispatcher);
    }
    private static void ConfigurationPublishesSnapshot() {
      var config = ConfigurationWithLayers(Layer("background", null));
      var source = (ILayerStackSnapshotSource)config;
      LayerStackSnapshot published = source.DomeLayerStackSnapshot;
      DomeLayerView dto = config.domeLayerStack[0];

      Assert(!string.IsNullOrWhiteSpace(dto.InstanceId),
        "the publication boundary did not assign an instance ID");
      Assert(published.Layers[0].Id.Value == dto.InstanceId,
        "the DTO and immutable snapshot received different identities");
      DomeLayerView changed = dto with { Enabled = false };
      Assert(published.Layers[0].Enabled &&
          config.domeLayerStack[0].Enabled && !changed.Enabled,
        "the published snapshot retained a mutable view");
    }

    private static void ShowStateTransactionsAreAtomic() {
      var colors = new LEDColor[DomePalette.SlotCount];
      colors[0] = new LEDColor(0x112233, 0x445566);
      var config = new global::Spectrum.SpectrumConfiguration {
        domeGlobalFadeSpeed = 0.25,
        domeGlobalHueSpeed = 0.5,
      };
      config.ReplaceDomeLayerStack(new[] {
        Layer("background", "old-layer"),
      });
      config.ReplaceDomePalettes(new[] {
        new DomePalette { Name = "Live", Colors = colors },
      });
      var source = (IDomeShowStateConfiguration)config;
      DomeShowStateSnapshot beforePaletteEdit =
        source.DomeShowStateSnapshot;

      new PaletteService(config).ReplaceColors(
        "Live", new[] { new LEDColor(0xAABBCC) });
      DomeShowStateSnapshot afterPaletteEdit = source.DomeShowStateSnapshot;
      Assert(afterPaletteEdit.Generation > beforePaletteEdit.Generation,
        "an in-place palette edit did not publish a new generation");
      Assert(beforePaletteEdit.Palettes[0].GetSingleColor(0) == 0x112233 &&
          afterPaletteEdit.Palettes[0].GetSingleColor(0) == 0xAABBCC,
        "a show-state snapshot retained mutable palette objects");

      config.ReplaceDomeScenes(new List<DomeScene> {
        new DomeScene {
          Name = "Next",
          Layers = new List<DomeLayerSettings> {
            Layer("radial", "new-layer"),
          },
          GlobalFadeSpeed = 0.75,
          GlobalHueSpeed = 1.5,
        },
      });
      int generationNotifications = 0;
      int compatibilityNotifications = 0;
      config.PropertyChanged += (sender, e) => {
        if (e.PropertyName ==
            DomeShowStateSnapshot.NotificationPropertyName) {
          generationNotifications++;
          return;
        }
        if (e.PropertyName == nameof(config.domeLayerStack) ||
            e.PropertyName == nameof(config.domeGlobalFadeSpeed) ||
            e.PropertyName == nameof(config.domeGlobalHueSpeed)) {
          compatibilityNotifications++;
          Assert(config.domeLayerStack[0].InstanceId == "new-layer" &&
              config.domeGlobalFadeSpeed == 0.75 &&
              config.domeGlobalHueSpeed == 1.5,
            "a subscriber observed a partially applied scene");
        }
      };

      (bool applied, string? error) = new SceneService(
        config, DomeLayerCatalog.Metadata).Apply("Next");
      DomeShowStateSnapshot appliedState = source.DomeShowStateSnapshot;
      Assert(applied, error);
      Assert(generationNotifications == 1 &&
          compatibilityNotifications == 3,
        "scene recall did not publish exactly one show generation");
      Assert(appliedState.LayerStack.Layers[0].Id.Value == "new-layer" &&
          appliedState.GlobalFadeSpeed == 0.75 &&
          appliedState.GlobalHueSpeed == 1.5 &&
          appliedState.Palettes[0].GetSingleColor(0) == 0xAABBCC,
        "the recalled generation mixed old and new show values");
    }

    private static void ShowStateSseIsAtomic() {
      var config = new global::Spectrum.SpectrumConfiguration {
        domeGlobalFadeSpeed = 0.1,
        domeGlobalHueSpeed = 0.2,
      };
      config.ReplaceDomeLayerStack(new[] {
        Layer("background", "sse-old"),
      });
      config.ReplaceDomePalettes(new[] {
        new DomePalette {
          Name = "SSE",
          Colors = new[] { new LEDColor(0x123456) },
        },
      });
      config.ReplaceDomeScenes(new[] {
        new DomeScene {
          Name = "SSE Next",
          Layers = new List<DomeLayerSettings> {
            Layer("background", "sse-new"),
          },
          GlobalFadeSpeed = 0.8,
          GlobalHueSpeed = 1.2,
        },
      });
      using var stream = new global::Spectrum.Web.ConfigEventStream(
        global::Spectrum.Web.SpectrumParameters.BuildRegistry(),
        config, null, null, null);
      global::Spectrum.Web.ConfigEventStream.Subscriber subscriber =
        stream.Subscribe(ControlRole.Maintenance, out Guid id);

      (bool applied, string? error) =
        new SceneService(
          config, DomeLayerCatalog.Metadata).Apply("SSE Next");
      Assert(applied, error);
      var frames = new List<string>();
      while (subscriber.Reader.TryRead(out string? frame)) {
        frames.Add(frame);
      }
      Assert(frames.Count == 1 &&
          frames[0].Contains("\"kind\":\"show\"") &&
          frames[0].Contains("sse-new") &&
          frames[0].Contains("\"globalFadeSpeed\":0.8") &&
          frames[0].Contains("\"globalHueSpeed\":1.2"),
        "SSE exposed a compound show update as intermediate frames");
      stream.Unsubscribe(id);
    }

    private static void InitialShowStateSseIsAtomic() {
      var oldColors = new LEDColor[DomePalette.SlotCount];
      oldColors[0] = new LEDColor(0x112233);
      var config = new global::Spectrum.SpectrumConfiguration {
        domeGlobalFadeSpeed = 0.125,
        domeGlobalHueSpeed = 0.25,
      };
      config.ReplaceDomeLayerStack(new[] {
        Layer("background", "initial-old"),
      });
      config.ReplaceDomePalettes(new[] {
        new DomePalette { Name = "Old", Colors = oldColors },
      });
      DomeShowStateSnapshot? captured = null;
      int captureCount = 0;
      using var snapshotCaptured = new ManualResetEventSlim();
      using var continueSerialization = new ManualResetEventSlim();
      using var stream = new global::Spectrum.Web.ConfigEventStream(
        global::Spectrum.Web.SpectrumParameters.BuildRegistry(),
        config, null, null, null,
        snapshot => {
          if (Interlocked.Increment(ref captureCount) != 1) {
            return;
          }
          captured = snapshot;
          snapshotCaptured.Set();
          continueSerialization.Wait();
        });

      Task<List<string>> initialFrames =
        Task.Run(() => stream.InitialStateFrames());
      bool didCapture = snapshotCaptured.Wait(TimeSpan.FromSeconds(2));
      if (!didCapture) {
        continueSerialization.Set();
      }
      Assert(didCapture,
        "initial SSE serialization did not capture the show snapshot");

      var newColors = new LEDColor[DomePalette.SlotCount];
      newColors[0] = new LEDColor(0xAABBCC);
      ((IDomeShowStateConfiguration)config).ApplyDomeShowState(
        new DomeShowStateUpdate(
          new List<DomeLayerSettings> {
            Layer("background", "initial-new"),
          },
          new List<DomePalette> {
            new DomePalette { Name = "New", Colors = newColors },
          },
          0.75,
          1.5,
          DomeSceneView.ToScenes(config.domeScenes)));
      continueSerialization.Set();

      string show = initialFrames.GetAwaiter().GetResult().Single(
        frame => frame.Contains("\"kind\":\"show\""));
      Assert(captured != null,
        "the show-state serialization did not capture a snapshot");
      Assert(show.Contains(
            "\"generation\":" + captured.Generation) &&
          show.Contains("initial-old") &&
          show.Contains("#112233") &&
          show.Contains("\"globalFadeSpeed\":0.125") &&
          show.Contains("\"globalHueSpeed\":0.25") &&
          !show.Contains("initial-new") &&
          !show.Contains("#AABBCC"),
        "an initial SSE frame mixed fields from two show generations");
    }

    private static void ConfigurationMutationsUseStateDispatcher() {
      var config = new global::Spectrum.SpectrumConfiguration();
      var dispatcher = new QueuedStateDispatcher();
      config.AttachMutationDispatcher(dispatcher);
      int notificationThread = -1;
      int notifications = 0;
      config.PropertyChanged += (sender, e) => {
        if (e.PropertyName == nameof(config.domeBrightness)) {
          notificationThread = Environment.CurrentManagedThreadId;
          notifications++;
        }
      };

      RunOnDedicatedThread(() => {
        config.domeBrightness = 0.75;
        return true;
      });
      Assert(Math.Abs(config.domeBrightness - 0.1) < 0.000001 &&
          notifications == 0 && dispatcher.PendingCount == 1,
        "an off-thread configuration write bypassed the dispatcher");

      dispatcher.Drain();
      Assert(Math.Abs(config.domeBrightness - 0.75) < 0.000001 &&
          notifications == 1 &&
          notificationThread == Environment.CurrentManagedThreadId,
        "PropertyChanged was not delivered on the state-owner thread");
    }

    private static void RuntimeSettingsPublishCompleteGenerations() {
      var config = new global::Spectrum.SpectrumConfiguration();
      var source = (IRuntimeSettingsConfiguration)config;

      var aliasedCounters = new Dictionary<string, int> {
        ["immutable"] = 7,
      };
      config.ReplaceDomeLayerFireCounters(aliasedCounters);
      DomeRuntimeFrameSnapshot retained = source.DomeRuntimeFrameSnapshot;
      aliasedCounters["immutable"] = 99;
      Assert(retained.FireGeneration("immutable") == 7,
        "a published command snapshot retained its mutable source map");

      var aliasedCableMapping = Enumerable.Range(
        0, LEDDomeOutput.NumCables).ToArray();
      config.ReplaceDomeCableMapping(aliasedCableMapping);
      DomeOutputSettingsSnapshot retainedOutput =
        source.DomeOutputSettingsSnapshot;
      aliasedCableMapping[0] = 9;
      Assert(retainedOutput.CableMapping[0] == 0,
        "a published output snapshot retained its mutable source array");

      Exception? readerFailure = null;
      int iterations = 1500;
      Task writer = Task.Run(() => {
        for (int generation = 1; generation <= iterations; generation++) {
          var counters = new Dictionary<string, int>();
          for (int layer = 0; layer < 16; layer++) {
            counters["layer-" + layer] = generation;
          }
          config.ReplaceDomeLayerFireCounters(counters);
          config.ReplaceDomeCableMapping(generation % 2 == 0
            ? Enumerable.Range(0, LEDDomeOutput.NumCables).ToArray()
            : Enumerable.Range(
                0, LEDDomeOutput.NumCables).Reverse().ToArray());
        }
      });

      while (!writer.IsCompleted && readerFailure == null) {
        try {
          DomeRuntimeFrameSnapshot runtime =
            source.DomeRuntimeFrameSnapshot;
          if (runtime.FireGenerations.Count == 16) {
            int expected = runtime.FireGenerations["layer-0"];
            foreach (int value in runtime.FireGenerations.Values) {
              Assert(value == expected,
                "a reader observed a torn fire-counter generation");
            }
          }

          var mapping = source.DomeOutputSettingsSnapshot.CableMapping;
          if (mapping.Length == LEDDomeOutput.NumCables) {
            bool identity = true;
            bool reverse = true;
            for (int i = 0; i < mapping.Length; i++) {
              identity &= mapping[i] == i;
              reverse &= mapping[i] == mapping.Length - 1 - i;
            }
            Assert(identity || reverse,
              "a reader observed a torn cable-mapping generation");
          }
        } catch (Exception error) {
          readerFailure = error;
        }
      }
      writer.GetAwaiter().GetResult();
      if (readerFailure != null) {
        throw readerFailure;
      }
    }

    private static void WebReadsUseStateDispatcher() {
      var layer = Layer("background", "web-owner-read");
      layer.RendererParams = new Dictionary<string, double> {
        ["level"] = 0.4,
      };
      var config = ConfigurationWithLayers(layer);
      var dispatcher = new QueuedStateDispatcher();
      config.AttachMutationDispatcher(dispatcher);
      var controller = new global::Spectrum.Web.LayersController(
        dispatcher, config);

      Exception? directReadError = null;
      try {
        Task.Run(() => config.domeLayerStack).GetAwaiter().GetResult();
      } catch (Exception error) {
        directReadError = error;
      }
      Assert(directReadError == null,
        "an immutable off-owner configuration read was rejected");

      Task<global::Spectrum.Web.LayersController.LayersState> read =
        Task.Run(controller.StateAsync);
      Assert(dispatcher.WaitForPending(TimeSpan.FromSeconds(2)) &&
          dispatcher.PendingCount == 1 && !read.IsCompleted,
        "a compound web read bypassed the state-owner dispatcher");
      dispatcher.Drain();
      var state = read.GetAwaiter().GetResult();
      Assert(state.layers.Count == 1 &&
          state.layers[0].instanceId == "web-owner-read",
        "the owner-thread web projection returned the wrong layer state");
      state.layers[0].rendererParams!["level"] = 0.9;
      Assert(Math.Abs(
          config.domeLayerStack[0].RendererParams["level"] - 0.4) < 1e-9,
        "a web DTO retained a mutable configuration alias");
    }

  }
}
