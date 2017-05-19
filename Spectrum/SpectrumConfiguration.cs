using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;
using PropertyChanged;
using System.ComponentModel;
using System.Collections.Concurrent;
using System.Xml.Serialization;

namespace Spectrum {

  public class SpectrumConfiguration : Configuration {

    public event PropertyChangedEventHandler PropertyChanged;

    public int audioDeviceIndex { get; set; } = -1;

    public bool huesEnabled { get; set; } = false;
    public bool ledBoardEnabled { get; set; } = false;
    public bool domeEnabled { get; set; } = false;
    public bool midiInputEnabled { get; set; } = false;
    public bool whyFireEnabled { get; set; } = false;
    public bool barEnabled { get; set; } = false;

    public bool audioInputInSeparateThread { get; set; } = false;
    public bool huesOutputInSeparateThread { get; set; } = false;
    public bool ledBoardOutputInSeparateThread { get; set; } = false;
    public bool midiInputInSeparateThread { get; set; } = false;
    public bool domeOutputInSeparateThread { get; set; } = false;
    public bool whyFireOutputInSeparateThread { get; set; } = false;
    public bool barOutputInSeparateThread { get; set; } = false;

    [XmlIgnore]
    public int operatorFPS { get; set; } = 0;
    [XmlIgnore]
    public int domeTeensyFPS1 { get; set; } = 0;
    [XmlIgnore]
    public int domeTeensyFPS2 { get; set; } = 0;
    [XmlIgnore]
    public int domeTeensyFPS3 { get; set; } = 0;
    [XmlIgnore]
    public int domeTeensyFPS4 { get; set; } = 0;
    [XmlIgnore]
    public int domeTeensyFPS5 { get; set; } = 0;
    [XmlIgnore]
    public int domeBeagleboneOPCFPS { get; set; } = 0;
    [XmlIgnore]
    public int domeBeagleboneCAMPFPS { get; set; } = 0;
    [XmlIgnore]
    public int boardTeensyFPS { get; set; } = 0;
    [XmlIgnore]
    public int boardBeagleboneOPCFPS { get; set; } = 0;
    [XmlIgnore]
    public int boardBeagleboneCAMPFPS { get; set; } = 0;
    [XmlIgnore]
    public int barTeensyFPS { get; set; } = 0;
    [XmlIgnore]
    public int barBeagleboneOPCFPS { get; set; } = 0;
    [XmlIgnore]
    public int barBeagleboneCAMPFPS { get; set; } = 0;

    // 0 - Teensy, 1 - Beaglebone via OPC, 2 - Beaglebone via CAMP
    public int boardHardwareSetup { get; set; } = 0;
    public string boardBeagleboneOPCAddress { get; set; } = "";
    public string boardBeagleboneCAMPAddress { get; set; } = "";
    public string boardTeensyUSBPort { get; set; } = "COM4";
    public int boardRowLength { get; set; } = 30;
    public int boardRowsPerStrip { get; set; } = 5;
    public double boardBrightness { get; set; } = 0.1;

    // 0 - Teensy, 1 - Beaglebone via OPC, 2 - Beaglebone via CAMP
    public int barHardwareSetup { get; set; } = 0;
    public string barBeagleboneOPCAddress { get; set; } = "";
    public string barBeagleboneCAMPAddress { get; set; } = "";
    public string barTeensyUSBPort { get; set; } = "";
    public bool barSimulationEnabled { get; set; } = false;
    public int barInfinityWidth { get; set; } = 0;
    public int barInfinityLength { get; set; } = 0;
    public int barRunnerLength { get; set; } = 0;
    public double barBrightness { get; set; } = 0.1;

    // 0 - 5 Teensies, 1 - Beaglebone via OPC, 2 - Beaglebone via CAMP
    public int domeHardwareSetup { get; set; } = 0;
    public string domeTeensyUSBPort1 { get; set; } = null;
    public string domeTeensyUSBPort2 { get; set; } = null;
    public string domeTeensyUSBPort3 { get; set; } = null;
    public string domeTeensyUSBPort4 { get; set; } = null;
    public string domeTeensyUSBPort5 { get; set; } = null;
    public string domeBeagleboneOPCAddress { get; set; } = "";
    public string domeBeagleboneCAMPAddress { get; set; } = "";
    public bool domeSimulationEnabled { get; set; } = false;
    public double domeMaxBrightness { get; set; } = 0.5;
    public double domeBrightness { get; set; } = 0.1;
    public int domeVolumeAnimationSize { get; set; } = 2;
    public int domeAutoFlashDelay { get; set; } = 100;
    public double domeVolumeRotationSpeed { get; set; } = 1.0;
    public double domeGradientSpeed { get; set; } = 1.0;
    public int domeSkipLEDs { get; set; } = 0;

    // 0 - None, 1 - Flash colors by strut, 2 - Iterate through struts
    public int domeTestPattern { get; set; } = 0;

    public int hueDelay { get; set; } = 125;
    public bool hueIdleOnSilent { get; set; } = true;

    public bool lightsOff { get; set; } = false;
    public bool redAlert { get; set; } = false;
    public bool controlLights { get; set; } = true;
    public int brighten { get; set; } = 0;
    public int colorslide { get; set; } = 0;
    public int sat { get; set; } = 0;

    public double peakC { get; set; } = .800;
    public double dropQ { get; set; } = .025;
    public double dropT { get; set; } = .075;
    public double kickQ { get; set; } = 1;
    public double kickT { get; set; } = 0;
    public double snareQ { get; set; } = 1;
    public double snareT { get; set; } = .5;

    public string hueURL { get; set; }
      = "http://192.168.1.26/api/161d04c425fa45e293386cf241a26bf/";
    public int[] hueIndices { get; set; } = new int[] { 2, 1, 4, 5, 6 };

    public int midiDeviceIndex { get; set; } = -1;

    public string whyFireURL { get; set; } = "http://why.fire/WhyService.svc/Effects/";

    // This probably should not be here...
    [XmlIgnore, DoNotNotify]
    public BeatBroadcaster beatBroadcaster { get; set; } = new BeatBroadcaster();
    public LEDColorPalette colorPalette { get; set; } = new LEDColorPalette();

    // Excuse in Configuration interface
    [XmlIgnore, DoNotNotify]
    public ConcurrentQueue<DomeLEDCommand> domeCommandQueue { get; } =
      new ConcurrentQueue<DomeLEDCommand>();
    [XmlIgnore, DoNotNotify]
    public ConcurrentQueue<BarLEDCommand> barCommandQueue { get; } =
      new ConcurrentQueue<BarLEDCommand>();

    // The rest is not on Configuration
    // Just convenience properties for data binding

    public bool hueOverrideIsCustom { get; set; } = false;

    public bool hueOverrideIsDisabled {
      get {
        return this.controlLights
          && !this.hueOverrideIsCustom
          && !this.lightsOff
          && !this.redAlert;
      }
    }

    // This is all annoyingly UI-specific stuff
    public int hueOverrideIndex {
      get {
        if (!this.controlLights && this.hueOverrideIsCustom) {
          return 4;
        } else if (!this.controlLights) {
          return 1;
        } else if (this.lightsOff) {
          return 2;
        } else if (this.redAlert) {
          return 3;
        } else {
          return 0;
        }
      }
      set {
        this.controlLights = value != 1 && value != 4;
        this.lightsOff = value == 2;
        this.redAlert = value == 3;
        this.hueOverrideIsCustom = value == 4;
        if (value == 1) {
          this.brighten = 0;
          this.colorslide = 0;
          this.sat = 0;
        }
      }
    }

  }

}