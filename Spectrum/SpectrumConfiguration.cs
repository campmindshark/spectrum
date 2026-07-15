using System.Collections.Generic;
using Spectrum.Base;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Spectrum {

  public class SpectrumConfiguration : Configuration, ILayerStackSnapshotSource {

    public event PropertyChangedEventHandler PropertyChanged;

    private void SetField<T>(ref T field, T value,
        [CallerMemberName] string name = null) {
      if (EqualityComparer<T>.Default.Equals(field, value)) {
        return;
      }
      field = value;
      this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public SpectrumConfiguration() {
      this._colorPalette.PropertyChanged += ColorPalettePropertyChanged;
    }

    private void ColorPalettePropertyChanged(object sender, PropertyChangedEventArgs e) {
      PropertyChangedEventArgs forwardedEvent =
        new PropertyChangedEventArgs("colorPalette." + e.PropertyName);
      this.PropertyChanged?.Invoke(this, forwardedEvent);
    }

    private string _audioDeviceID = null;
    public string audioDeviceID {
      get => _audioDeviceID;
      set => SetField(ref _audioDeviceID, value);
    }

    private bool _domeEnabled = false;
    public bool domeEnabled {
      get => _domeEnabled;
      set => SetField(ref _domeEnabled, value);
    }
    private bool _midiInputEnabled = false;
    public bool midiInputEnabled {
      get => _midiInputEnabled;
      set => SetField(ref _midiInputEnabled, value);
    }

    private bool _midiInputInSeparateThread = false;
    public bool midiInputInSeparateThread {
      get => _midiInputInSeparateThread;
      set => SetField(ref _midiInputInSeparateThread, value);
    }
    private bool _domeOutputInSeparateThread = false;
    public bool domeOutputInSeparateThread {
      get => _domeOutputInSeparateThread;
      set => SetField(ref _domeOutputInSeparateThread, value);
    }

    private string _domeBeagleboneOPCAddress = "";
    public string domeBeagleboneOPCAddress {
      get => _domeBeagleboneOPCAddress;
      set => SetField(ref _domeBeagleboneOPCAddress, value);
    }

    private bool _domeSimulationEnabled = false;
    public bool domeSimulationEnabled {
      get => _domeSimulationEnabled;
      set => SetField(ref _domeSimulationEnabled, value);
    }
    private bool _webDomeSimulatorEnabled = true;
    public bool webDomeSimulatorEnabled {
      get => _webDomeSimulatorEnabled;
      set => SetField(ref _webDomeSimulatorEnabled, value);
    }
    private double _domeMaxBrightness = 0.5;
    public double domeMaxBrightness {
      get => _domeMaxBrightness;
      set => SetField(ref _domeMaxBrightness, value);
    }
    private double _domeBrightness = 0.1;
    public double domeBrightness {
      get => _domeBrightness;
      set => SetField(ref _domeBrightness, value);
    }
    private int _domeTestPattern = 0;
    public int domeTestPattern {
      get => _domeTestPattern;
      set => SetField(ref _domeTestPattern, value);
    }
    // Left null by default (not a pre-filled list): XSerializer deserializes a
    // collection property by calling IList.Add on the *existing* instance, so a
    // non-null default would double up the persisted entries on load.
    private List<DomeLayerSettings> _domeLayerStack = null;
    private static readonly LayerStackService layerStackService = new();
    private LayerStackSnapshot _domeLayerStackSnapshot =
      LayerStackSnapshot.Empty;
    public List<DomeLayerSettings> domeLayerStack {
      get => _domeLayerStack;
      set {
        List<DomeLayerSettings> published = value;
        if (NeedsLayerInstanceIds(value)) {
          (List<DomeLayerSettings> normalized, string error) =
            new LayerStackService().Normalize(value);
          if (error == null) {
            published = normalized;
          }
        }
        if (EqualityComparer<List<DomeLayerSettings>>.Default.Equals(
              this._domeLayerStack, published)) {
          return;
        }
        (LayerStackSnapshot snapshot, string snapshotError) =
          layerStackService.CreateSnapshot(published);
        if (snapshotError != null) {
          snapshot = LayerStackSnapshot.Empty;
        }
        this._domeLayerStack = published;
        Volatile.Write(ref this._domeLayerStackSnapshot, snapshot);
        this.PropertyChanged?.Invoke(
          this, new PropertyChangedEventArgs(nameof(this.domeLayerStack)));
      }
    }
    LayerStackSnapshot ILayerStackSnapshotSource.DomeLayerStackSnapshot =>
      Volatile.Read(ref this._domeLayerStackSnapshot);

    private static bool NeedsLayerInstanceIds(
      IReadOnlyList<DomeLayerSettings> layers
    ) {
      if (layers == null) {
        return false;
      }
      for (int i = 0; i < layers.Count; i++) {
        DomeLayerSettings layer = layers[i];
        if (layer != null && string.IsNullOrWhiteSpace(layer.InstanceId)) {
          return true;
        }
      }
      return false;
    }
    // Left null by default (rather than a pre-filled identity array): XSerializer
    // deserializes an array property by calling IList.Add on the *existing*
    // instance, and a non-null array is fixed-size, so a pre-initialized default
    // throws NotSupportedException ("Collection was of a fixed size") on load.
    // LEDDomeOutput.RebuildOutputMapping already treats null as the identity
    // mapping, so a null default is equivalent to the legacy hard-coded wiring.
    private int[] _domeCableMapping = null;
    public int[] domeCableMapping {
      get => _domeCableMapping;
      set => SetField(ref _domeCableMapping, value);
    }
    // Same null-by-default rule as domeCableMapping. Values map each physical
    // output port to the legacy strip path plugged into it; LEDDomeOutput falls
    // back to identity unless all values 0..7 occur exactly once.
    private int[] _domePortMapping = null;
    public int[] domePortMapping {
      get => _domePortMapping;
      set => SetField(ref _domePortMapping, value);
    }
    // Cross-layer visual state; per-visualizer tuning lives in each layer's
    // renderer parameter bag instead (see Configuration).
    private double _domeGlobalFadeSpeed = 0;
    public double domeGlobalFadeSpeed {
      get => _domeGlobalFadeSpeed;
      set => SetField(ref _domeGlobalFadeSpeed, value);
    }
    private double _domeGlobalHueSpeed = 1;
    public double domeGlobalHueSpeed {
      get => _domeGlobalHueSpeed;
      set => SetField(ref _domeGlobalHueSpeed, value);
    }
    // Left null by default (not a pre-filled list) for the same reason as
    // domeLayerStack: XSerializer deserializes a collection property by calling
    // IList.Add on the *existing* instance, so a non-null default would double up
    // the persisted scenes on load. Null reads as "no saved scenes."
    private List<DomeScene> _domeScenes = null;
    public List<DomeScene> domeScenes {
      get => _domeScenes;
      set => SetField(ref _domeScenes, value);
    }
    // Left null by default for the same reason as domeScenes: XSerializer
    // deserializes a collection property by calling IList.Add on the *existing*
    // instance, so a non-null default would double up the persisted palettes on
    // load. Null reads as "no saved palettes."
    private List<DomePalette> _domePalettes = null;
    public List<DomePalette> domePalettes {
      get => _domePalettes;
      set => SetField(ref _domePalettes, value);
    }
    // Non-null default, matching midiDevices/channelToMidiLevelDriverPreset
    // (Dictionary properties round-trip fine through XSerializer, unlike
    // List/array properties).
    private Dictionary<string, int> _domeLayerFireCounters =
      new Dictionary<string, int>();
    public Dictionary<string, int> domeLayerFireCounters {
      get => _domeLayerFireCounters;
      set => SetField(ref _domeLayerFireCounters, value);
    }

    // Parallel to _domeLayerFireCounters (see the Configuration interface): the
    // Clear button bumps these, a layer edge-detects the bump and drops its live
    // state. Non-null default for the same XSerializer round-trip reason.
    private Dictionary<string, int> _domeLayerClearCounters =
      new Dictionary<string, int>();
    public Dictionary<string, int> domeLayerClearCounters {
      get => _domeLayerClearCounters;
      set => SetField(ref _domeLayerClearCounters, value);
    }

    // maps from device ID to preset ID
    private bool _vjHUDEnabled = false;
    public bool vjHUDEnabled {
      get => _vjHUDEnabled;
      set => SetField(ref _vjHUDEnabled, value);
    }
    private Dictionary<int, int> _midiDevices = new Dictionary<int, int>();
    public Dictionary<int, int> midiDevices {
      get => _midiDevices;
      set => SetField(ref _midiDevices, value);
    }
    private Dictionary<int, MidiPreset> _midiPresets = new Dictionary<int, MidiPreset>();
    public Dictionary<int, MidiPreset> midiPresets {
      get => _midiPresets;
      set => SetField(ref _midiPresets, value);
    }
    private Dictionary<string, ILevelDriverPreset> _levelDriverPresets = new Dictionary<string, ILevelDriverPreset>();
    public Dictionary<string, ILevelDriverPreset> levelDriverPresets {
      get => _levelDriverPresets;
      set => SetField(ref _levelDriverPresets, value);
    }
    private Dictionary<int, string> _channelToAudioLevelDriverPreset = new Dictionary<int, string>();
    public Dictionary<int, string> channelToAudioLevelDriverPreset {
      get => _channelToAudioLevelDriverPreset;
      set => SetField(ref _channelToAudioLevelDriverPreset, value);
    }
    private Dictionary<int, string> _channelToMidiLevelDriverPreset = new Dictionary<int, string>();
    public Dictionary<int, string> channelToMidiLevelDriverPreset {
      get => _channelToMidiLevelDriverPreset;
      set => SetField(ref _channelToMidiLevelDriverPreset, value);
    }

    private LEDColorPalette _colorPalette = new LEDColorPalette();
    public LEDColorPalette colorPalette {
      get {
        return _colorPalette;
      }
      set {
        value.PropertyChanged += this.ColorPalettePropertyChanged;
        this._colorPalette = value;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(colorPalette)));
      }
    }
    private int _colorPaletteIndex = 0;
    public int colorPaletteIndex {
      get => _colorPaletteIndex;
      set => SetField(ref _colorPaletteIndex, value);
    }
    private double _flashSpeed = 0.0;
    public double flashSpeed {
      get => _flashSpeed;
      set => SetField(ref _flashSpeed, value);
    }

    // 0 = human, 1 = Madmom, 2 = Pro DJ Link
    private int _beatInput = 0;
    public int beatInput {
      get => _beatInput;
      set => SetField(ref _beatInput, value);
    }

    private int _orientationDeviceSpotlight = 0;
    public int orientationDeviceSpotlight {
      get => _orientationDeviceSpotlight;
      set => SetField(ref _orientationDeviceSpotlight, value);
    }
    private bool _orientationCalibrate = false;
    public bool orientationCalibrate {
      get => _orientationCalibrate;
      set => SetField(ref _orientationCalibrate, value);
    }
    // Initialized to "" (not left null) so both the receiver's IsNullOrEmpty
    // check and StringParameter.Coerce/Get have a real string, and a
    // spectrum_config.xml predating this property (which leaves the field
    // untouched on deserialize) still reads as "no serial input".
    private string _wandSerialPort = "";
    public string wandSerialPort {
      get => _wandSerialPort;
      set => SetField(ref _wandSerialPort, value);
    }
  }

}
