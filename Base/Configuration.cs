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
    string audioDeviceID { get; set; }

    bool huesEnabled { get; set; }
    bool ledBoardEnabled { get; set; }
    bool domeEnabled { get; set; }
    bool midiInputEnabled { get; set; }
    bool barEnabled { get; set; }
    bool stageEnabled { get; set; }

    // If this is true, we will poll the Un4seen APIs in a thread separate to
    // the one running the visualizers. If it is false, a single thread will
    // first poll the Un4seen APIs and then run the visualizers.
    bool huesOutputInSeparateThread { get; set; }
    bool ledBoardOutputInSeparateThread { get; set; }
    bool midiInputInSeparateThread { get; set; }
    bool domeOutputInSeparateThread { get; set; }
    bool barOutputInSeparateThread { get; set; }
    bool stageOutputInSeparateThread { get; set; }

    int operatorFPS { get; set; }
    int domeBeagleboneOPCFPS { get; set; }
    int boardBeagleboneOPCFPS { get; set; }
    int barBeagleboneOPCFPS { get; set; }
    int stageBeagleboneOPCFPS { get; set; }

    string boardBeagleboneOPCAddress { get; set; }

    int boardRowLength { get; set; }
    int boardRowsPerStrip { get; set; }
    // Brightness of the LED Board
    double boardBrightness { get; set; }

    string barBeagleboneOPCAddress { get; set; }
    bool barSimulationEnabled { get; set; }
    // Dimensions (in terms of pixel count)
    int barInfinityWidth { get; set; }
    int barInfinityLength { get; set; }
    int barRunnerLength { get; set; }
    double barBrightness { get; set; }
    // 0 - None, 1 - Flash colors
    int barTestPattern { get; set; }

    string stageBeagleboneOPCAddress { get; set; }
    bool stageSimulationEnabled { get; set; }
    // 48 individual side lengths (in LED count)
    int[] stageSideLengths { get; set; }
    // Brightness of the stage
    double stageBrightness { get; set; }
    // 0 - None, 1 - Flash colors
    int stageTestPattern { get; set; }
    // Config params for animations
    double stageTracerSpeed { get; set; }

    string domeBeagleboneOPCAddress { get; set; }

    // Configuration params for the dome
    bool domeSimulationEnabled { get; set; }
    double domeMaxBrightness { get; set; }
    double domeBrightness { get; set; }
    int domeVolumeAnimationSize { get; set; }
    int domeAutoFlashDelay { get; set; }
    double domeVolumeRotationSpeed { get; set; }
    double domeGradientSpeed { get; set; }
    int domeSkipLEDs { get; set; }
    // 0 - None, 1 - Flash colors by strut, 2 - Iterate through struts, 3 - Strip test
    int domeTestPattern { get; set; }
    int domeActiveVis { get; set; }

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

    // maps from device ID to preset ID
    bool vjHUDEnabled { get; set; }
    Dictionary<int, int> midiDevices { get; set; }
    Dictionary<int, MidiPreset> midiPresets { get; set; }
    ObservableMidiLog midiLog { get; set; }

    Dictionary<string, ILevelDriverPreset> levelDriverPresets { get; set; }
    Dictionary<int, string> channelToAudioLevelDriverPreset { get; set; }
    Dictionary<int, string> channelToMidiLevelDriverPreset { get; set; }

    // This probably should not be here...
    BeatBroadcaster beatBroadcaster { get; set; }
    LEDColorPalette colorPalette { get; set; }
    int colorPaletteIndex { get; set; }
    double flashSpeed { get; set; }

    // You might look at this and be disgusted. Yes, I am kinda violating the
    // whole organizing principle here, but the UI needs to know what's going on
    // with LEDDomeOutput, and the config is the only reference they share.
    ConcurrentQueue<DomeLEDCommand> domeCommandQueue { get; }
    ConcurrentQueue<BarLEDCommand> barCommandQueue { get; }
    ConcurrentQueue<StageLEDCommand> stageCommandQueue { get; }

  }

}
