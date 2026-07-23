using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Spectrum.Base;
using XSerializer;
using static Spectrum.LayerPipeline.Tests.TestAssertions;

namespace Spectrum.LayerPipeline.Tests {

  public static class ConfigurationContractTests {
    public static void Register(Action<string, Action> run) {
      run(nameof(ConfigurationSerializes), ConfigurationSerializes);
      run(
        nameof(ConfigurationCollectionsIsolateNestedAliases),
        ConfigurationCollectionsIsolateNestedAliases);
      run(
        nameof(ConfigurationCollectionNotificationsAreExact),
        ConfigurationCollectionNotificationsAreExact);
      run(
        nameof(ConfigurationSurfaceRejectsMutableCollections),
        ConfigurationSurfaceRejectsMutableCollections);
      run(
        nameof(EventStreamSubscribersAreBounded),
        EventStreamSubscribersAreBounded);
      run(nameof(WebLayerContract), WebLayerContract);
    }

    private static void ConfigurationSerializes() {
      DomeLayerSettings serializedLayer = Layer(
        "background", "serialize-background");
      serializedLayer.RendererParams = new Dictionary<string, double> {
        ["color"] = 0x123456,
      };
      serializedLayer.OperationParams = new Dictionary<string, double> {
        ["amount"] = .5,
      };
      var config = new global::Spectrum.SpectrumConfiguration {
        audioDeviceID = "test-device",
      };
      config.ReplaceDomeLayerStack(new[] { serializedLayer });
      config.ReplaceMidiLevelDriverChannels(
        new Dictionary<int, MidiLevelDriverPreset> {
          [2] = new MidiLevelDriverPreset {
            AttackTime = 10,
            PeakLevel = 0.9,
            DecayTime = 20,
            SustainLevel = 0.7,
            ReleaseTime = 30,
          },
        });
      using var stream = new MemoryStream();
      new XmlSerializer<global::Spectrum.SpectrumConfigurationDocument>()
        .Serialize(
          stream,
          global::Spectrum.SpectrumConfigurationDocument.FromConfiguration(
            config));
      Assert(stream.Length > 0, "serializer produced no XML");
      string xml = System.Text.Encoding.UTF8.GetString(stream.ToArray());
      Assert(xml.Contains("<SpectrumConfiguration") &&
          !xml.Contains("<SpectrumConfigurationDocument"),
        "the configuration document emitted the wrong XML root");
      stream.Position = 0;
      var restored =
        new XmlSerializer<global::Spectrum.SpectrumConfigurationDocument>()
          .Deserialize(stream).ToConfiguration();
      Assert(restored.audioDeviceID == "test-device", "round trip lost config");
      Assert(restored.domeLayerStack.Length == 1 &&
        restored.domeLayerStack[0].InstanceId == "serialize-background",
        "round trip lost layer identity");
      Assert(restored.domeLayerStack[0].RendererParams["color"] == 0x123456 &&
          restored.domeLayerStack[0].OperationParams["amount"] == .5,
        "round trip merged or lost parameter namespaces");
      BeatSettingsSnapshot beat =
        ((IRuntimeSettingsConfiguration)restored).BeatSettingsSnapshot;
      Assert(beat.TryGetMidiPreset(
          2, out MidiLevelDriverSettingsSnapshot envelope) &&
          envelope.AttackTime == 10 && envelope.PeakLevel == 0.9 &&
          envelope.DecayTime == 20 && envelope.SustainLevel == 0.7 &&
          envelope.ReleaseTime == 30,
        "round trip lost the MIDI level-driver channel");
    }

    private static void ConfigurationCollectionsIsolateNestedAliases() {
      var layer = Layer("background", "alias-layer");
      layer.RendererParams = new Dictionary<string, double> {
        ["color"] = 0x123456,
      };
      var sceneLayer = Layer("background", "alias-scene-layer");
      sceneLayer.OperationParams = new Dictionary<string, double> {
        ["amount"] = 0.25,
      };
      var paletteColor = new LEDColor(0x112233, 0x445566);
      var binding = new ContinuousKnobMidiBindingConfig {
        BindingName = "alias-binding",
        knobIndex = 7,
        configPropertyName = nameof(Configuration.domeBrightness),
        startValue = 0,
        endValue = 1,
      };
      var preset = new MidiPreset {
        id = 3,
        Name = "Alias preset",
        Bindings = new List<IMidiBindingConfig> { binding },
      };
      var config = new global::Spectrum.SpectrumConfiguration();
      config.ReplaceDomeLayerStack(new[] { layer });
      config.ReplaceDomeScenes(new[] {
        new DomeScene {
          Name = "Alias scene",
          Layers = new List<DomeLayerSettings> { sceneLayer },
        },
      });
      config.ReplaceDomePalettes(new[] {
        new DomePalette {
          Name = "Alias palette",
          Colors = new[] { paletteColor },
        },
      });
      config.ReplaceMidiPresets(new Dictionary<int, MidiPreset> {
        [3] = preset,
      });

      layer.RendererParams["color"] = 0;
      sceneLayer.OperationParams["amount"] = 1;
      paletteColor.color1 = 0;
      binding.endValue = 0;
      preset.Bindings.Clear();
      Assert(config.domeLayerStack[0].RendererParams["color"] == 0x123456 &&
          config.domeScenes[0].Layers[0].OperationParams["amount"] == 0.25 &&
          config.domePalettes[0].Colors[0]?.Color1 == 0x112233 &&
          config.midiPresets[3].Bindings.Length == 1 &&
          ((ContinuousKnobMidiBindingView)
            config.midiPresets[3].Bindings[0]).EndValue == 1,
        "a collection edit retained a nested source alias");

      global::Spectrum.SpectrumConfigurationDocument document =
        global::Spectrum.SpectrumConfigurationDocument.FromConfiguration(
          config);
      document.domeLayerStack![0].RendererParams!["color"] = 7;
      document.domeScenes![0].Layers![0].OperationParams!["amount"] = 7;
      document.domePalettes![0].Colors![0]!.color1 = 7;
      document.midiPresets[3].Bindings.Clear();
      Assert(config.domeLayerStack[0].RendererParams["color"] == 0x123456 &&
          config.domeScenes[0].Layers[0].OperationParams["amount"] == 0.25 &&
          config.domePalettes[0].Colors[0]?.Color1 == 0x112233 &&
          config.midiPresets[3].Bindings.Length == 1,
        "the persistence document retained a live configuration alias");
    }

    private static void ConfigurationCollectionNotificationsAreExact() {
      var config = new global::Spectrum.SpectrumConfiguration();
      var names = new List<string>();
      config.PropertyChanged += (_, e) => {
        if (e.PropertyName != null) {
          names.Add(e.PropertyName);
        }
      };

      config.ReplaceDomeLayerStack(new[] {
        Layer("background", "notification-layer"),
      });
      Assert(names.SequenceEqual(new[] {
          nameof(config.domeLayerStack),
          DomeShowStateSnapshot.NotificationPropertyName,
        }),
        "a layer replacement published extra or missing notifications");

      names.Clear();
      config.ReplaceDomePalettes(new[] {
        new DomePalette { Name = "Notification palette" },
      });
      Assert(names.SequenceEqual(new[] {
          nameof(config.domePalettes),
          DomeShowStateSnapshot.NotificationPropertyName,
        }),
        "a palette replacement published extra or missing notifications");

      names.Clear();
      config.UpsertMidiPreset(4, new MidiPreset {
        id = 4,
        Name = "Notification MIDI",
      });
      Assert(names.SequenceEqual(new[] { nameof(config.midiPresets) }),
        "a MIDI preset edit published extra or missing notifications");
    }

    private static void ConfigurationSurfaceRejectsMutableCollections() {
      Type[] mutableDefinitions = {
        typeof(List<>),
        typeof(Dictionary<,>),
        typeof(IList<>),
        typeof(IDictionary<,>),
        typeof(ICollection<>),
      };
      foreach (Type surface in new[] {
          typeof(Configuration),
          typeof(global::Spectrum.SpectrumConfiguration),
        }) {
        foreach (System.Reflection.PropertyInfo property in
            surface.GetProperties()) {
          Type type = property.PropertyType;
          bool mutable = type.IsArray ||
            type.IsGenericType && mutableDefinitions.Contains(
              type.GetGenericTypeDefinition());
          Assert(!mutable,
            surface.Name + "." + property.Name +
            " exposes mutable collection type " + type.Name);
        }
      }
    }

    private static void EventStreamSubscribersAreBounded() {
      var config = ConfigurationWithLayers(
        Layer("background", "coalesced-show"));
      var telemetry = new RuntimeTelemetry();
      using var stream = new global::Spectrum.Web.ConfigEventStream(
        global::Spectrum.Web.SpectrumParameters.BuildRegistry(),
        config, null, telemetry, null);
      global::Spectrum.Web.ConfigEventStream.Subscriber subscriber =
        stream.Subscribe(ControlRole.Maintenance, out Guid id);
      config.domeGlobalFadeSpeed = 0.75;
      int writes =
        global::Spectrum.Web.ConfigEventStream.SubscriberCapacity + 37;
      for (int value = 1; value <= writes; value++) {
        telemetry.OperatorFPS = value;
      }

      var retained = new List<string>();
      while (subscriber.Reader.TryRead(out string? frame)) {
        retained.Add(frame);
      }
      Assert(retained.Count == 2 &&
          retained.Any(frame =>
            frame.Contains("\"kind\":\"show\"") &&
            frame.Contains("coalesced-show")) &&
          retained.Any(frame =>
            frame.Contains("\"key\":\"operatorFPS\"") &&
            frame.Contains("\"value\":" + writes)),
        "an unrelated telemetry flood displaced infrequent show state");

      for (int key = 0;
          key <= global::Spectrum.Web.ConfigEventStream.SubscriberCapacity;
          key++) {
        subscriber.Write("test", "distinct-" + key, "{}");
      }
      retained.Clear();
      while (subscriber.Reader.TryRead(out string? frame)) {
        retained.Add(frame);
      }
      Assert(retained.Count <=
          global::Spectrum.Web.ConfigEventStream.SubscriberCapacity &&
          retained.Any(frame =>
            frame.Contains("\"kind\":\"reset\"")),
        "distinct SSE state overflow was not bounded by a resync marker");
      stream.Unsubscribe(id);
    }

    private static void WebLayerContract() {
      DomeLayerSettings layer = Layer("background", "web-background");
      layer.RendererParams = new Dictionary<string, double> {
        ["color"] = 0xABCDEF,
      };
      layer.BlendMode = DomeBlend.ChromaticFringe.Id;
      layer.OperationParams = new Dictionary<string, double> {
        ["offset"] = .125,
      };
      var config = ConfigurationWithLayers(layer);
      var controller = new global::Spectrum.Web.LayersController(
        new InlineGateway(), config);
      global::Spectrum.Web.LayersController.LayersState state =
        controller.State();
      Assert(state.layers[0].rendererParams!["color"] == 0xABCDEF &&
        state.layers[0].operationParams!["offset"] == .125,
        "web contract merged parameter namespaces");
      global::Spectrum.Web.LayersController.OperationOptionDto? operation =
        null;
      foreach (
        global::Spectrum.Web.LayersController.OperationOptionDto candidate
        in state.operations
      ) {
        if (candidate.id == DomeBlend.ChromaticFringe.Id) {
          operation = candidate;
          break;
        }
      }
      Assert(operation != null &&
        operation.label == DomeBlend.ChromaticFringe.DisplayName &&
        operation.@params.Count > 0,
        "web operation descriptor is incomplete");
      global::Spectrum.Web.LayersController.VisualizerOptionDto? astronomy =
        state.visualizers.FirstOrDefault(v => v.key == "astronomy");
      global::Spectrum.Web.LayersController.VisualizerOptionDto? background =
        state.visualizers.FirstOrDefault(v => v.key == "background");
      global::Spectrum.Web.LayersController.ParamDto? startDate =
        astronomy?.@params.FirstOrDefault(p => p.key == "startDate");
      global::Spectrum.Web.LayersController.ParamDto? showDaytimeSky =
        astronomy?.@params.FirstOrDefault(
          p => p.key == "showDaytimeSky");
      global::Spectrum.Web.LayersController.ParamDto? showNighttimeSky =
        astronomy?.@params.FirstOrDefault(
          p => p.key == "showNighttimeSky");
      Assert(startDate != null && startDate.type == "Date" &&
          DomeLayerDate.TryDecode(startDate.@default, out _),
        "web astronomy start-date descriptor is incomplete");
      Assert(showDaytimeSky != null && showDaytimeSky.type == "Bool" &&
          showDaytimeSky.@default == 1,
        "web astronomy daytime-sky checkbox descriptor is incomplete");
      Assert(showNighttimeSky != null &&
          showNighttimeSky.type == "Bool" &&
          showNighttimeSky.@default == 1,
        "web astronomy nighttime-sky checkbox descriptor is incomplete");
      Assert(astronomy?.fireAction?.label == "Play" &&
          astronomy.clearAction?.label == "Stop" &&
          background?.fireAction == null && background?.clearAction == null,
        "web layer action descriptors are incomplete");
    }

    private static global::Spectrum.SpectrumConfiguration
      ConfigurationWithLayers(params DomeLayerSettings[] layers) {
      var config = new global::Spectrum.SpectrumConfiguration();
      config.ReplaceDomeLayerStack(layers);
      return config;
    }

    private static DomeLayerSettings Layer(string key, string? id) => new() {
      InstanceId = id,
      VisualizerKey = key,
      BlendMode = DomeBlend.Add.Id,
      Opacity = 1,
      Enabled = true,
    };

    private sealed class InlineGateway : ApplicationStateDispatcher {
      public bool CheckAccess() => true;
      public void Post(Action mutation) => mutation();
      public Task InvokeAsync(Action mutation) {
        mutation();
        return Task.CompletedTask;
      }
      public Task<T> InvokeAsync<T>(Func<T> read) =>
        Task.FromResult(read());
    }
  }
}
