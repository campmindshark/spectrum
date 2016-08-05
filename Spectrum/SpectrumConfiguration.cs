using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;

namespace Spectrum {

  [Serializable]
  public class SpectrumConfiguration : Configuration {

    public int audioDeviceIndex { get; set; } = -1;

    public bool huesEnabled { get; set; } = false;
    public bool ledBoardEnabled { get; set; } = false;
    public bool midiInputEnabled { get; set; } = false;

    public bool audioInputInSeparateThread { get; set; } = false;
    public bool huesOutputInSeparateThread { get; set; } = false;
    public bool ledBoardOutputInSeparateThread { get; set; } = false;
    public bool midiInputInSeparateThread { get; set; } = false;

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

  }

}
