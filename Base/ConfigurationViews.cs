using System.Collections.Generic;
using System.Collections.Immutable;

namespace Spectrum.Base {

  // Cached immutable read models for the application-facing configuration
  // API. Serializer DTOs are converted at publication boundaries and never
  // escape through Configuration.
  public sealed record DomeLayerView(
    string? InstanceId,
    string? VisualizerKey,
    string BlendMode,
    double Opacity,
    bool Enabled,
    string? Notes,
    ImmutableDictionary<string, double> RendererParams,
    ImmutableDictionary<string, double> OperationParams
  ) {
    public static DomeLayerView FromSettings(DomeLayerSettings settings) =>
      new DomeLayerView(
        settings.InstanceId,
        settings.VisualizerKey,
        settings.BlendMode,
        settings.Opacity,
        settings.Enabled,
        settings.Notes,
        settings.RendererParams == null
          ? ImmutableDictionary<string, double>.Empty
          : settings.RendererParams.ToImmutableDictionary(),
        settings.OperationParams == null
          ? ImmutableDictionary<string, double>.Empty
          : settings.OperationParams.ToImmutableDictionary());

    public DomeLayerSettings ToSettings() => new DomeLayerSettings {
      InstanceId = this.InstanceId,
      VisualizerKey = this.VisualizerKey,
      BlendMode = this.BlendMode,
      Opacity = this.Opacity,
      Enabled = this.Enabled,
      Notes = this.Notes,
      RendererParams = this.RendererParams.IsEmpty
        ? null
        : new Dictionary<string, double>(this.RendererParams),
      OperationParams = this.OperationParams.IsEmpty
        ? null
        : new Dictionary<string, double>(this.OperationParams),
    };

    public static ImmutableArray<DomeLayerView> Compile(
      IReadOnlyList<DomeLayerSettings>? settings
    ) {
      if (settings == null || settings.Count == 0) {
        return ImmutableArray<DomeLayerView>.Empty;
      }
      var result = ImmutableArray.CreateBuilder<DomeLayerView>(settings.Count);
      foreach (DomeLayerSettings layer in settings) {
        if (layer != null) {
          result.Add(FromSettings(layer));
        }
      }
      return result.MoveToImmutable();
    }

    public static List<DomeLayerSettings> ToSettings(
      ImmutableArray<DomeLayerView> layers
    ) {
      var result = new List<DomeLayerSettings>(layers.Length);
      foreach (DomeLayerView layer in layers) {
        if (layer != null) {
          result.Add(layer.ToSettings());
        }
      }
      return result;
    }
  }

  public sealed record DomeSceneView(
    string? Name,
    ImmutableArray<DomeLayerView> Layers,
    double GlobalFadeSpeed,
    double GlobalHueSpeed
  ) {
    public static DomeSceneView FromScene(DomeScene scene) =>
      new DomeSceneView(
        scene.Name,
        DomeLayerView.Compile(scene.Layers),
        scene.GlobalFadeSpeed,
        scene.GlobalHueSpeed);

    public DomeScene ToScene() => new DomeScene {
      Name = this.Name,
      Layers = DomeLayerView.ToSettings(this.Layers),
      GlobalFadeSpeed = this.GlobalFadeSpeed,
      GlobalHueSpeed = this.GlobalHueSpeed,
    };

    public static ImmutableArray<DomeSceneView> Compile(
      IReadOnlyList<DomeScene>? scenes
    ) {
      if (scenes == null || scenes.Count == 0) {
        return ImmutableArray<DomeSceneView>.Empty;
      }
      var result = ImmutableArray.CreateBuilder<DomeSceneView>(scenes.Count);
      foreach (DomeScene scene in scenes) {
        if (scene != null) {
          result.Add(FromScene(scene));
        }
      }
      return result.MoveToImmutable();
    }

    public static List<DomeScene> ToScenes(
      ImmutableArray<DomeSceneView> scenes
    ) {
      var result = new List<DomeScene>(scenes.Length);
      foreach (DomeSceneView scene in scenes) {
        if (scene != null) {
          result.Add(scene.ToScene());
        }
      }
      return result;
    }
  }

  public interface IMidiBindingView {
    int BindingType { get; }
    string? BindingName { get; }
    IMidiBindingConfig ToConfig();
  }

  public sealed record TapTempoMidiBindingView(
    string? BindingName,
    MidiCommandType ButtonType,
    int ButtonIndex
  ) : IMidiBindingView {
    public int BindingType => 0;
    public IMidiBindingConfig ToConfig() => new TapTempoMidiBindingConfig {
      BindingName = this.BindingName,
      buttonType = this.ButtonType,
      buttonIndex = this.ButtonIndex,
    };
  }

  public sealed record ContinuousKnobMidiBindingView(
    string? BindingName,
    int KnobIndex,
    string? ConfigPropertyName,
    double StartValue,
    double EndValue
  ) : IMidiBindingView {
    public int BindingType => 1;
    public IMidiBindingConfig ToConfig() =>
      new ContinuousKnobMidiBindingConfig {
        BindingName = this.BindingName,
        knobIndex = this.KnobIndex,
        configPropertyName = this.ConfigPropertyName,
        startValue = this.StartValue,
        endValue = this.EndValue,
      };
  }

  public sealed record DiscreteKnobMidiBindingView(
    string? BindingName,
    int KnobIndex,
    string? ConfigPropertyName,
    int NumPossibleValues
  ) : IMidiBindingView {
    public int BindingType => 2;
    public IMidiBindingConfig ToConfig() => new DiscreteKnobMidiBindingConfig {
      BindingName = this.BindingName,
      knobIndex = this.KnobIndex,
      configPropertyName = this.ConfigPropertyName,
      numPossibleValues = this.NumPossibleValues,
    };
  }

  public sealed record DiscreteLogarithmicKnobMidiBindingView(
    string? BindingName,
    int KnobIndex,
    string? ConfigPropertyName,
    int NumPossibleValues,
    double StartValue
  ) : IMidiBindingView {
    public int BindingType => 3;
    public IMidiBindingConfig ToConfig() =>
      new DiscreteLogarithmicKnobMidiBindingConfig {
        BindingName = this.BindingName,
        knobIndex = this.KnobIndex,
        configPropertyName = this.ConfigPropertyName,
        numPossibleValues = this.NumPossibleValues,
        startValue = this.StartValue,
      };
  }

  public sealed record AdsrLevelDriverMidiBindingView(
    string? BindingName,
    int IndexRangeStart
  ) : IMidiBindingView {
    public int BindingType => 4;
    public IMidiBindingConfig ToConfig() =>
      new AdsrLevelDriverMidiBindingConfig {
        BindingName = this.BindingName,
        indexRangeStart = this.IndexRangeStart,
      };
  }

  public sealed record MidiPresetView(
    int Id,
    string? Name,
    ImmutableArray<IMidiBindingView> Bindings
  ) {
    public static MidiPresetView FromPreset(MidiPreset preset) {
      var bindings = ImmutableArray.CreateBuilder<IMidiBindingView>(
        preset.Bindings?.Count ?? 0);
      if (preset.Bindings != null) {
        foreach (IMidiBindingConfig binding in preset.Bindings) {
          IMidiBindingView? view = FromBinding(binding);
          if (view != null) {
            bindings.Add(view);
          }
        }
      }
      return new MidiPresetView(
        preset.id, preset.Name, bindings.MoveToImmutable());
    }

    public MidiPreset ToPreset() {
      var bindings = new List<IMidiBindingConfig>(this.Bindings.Length);
      foreach (IMidiBindingView binding in this.Bindings) {
        if (binding != null) {
          bindings.Add(binding.ToConfig());
        }
      }
      return new MidiPreset {
        id = this.Id,
        Name = this.Name,
        Bindings = bindings,
      };
    }

    public static ImmutableDictionary<int, MidiPresetView> Compile(
      IReadOnlyDictionary<int, MidiPreset> presets
    ) {
      if (presets == null || presets.Count == 0) {
        return ImmutableDictionary<int, MidiPresetView>.Empty;
      }
      var result = ImmutableDictionary.CreateBuilder<int, MidiPresetView>();
      foreach (KeyValuePair<int, MidiPreset> pair in presets) {
        if (pair.Value != null) {
          result[pair.Key] = FromPreset(pair.Value);
        }
      }
      return result.ToImmutable();
    }

    private static IMidiBindingView? FromBinding(IMidiBindingConfig? binding) {
      return binding switch {
        null => null,
        TapTempoMidiBindingConfig value => new TapTempoMidiBindingView(
          value.BindingName, value.buttonType, value.buttonIndex),
        ContinuousKnobMidiBindingConfig value =>
          new ContinuousKnobMidiBindingView(
            value.BindingName, value.knobIndex, value.configPropertyName,
            value.startValue, value.endValue),
        DiscreteKnobMidiBindingConfig value =>
          new DiscreteKnobMidiBindingView(
            value.BindingName, value.knobIndex, value.configPropertyName,
            value.numPossibleValues),
        DiscreteLogarithmicKnobMidiBindingConfig value =>
          new DiscreteLogarithmicKnobMidiBindingView(
            value.BindingName, value.knobIndex, value.configPropertyName,
            value.numPossibleValues, value.startValue),
        AdsrLevelDriverMidiBindingConfig value =>
          new AdsrLevelDriverMidiBindingView(
            value.BindingName, value.indexRangeStart),
        _ => throw new System.InvalidOperationException(
          "Unsupported MIDI binding type: " + binding.GetType().FullName),
      };
    }
  }
}
