using System.Collections.Generic;
using Spectrum.Base;
using System.ComponentModel;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace Spectrum {

  public class SpectrumConfiguration : Configuration {

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
      this.midiLog.PropertyChanged += MidiLogPropertyChanged;
      this.beatBroadcaster = new BeatBroadcaster(this);
    }

    private void ColorPalettePropertyChanged(object sender, PropertyChangedEventArgs e) {
      PropertyChangedEventArgs forwardedEvent =
        new PropertyChangedEventArgs("colorPalette." + e.PropertyName);
      this.PropertyChanged(this, forwardedEvent);
    }

    private void MidiLogPropertyChanged(object sender, PropertyChangedEventArgs e) {
      this.PropertyChanged(this, new PropertyChangedEventArgs("midiLog"));
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

    // Whenever adding one of these, also update MainWindow.configPropertiesIgnored
    // to avoid wasted disk I/O whenever these properties update
    private int _operatorFPS = 0;
    [XmlIgnore]
    public int operatorFPS {
      get => _operatorFPS;
      set => SetField(ref _operatorFPS, value);
    }
    private int _domeBeagleboneOPCFPS = 0;
    [XmlIgnore]
    public int domeBeagleboneOPCFPS {
      get => _domeBeagleboneOPCFPS;
      set => SetField(ref _domeBeagleboneOPCFPS, value);
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
    private int _domeVolumeAnimationSize = 4;
    public int domeVolumeAnimationSize {
      get => _domeVolumeAnimationSize;
      set => SetField(ref _domeVolumeAnimationSize, value);
    }
    private int _domeAutoFlashDelay = 100;
    public int domeAutoFlashDelay {
      get => _domeAutoFlashDelay;
      set => SetField(ref _domeAutoFlashDelay, value);
    }
    private double _domeVolumeRotationSpeed = 1.0;
    public double domeVolumeRotationSpeed {
      get => _domeVolumeRotationSpeed;
      set => SetField(ref _domeVolumeRotationSpeed, value);
    }
    private double _domeGradientSpeed = 1.0;
    public double domeGradientSpeed {
      get => _domeGradientSpeed;
      set => SetField(ref _domeGradientSpeed, value);
    }
    private int _domeTestPattern = 0;
    public int domeTestPattern {
      get => _domeTestPattern;
      set => SetField(ref _domeTestPattern, value);
    }
    private int _domeActiveVis = 0;
    public int domeActiveVis {
      get => _domeActiveVis;
      set => SetField(ref _domeActiveVis, value);
    }
    // Left null by default (not a pre-filled list): XSerializer deserializes a
    // collection property by calling IList.Add on the *existing* instance, so a
    // non-null default would double up the persisted entries on load. A null /
    // empty stack is synthesized from domeActiveVis in MainWindow.LoadConfig.
    // Persisted (no [XmlIgnore]), so it is NOT in configPropertiesIgnored.
    private List<DomeLayerSettings> _domeLayerStack = null;
    public List<DomeLayerSettings> domeLayerStack {
      get => _domeLayerStack;
      set => SetField(ref _domeLayerStack, value);
    }
    // Left null by default (rather than a pre-filled identity array): XSerializer
    // deserializes an array property by calling IList.Add on the *existing*
    // instance, and a non-null array is fixed-size, so a pre-initialized default
    // throws NotSupportedException ("Collection was of a fixed size") on load.
    // LEDDomeOutput.RebuildCableMapping already treats null as the identity
    // mapping, so a null default is equivalent to the legacy hard-coded wiring.
    private int[] _domeCableMapping = null;
    public int[] domeCableMapping {
      get => _domeCableMapping;
      set => SetField(ref _domeCableMapping, value);
    }
    // Transient calibration UI state (see Configuration). Not persisted, and
    // listed in MainWindow.configPropertiesIgnored so toggling them mid-
    // calibration doesn't churn the config file.
    private bool _domeCalibrationActive = false;
    [XmlIgnore]
    public bool domeCalibrationActive {
      get => _domeCalibrationActive;
      set => SetField(ref _domeCalibrationActive, value);
    }
    private int _domeCalibrationCableIndex = -1;
    [XmlIgnore]
    public int domeCalibrationCableIndex {
      get => _domeCalibrationCableIndex;
      set => SetField(ref _domeCalibrationCableIndex, value);
    }
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
    private double _domeTwinkleDensity = 0;
    public double domeTwinkleDensity {
      get => _domeTwinkleDensity;
      set => SetField(ref _domeTwinkleDensity, value);
    }
    private double _domeRippleCDStep = 1;
    public double domeRippleCDStep {
      get => _domeRippleCDStep;
      set => SetField(ref _domeRippleCDStep, value);
    }
    private double _domeRippleStep = 1;
    public double domeRippleStep {
      get => _domeRippleStep;
      set => SetField(ref _domeRippleStep, value);
    }
    private int _domeRadialEffect = 0;
    public int domeRadialEffect {
      get => _domeRadialEffect;
      set => SetField(ref _domeRadialEffect, value);
    }
    private double _domeRadialSize = 0.1;
    public double domeRadialSize {
      get => _domeRadialSize;
      set => SetField(ref _domeRadialSize, value);
    }
    private int _domeRadialFrequency = 1;
    public int domeRadialFrequency {
      get => _domeRadialFrequency;
      set => SetField(ref _domeRadialFrequency, value);
    }
    private double _domeRadialCenterAngle = 0.0;
    public double domeRadialCenterAngle {
      get => _domeRadialCenterAngle;
      set => SetField(ref _domeRadialCenterAngle, value);
    }
    private double _domeRadialCenterDistance = 0.0;
    public double domeRadialCenterDistance {
      get => _domeRadialCenterDistance;
      set => SetField(ref _domeRadialCenterDistance, value);
    }
    private double _domeRadialCenterSpeed = 0.0;
    public double domeRadialCenterSpeed {
      get => _domeRadialCenterSpeed;
      set => SetField(ref _domeRadialCenterSpeed, value);
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
    private ObservableMidiLog _midiLog = new ObservableMidiLog();
    [XmlIgnore]
    public ObservableMidiLog midiLog {
      get => _midiLog;
      set => SetField(ref _midiLog, value);
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

    // This probably should not be here...
    [XmlIgnore]
    public BeatBroadcaster beatBroadcaster { get; set; }

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

    // Whenever adding one of these, also update MainWindow.configPropertiesIgnored
    // to avoid wasted disk I/O whenever these properties update
    [XmlIgnore]
    public ConcurrentQueue<DomeLEDCommand> domeCommandQueue { get; } =
      new ConcurrentQueue<DomeLEDCommand>();

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
    private bool _orientationShowContours = false;
    public bool orientationShowContours {
      get => _orientationShowContours;
      set => SetField(ref _orientationShowContours, value);
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
