using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spectrum.Base {

  /**
   * A reference to a single shared Configuration is passed around all over
   * the Spectrum projects. It is updated by the user through the UI.
   */
  public interface Configuration : INotifyPropertyChanged {

    // The unique index identifying the audio device we are listening to
    int audioDeviceIndex { get; set; }

    bool huesEnabled { get; set; }
    bool ledBoardEnabled { get; set; }
    bool midiInputEnabled { get; set; }

    // If this is true, we will poll the Un4seen APIs in a thread separate to
    // the one running the visualizers. If it is false, a single thread will
    // first poll the Un4seen APIs and then run the visualizers.
    bool audioInputInSeparateThread { get; set; }
    bool huesOutputInSeparateThread { get; set; }
    bool ledBoardOutputInSeparateThread { get; set; }
    bool midiInputInSeparateThread { get; set; }
    bool domeOutputInSeparateThread { get; set; }

    // This is the delay in milliseconds between consecutive commands we give
    // the Hue hub
    int hueDelay { get; set; }
    // If true, when the audio stream goes silent the Hues will "idle"
    bool hueIdleOnSilent { get; set; }

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
    double peakC { get; set; }
    double dropQ { get; set; }
    double dropT { get; set; }
    double kickQ { get; set; }
    double kickT { get; set; }
    double snareQ { get; set; }
    double snareT { get; set; }

    // The URL at which the Hue hub can be accessed
    string hueURL { get; set; }
    // The list of IDs of Hue bulbs, from left to right
    int[] hueIndices { get; set; }

    // The USB port where the Teensy is located (LED Board)
    string teensyUSBPort { get; set; }
    // Parameters to SquareTeensyOutput (LED Board)
    int teensyRowLength { get; set; }
    int teensyRowsPerStrip { get; set; }
    // Brightness of the LED Board
    double ledBoardBrightness { get; set; }

    // The index of the device representing the MIDI controller
    int midiDeviceIndex { get; set; }

    bool domeEnabled { get; set; }
    bool domeSimulationEnabled { get; set; }
    string domeTeensyUSBPort1 { get; set; }
    string domeTeensyUSBPort2 { get; set; }
    string domeTeensyUSBPort3 { get; set; }
    string domeTeensyUSBPort4 { get; set; }
    string domeTeensyUSBPort5 { get; set; }
    double domeMaxBrightness { get; set; }
    int domeVolumeAnimationSize { get; set; }
    LEDColorPalette domeColorPalette { get; set; }
    BeatBroadcaster domeBeatBroadcaster { get; set; }

    // You might look at this and be disgusted. Yes, I am kinda violating the
    // whole organizing principle here, but the UI needs to know what's going on
    // with LEDDomeOutput, and the config is the only reference they share.
    ConcurrentQueue<LEDCommand> domeCommandQueue { get; }

  }

}
