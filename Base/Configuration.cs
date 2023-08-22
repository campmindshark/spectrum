using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;

namespace Spectrum.Base {

  /**
   * A reference to a single shared Configuration is passed around all over
   * the Spectrum projects. It is updated by the user through the UI.
   */
  public interface Configuration : INotifyPropertyChanged {

    // The unique index identifying the audio device we are listening to
    int audioDeviceIndex { get; set; }
    string audioDeviceID { get; set; }

    bool domeEnabled { get; set; }
    bool midiInputEnabled { get; set; }
    bool barEnabled { get; set; }
    bool stageEnabled { get; set; }

    // If this is true, we will poll the Un4seen APIs in a thread separate to
    // the one running the visualizers. If it is false, a single thread will
    // first poll the Un4seen APIs and then run the visualizers.
    bool midiInputInSeparateThread { get; set; }
    bool domeOutputInSeparateThread { get; set; }
    bool barOutputInSeparateThread { get; set; }
    bool stageOutputInSeparateThread { get; set; }

    int operatorFPS { get; set; }
    int domeBeagleboneOPCFPS { get; set; }
    int barBeagleboneOPCFPS { get; set; }
    int stageBeagleboneOPCFPS { get; set; }


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

    // Dome Visualizer settings
    // TODO: move "volume" settings down here?
    int domeRadialEffect { get; set; }
    double domeGlobalFadeSpeed { get; set; }
    double domeGlobalHueSpeed { get; set; }
    double domeTwinkleDensity { get; set; }
    double domeRippleCDStep { get; set; }
    double domeRippleStep { get; set; }
    double domeRadialSize { get; set; }
    int domeRadialFrequency { get; set; }
    double domeRadialCenterAngle { get; set; }
    double domeRadialCenterDistance { get; set; }
    double domeRadialCenterSpeed { get; set; }

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

    // 0 = human, 1 = Madmom, 2 = Ableton Link
    int beatInput { get; set; }
    bool humanLinkOutput { get; set; }
    bool madmomLinkOutput { get; set; }

    int orientationDeviceSpotlight { get; set; }
    bool orientationCalibrate { get; set; }

    bool orientationShowContours { get; set; }
    bool orientationSphereTopology { get; set; }
    double orientationPlanetVisualSize { get; set; }
    int orientationPlanetSpawnNumber { get; set; }
    bool orientationPlanetSpawn { get; set; }
    bool orientationPlanetClear { get; set; }
    double orientationDomeG { get; set; }
    double orientationWandG { get; set; }
    double orientationFriction { get; set; }
  }

}
