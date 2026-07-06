using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;

namespace Spectrum.Base {

  /**
   * A reference to a single shared Configuration is passed around all over
   * the Spectrum projects. It is updated by the user through the UI.
   */
  public interface Configuration : INotifyPropertyChanged {

    string audioDeviceID { get; set; }

    bool domeEnabled { get; set; }
    bool midiInputEnabled { get; set; }
    bool barEnabled { get; set; }

    // If this is true, we will poll the input/output APIs in a thread separate
    // to the one running the visualizers. If it is false, a single thread will
    // first poll the input/output APIs and then run the visualizers.
    bool midiInputInSeparateThread { get; set; }
    bool domeOutputInSeparateThread { get; set; }
    bool barOutputInSeparateThread { get; set; }

    int operatorFPS { get; set; }
    int domeBeagleboneOPCFPS { get; set; }
    int barBeagleboneOPCFPS { get; set; }


    string barBeagleboneOPCAddress { get; set; }
    bool barSimulationEnabled { get; set; }
    // Dimensions (in terms of pixel count)
    int barInfinityWidth { get; set; }
    int barInfinityLength { get; set; }
    int barRunnerLength { get; set; }
    double barBrightness { get; set; }
    // 0 - None, 1 - Flash colors
    int barTestPattern { get; set; }

    string domeBeagleboneOPCAddress { get; set; }

    // Configuration params for the dome
    bool domeSimulationEnabled { get; set; }
    double domeMaxBrightness { get; set; }
    double domeBrightness { get; set; }
    int domeVolumeAnimationSize { get; set; }
    int domeAutoFlashDelay { get; set; }
    double domeVolumeRotationSpeed { get; set; }
    double domeGradientSpeed { get; set; }
    // 0 - None, 1 - Flash colors by strut, 2 - Iterate through struts, 3 - Strip test
    int domeTestPattern { get; set; }
    int domeActiveVis { get; set; }

    // Dome cable mapping. The dome is wired as 10 cables (5 control boxes, each
    // with two ethernet cables: A = strands 0-3, B = strands 4-7), identified by
    // box*2 + half (half 0 = A, 1 = B). domeCableMapping[c] records, for each
    // controller cable c, the physical dome endpoint (same box*2 + half labeling
    // under the hard-coded layout) that actually lights when c is driven.
    // Identity {0..9} = the legacy hard-coded wiring. Discovered by the "Set
    // Dome Mapping" calibration window.
    int[] domeCableMapping { get; set; }
    // Transient state driving that calibration (not persisted): whether the
    // calibration is running, and which controller cable (box*2 + half, or -1
    // for all-off) the dome should currently be lighting.
    bool domeCalibrationActive { get; set; }
    int domeCalibrationCableIndex { get; set; }

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

    // 0 = human, 1 = Madmom, 2 = Pro DJ Link
    int beatInput { get; set; }

    int orientationDeviceSpotlight { get; set; }
    bool orientationCalibrate { get; set; }

    bool orientationShowContours { get; set; }

    // COM port of the USB-CDC ESP-NOW wand receiver, "" = no serial input.
    // The receiver reacts live to changes; not in configPropertiesToRebootOn.
    string wandSerialPort { get; set; }
  }
}
