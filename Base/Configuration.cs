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

    // If this is true, we will poll the input/output APIs in a thread separate
    // to the one running the visualizers. If it is false, a single thread will
    // first poll the input/output APIs and then run the visualizers.
    bool midiInputInSeparateThread { get; set; }
    bool domeOutputInSeparateThread { get; set; }

    int operatorFPS { get; set; }
    int domeBeagleboneOPCFPS { get; set; }

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
    // Deprecated single-visualizer selector, kept as a write-only compatibility
    // alias: writing it replaces layer 0 of domeLayerStack with that visualizer
    // (see the alias subscriber in Operator). The layer stack below is the real
    // state that drives the compositor.
    int domeActiveVis { get; set; }

    // The dome's compositing stack, index 0 = background (bottom), last = front.
    // Persisted. Writers replace the whole list (snapshot swap) so PropertyChanged
    // fires and the operator thread never observes a mid-mutation list. A missing
    // / null value is migrated from domeActiveVis on config load.
    List<DomeLayerSettings> domeLayerStack { get; set; }

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

    // True while a consumer (the dome simulator window) is actually draining
    // domeCommandQueue. LEDDomeOutput gates enqueue on this in addition to
    // domeSimulationEnabled, so the queue can't grow without bound when the
    // flag is set with nobody reading it — e.g. flipped on over the web with
    // no simulator window open.
    bool domeCommandQueueHasConsumer { get; set; }

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
