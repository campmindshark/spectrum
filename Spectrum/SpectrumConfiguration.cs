using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;

namespace Spectrum {

  class SpectrumConfiguration : Configuration {

    public int audioDeviceIndex { get; set; } = -1;

    public bool audioInputInSeparateThread { get; set; } = false;
    public bool hueOutputInSeparateThread { get; set; } = true;
    public bool ledsOutputInSeparateThread { get; set; } = false;

    public bool lightsOff { get; set; } = false;
    public bool redAlert { get; set; } = false;
    public bool controlLights { get; set; } = true;
    public int brighten { get; set; } = 0;
    public int colorslide { get; set; } = 0;
    public int sat { get; set; } = 0;

    public float peakC { get; set; } = .800f;
    public float dropQ { get; set; } = .025f;
    public float dropT { get; set; } = .075f;
    public float kickQ { get; set; } = 1;
    public float kickT { get; set; } = 0;
    public float snareQ { get; set; } = 1;
    public float snareT { get; set; } = .5f;

    public string hueURL { get; set; }
      = "http://192.168.1.26/api/161d04c425fa45e293386cf241a26bf/";
    public int[] hueIndices { get; set; } = new int[] { 2, 1, 4, 5, 6 };

    public string teensyUSBPort { get; set; } = "COM3";
    public int teensyRowLength { get; set; } = 30;
    public int teensyRowsPerStrip { get; set; } = 5;

  }

}
