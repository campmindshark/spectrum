using System.Collections.Generic;
using System.Xml.Serialization;
using Spectrum.Base;

namespace Spectrum {

  // Current mutable XML representation of spectrum_config.xml. XSerializer
  // needs concrete List/Dictionary properties that it can populate in place;
  // those shapes stay here instead of defining the live application API.
  [XmlRoot("SpectrumConfiguration")]
  public sealed class SpectrumConfigurationDocument {
    public string? audioDeviceID { get; set; }
    public bool domeEnabled { get; set; }
    public bool midiInputEnabled { get; set; }
    public bool domeOutputInSeparateThread { get; set; }
    public string domeBeagleboneOPCAddress { get; set; } = "";
    public bool domeSimulationEnabled { get; set; }
    public bool webDomeSimulatorEnabled { get; set; } = true;
    public double domeMaxBrightness { get; set; } = 0.5;
    public double domeBrightness { get; set; } = 0.1;
    public int domeTestPattern { get; set; }
    public List<DomeLayerSettings>? domeLayerStack { get; set; }
    public int[]? domeCableMapping { get; set; }
    public DomePortMapping?[]? domePortMappings { get; set; }
    public double domeGlobalFadeSpeed { get; set; }
    public double domeGlobalHueSpeed { get; set; } = 1;
    public Dictionary<string, int> domeLayerFireCounters { get; set; } =
      new Dictionary<string, int>();
    public Dictionary<string, int> domeLayerClearCounters { get; set; } =
      new Dictionary<string, int>();
    public List<DomeScene>? domeScenes { get; set; }
    public List<DomePalette>? domePalettes { get; set; }
    public bool vjHUDEnabled { get; set; }
    public Dictionary<int, int> midiDevices { get; set; } =
      new Dictionary<int, int>();
    public Dictionary<int, MidiPreset> midiPresets { get; set; } =
      new Dictionary<int, MidiPreset>();
    public Dictionary<int, MidiLevelDriverPreset> midiLevelDriverChannels {
      get; set;
    } = new Dictionary<int, MidiLevelDriverPreset>();
    public double flashSpeed { get; set; }
    public int beatInput { get; set; }
    public int orientationDeviceSpotlight { get; set; }
    public bool orientationCalibrate { get; set; }
    public string wandSerialPort { get; set; } = "";

    public SpectrumConfiguration ToConfiguration() {
      var config = new SpectrumConfiguration {
        audioDeviceID = this.audioDeviceID,
        domeEnabled = this.domeEnabled,
        midiInputEnabled = this.midiInputEnabled,
        domeOutputInSeparateThread = this.domeOutputInSeparateThread,
        domeBeagleboneOPCAddress = this.domeBeagleboneOPCAddress ?? "",
        domeSimulationEnabled = this.domeSimulationEnabled,
        webDomeSimulatorEnabled = this.webDomeSimulatorEnabled,
        domeMaxBrightness = this.domeMaxBrightness,
        domeBrightness = this.domeBrightness,
        domeTestPattern = this.domeTestPattern,
        domeGlobalFadeSpeed = this.domeGlobalFadeSpeed,
        domeGlobalHueSpeed = this.domeGlobalHueSpeed,
        vjHUDEnabled = this.vjHUDEnabled,
        flashSpeed = this.flashSpeed,
        beatInput = this.beatInput,
        orientationDeviceSpotlight = this.orientationDeviceSpotlight,
        orientationCalibrate = this.orientationCalibrate,
        wandSerialPort = this.wandSerialPort ?? "",
      };
      config.ReplaceDomeLayerStack(this.domeLayerStack);
      config.ReplaceDomeCableMapping(this.domeCableMapping);
      config.ReplaceDomePortMappings(this.domePortMappings);
      config.ReplaceDomeLayerFireCounters(this.domeLayerFireCounters);
      config.ReplaceDomeLayerClearCounters(this.domeLayerClearCounters);
      config.ReplaceDomeScenes(this.domeScenes);
      config.ReplaceDomePalettes(this.domePalettes);
      config.ReplaceMidiDevices(this.midiDevices);
      config.ReplaceMidiPresets(this.midiPresets);
      config.ReplaceMidiLevelDriverChannels(this.midiLevelDriverChannels);
      return config;
    }

    public static SpectrumConfigurationDocument FromConfiguration(
      SpectrumConfiguration config
    ) => config.CreateDocument();
  }

  internal static class ConfigurationGraphCopy {
    internal static T[] Array<T>(IReadOnlyList<T>? source) {
      if (source == null) {
        return System.Array.Empty<T>();
      }
      var copy = new T[source.Count];
      for (int i = 0; i < source.Count; i++) {
        copy[i] = source[i];
      }
      return copy;
    }

    internal static Dictionary<TKey, TValue> Dictionary<TKey, TValue>(
      IReadOnlyDictionary<TKey, TValue>? source
    ) where TKey : notnull => source == null
      ? new Dictionary<TKey, TValue>()
      : new Dictionary<TKey, TValue>(source);

    internal static DomePortMapping?[] PortMappings(
      IReadOnlyList<DomePortMapping?>? source
    ) => source == null
      ? System.Array.Empty<DomePortMapping?>()
      : CopyPortMappings(source);

    private static DomePortMapping?[] CopyPortMappings(
      IReadOnlyList<DomePortMapping?> source
    ) {
      var copy = new DomePortMapping?[source.Count];
      for (int i = 0; i < source.Count; i++) {
        DomePortMapping? mapping = source[i];
        copy[i] = mapping == null
          ? null
          : new DomePortMapping(mapping.ports);
      }
      return copy;
    }

    internal static List<DomeLayerSettings> Layers(
      IReadOnlyList<DomeLayerSettings>? source
    ) {
      if (source == null) {
        return new List<DomeLayerSettings>();
      }
      var copy = new List<DomeLayerSettings>(source.Count);
      foreach (DomeLayerSettings? layer in source) {
        if (layer != null) {
          copy.Add(Layer(layer));
        }
      }
      return copy;
    }

    internal static DomeLayerSettings Layer(DomeLayerSettings source) =>
      new DomeLayerSettings {
        InstanceId = source.InstanceId,
        VisualizerKey = source.VisualizerKey,
        BlendMode = source.BlendMode,
        Opacity = source.Opacity,
        Enabled = source.Enabled,
        Notes = source.Notes,
        RendererParams = source.RendererParams == null
          ? null
          : new Dictionary<string, double>(source.RendererParams),
        OperationParams = source.OperationParams == null
          ? null
          : new Dictionary<string, double>(source.OperationParams),
      };

    internal static List<DomeScene> Scenes(
      IReadOnlyList<DomeScene>? source
    ) {
      if (source == null) {
        return new List<DomeScene>();
      }
      var copy = new List<DomeScene>(source.Count);
      foreach (DomeScene? scene in source) {
        if (scene == null) {
          continue;
        }
        copy.Add(new DomeScene {
          Name = scene.Name,
          Layers = Layers(scene.Layers),
          GlobalFadeSpeed = scene.GlobalFadeSpeed,
          GlobalHueSpeed = scene.GlobalHueSpeed,
        });
      }
      return copy;
    }

    internal static List<DomePalette> Palettes(
      IReadOnlyList<DomePalette>? source
    ) {
      if (source == null) {
        return new List<DomePalette>();
      }
      var copy = new List<DomePalette>(source.Count);
      foreach (DomePalette? palette in source) {
        if (palette == null) {
          continue;
        }
        copy.Add(new DomePalette {
          Name = palette.Name,
          Colors = DomePalette.CopyColors(palette.Colors),
        });
      }
      return copy;
    }

    internal static Dictionary<int, MidiPreset> MidiPresets(
      IReadOnlyDictionary<int, MidiPreset>? source
    ) {
      if (source == null) {
        return new Dictionary<int, MidiPreset>();
      }
      var copy = new Dictionary<int, MidiPreset>();
      foreach (KeyValuePair<int, MidiPreset> pair in source) {
        if (pair.Value != null) {
          copy[pair.Key] = MidiPreset(pair.Value);
        }
      }
      return copy;
    }

    internal static MidiPreset MidiPreset(MidiPreset source) {
      var bindings = new List<IMidiBindingConfig>();
      if (source.Bindings != null) {
        foreach (IMidiBindingConfig? binding in source.Bindings) {
          if (binding != null) {
            bindings.Add((IMidiBindingConfig)binding.Clone());
          }
        }
      }
      return new MidiPreset {
        id = source.id,
        Name = source.Name,
        Bindings = bindings,
      };
    }

    internal static Dictionary<int, MidiLevelDriverPreset>
      MidiLevelDriverChannels(
        IReadOnlyDictionary<int, MidiLevelDriverPreset>? source
      ) {
      if (source == null) {
        return new Dictionary<int, MidiLevelDriverPreset>();
      }
      var copy = new Dictionary<int, MidiLevelDriverPreset>();
      foreach (KeyValuePair<int, MidiLevelDriverPreset> pair in source) {
        if (pair.Value != null) {
          copy[pair.Key] = (MidiLevelDriverPreset)pair.Value.Clone();
        }
      }
      return copy;
    }
  }
}
