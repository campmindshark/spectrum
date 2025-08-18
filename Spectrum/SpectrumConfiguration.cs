using System.Collections.Generic;
using Spectrum.Base;
using PropertyChanged;
using System.ComponentModel;
using System.Collections.Concurrent;
using System.Xml.Serialization;

namespace Spectrum {

  public class SpectrumConfiguration : Configuration {

    public event PropertyChangedEventHandler PropertyChanged;

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

    public int audioDeviceIndex { get; set; } = -1;
    public string audioDeviceID { get; set; } = null;

    public bool domeEnabled { get; set; } = false;
    public bool midiInputEnabled { get; set; } = false;
    public bool barEnabled { get; set; } = false;
    public bool stageEnabled { get; set; } = false;

    public bool midiInputInSeparateThread { get; set; } = false;
    public bool domeOutputInSeparateThread { get; set; } = false;
    public bool barOutputInSeparateThread { get; set; } = false;
    public bool stageOutputInSeparateThread { get; set; } = false;

    // Whenever adding one of these, also update MainWindow.configPropertiesIgnored
    // to avoid wasted disk I/O whenever these properties update
    [XmlIgnore]
    public int operatorFPS { get; set; } = 0;
    [XmlIgnore]
    public int domeBeagleboneOPCFPS { get; set; } = 0;
    [XmlIgnore]
    public int barBeagleboneOPCFPS { get; set; } = 0;
    [XmlIgnore]
    public int stageBeagleboneOPCFPS { get; set; } = 0;


    public string barBeagleboneOPCAddress { get; set; } = "";
    public bool barSimulationEnabled { get; set; } = false;
    public int barInfinityWidth { get; set; } = 0;
    public int barInfinityLength { get; set; } = 0;
    public int barRunnerLength { get; set; } = 0;
    public double barBrightness { get; set; } = 0.1;
    public int barTestPattern { get; set; } = 0;

    public string stageBeagleboneOPCAddress { get; set; } = "";
    public bool stageSimulationEnabled { get; set; } = false;
    public int[] stageSideLengths { get; set; } = null;
    public double stageBrightness { get; set; } = 0.1;
    public int stageTestPattern { get; set; } = 0;
    public double stageTracerSpeed { get; set; } = 1.0;

    public string domeBeagleboneOPCAddress { get; set; } = "";

    public bool domeSimulationEnabled { get; set; } = false;
    public double domeMaxBrightness { get; set; } = 0.5;
    public double domeBrightness { get; set; } = 0.1;
    public int domeVolumeAnimationSize { get; set; } = 4;
    public int domeAutoFlashDelay { get; set; } = 100;
    public double domeVolumeRotationSpeed { get; set; } = 1.0;
    public double domeGradientSpeed { get; set; } = 1.0;
    public int domeSkipLEDs { get; set; } = 0;
    public int domeTestPattern { get; set; } = 0;
    public int domeActiveVis { get; set; } = 0;
    public double domeGlobalFadeSpeed { get; set; } = 0;
    public double domeGlobalHueSpeed { get; set; } = 1;
    public double domeTwinkleDensity { get; set; } = 0;
    public double domeRippleCDStep { get; set; } = 1;
    public double domeRippleStep { get; set; } = 1;
    public int domeRadialEffect { get; set; } = 0;
    public double domeRadialSize { get; set; } = 0.1;
    public int domeRadialFrequency { get; set; } = 1;
    public double domeRadialCenterAngle { get; set; } = 0.0;
    public double domeRadialCenterDistance { get; set; } = 0.0;
    public double domeRadialCenterSpeed { get; set; } = 0.0;

    // maps from device ID to preset ID
    public bool vjHUDEnabled { get; set; } = false;
    public Dictionary<int, int> midiDevices { get; set; } = new Dictionary<int, int>();
    public Dictionary<int, MidiPreset> midiPresets { get; set; } = new Dictionary<int, MidiPreset>();
    [XmlIgnore]
    public ObservableMidiLog midiLog { get; set; } = new ObservableMidiLog();

    public Dictionary<string, ILevelDriverPreset> levelDriverPresets { get; set; } = new Dictionary<string, ILevelDriverPreset>();
    public Dictionary<int, string> channelToAudioLevelDriverPreset { get; set; } = new Dictionary<int, string>();
    public Dictionary<int, string> channelToMidiLevelDriverPreset { get; set; } = new Dictionary<int, string>();

    // This probably should not be here...
    [XmlIgnore, DoNotNotify]
    public BeatBroadcaster beatBroadcaster { get; set; }

    private LEDColorPalette _colorPalette = new LEDColorPalette();
    public LEDColorPalette colorPalette {
      get {
        return _colorPalette;
      }
      set {
        value.PropertyChanged += this.ColorPalettePropertyChanged;
        this._colorPalette = value;
      }
    }
    public int colorPaletteIndex { get; set; } = 0;
    public double flashSpeed { get; set; } = 0.0;

    // Whenever adding one of these, also update MainWindow.configPropertiesIgnored
    // to avoid wasted disk I/O whenever these properties update
    [XmlIgnore, DoNotNotify]
    public ConcurrentQueue<DomeLEDCommand> domeCommandQueue { get; } =
      new ConcurrentQueue<DomeLEDCommand>();
    [XmlIgnore, DoNotNotify]
    public ConcurrentQueue<BarLEDCommand> barCommandQueue { get; } =
      new ConcurrentQueue<BarLEDCommand>();
    [XmlIgnore, DoNotNotify]
    public ConcurrentQueue<StageLEDCommand> stageCommandQueue { get; } =
      new ConcurrentQueue<StageLEDCommand>();

    // 0 = human, 1 = Madmom, 2 = Ableton Link
    public int beatInput { get; set; } = 0;
    public bool humanLinkOutput { get; set; } = false;
    public bool madmomLinkOutput { get; set; } = false;

    public int orientationDeviceSpotlight { get; set; } = 0;
    public bool orientationCalibrate { get; set; } = false;
    public bool orientationShowContours { get; set; } = false;
  }

}
