using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spectrum.Base {

  /**
   * A reference to a single shared Configuration is passed around all over
   * the Spectrum projects. It is updated by the user through the UI.
   */
  public interface Configuration {

    // If this is true, we will poll the Un4seen APIs in a thread separate to
    // the one running the visualizers. If it is false, a single thread will
    // first poll the Un4seen APIs and then run the visualizers.
    bool audioInputInSeparateThread { get; set; }
    // If this is true, we will update the Hues in a thread separate to the one
    // running the visualizers. If it is false, a single thread will first run
    // the visualizers and cache output commands, and then flush them.
    bool hueOutputInSeparateThread { get; set; }
    // If this is true, we will update the LEDs in a thread separate to the one
    // running the visualizers. If it is false, a single thread will first run
    // the visualizers and cache output commands, and then flush them.
    bool ledsOutputInSeparateThread { get; set; }

    // If lightsOff is true then we turn off the lights
    bool lightsOff { get; set; }
    // If redAlert is true then we just display red
    bool redAlert { get; set; }
    // If controlLights is false then we just display white
    bool controlLights { get; set; }
    // If controlLights is false, these can modify the color
    int brighten { get; set; }
    int colorslide { get; set; }
    int sat { get; set; }
    // These parameters come from the sliders in the UI and affect audio
    // processing
    float peakC { get; set; }
    float dropQ { get; set; }
    float dropT { get; set; }
    float kickQ { get; set; }
    float kickT { get; set; }
    float snareQ { get; set; }
    float snareT { get; set; }

    // The URL at which the Hue hub can be accessed
    string hueURL { get; set; }
    // The list of IDs of Hue bulbs, from left to right
    int[] hueIndices { get; set; }

    // The USB port where the Teensy is located
    string teensyUSBPort { get; set; }
    // Parameters to SquareTeensyOutput
    int teensyRowLength { get; set; }
    int teensyRowsPerStrip { get; set; }


  }

}
