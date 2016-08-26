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
    public bool midiInputEnabled { get; set; } = false;

    public bool audioInputInSeparateThread { get; set; } = false;
    public bool huesOutputInSeparateThread { get; set; } = false;
    public bool ledBoardOutputInSeparateThread { get; set; } = false;
    public bool midiInputInSeparateThread { get; set; } = false;
    public bool domeOutputInSeparateThread { get; set; } = false;

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

    public string teensyUSBPort { get; set; } = "COM4";
    public int teensyRowLength { get; set; } = 30;
    public int teensyRowsPerStrip { get; set; } = 5;
    public double ledBoardBrightness { get; set; } = 0.1;

    public int midiDeviceIndex { get; set; } = -1;

    public bool domeEnabled { get; set; } = false;
    public bool domeSimulationEnabled { get; set; } = false;
    public string domeTeensyUSBPort1 { get; set; } = null;
    public string domeTeensyUSBPort2 { get; set; } = null;
    public string domeTeensyUSBPort3 { get; set; } = null;
    public string domeTeensyUSBPort4 { get; set; } = null;
    public string domeTeensyUSBPort5 { get; set; } = null;
    public double domeMaxBrightness { get; set; } = 0.1;
    public int domeVolumeAnimationSize { get; set; } = 2;
    public LEDColorPalette domeColorPalette { get; set; } =
      new LEDColorPalette();
    public int domeAutoFlashDelay { get; set; } = 100;

    // This probably should not be here...
    [XmlIgnore, DoNotNotify]
    public BeatBroadcaster domeBeatBroadcaster { get; set; } =
      new BeatBroadcaster();

    // Excuse in Configuration interface
    [XmlIgnore, DoNotNotify]
    public ConcurrentQueue<LEDCommand> domeCommandQueue { get; } =
      new ConcurrentQueue<LEDCommand>();

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
