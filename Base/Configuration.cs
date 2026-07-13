using System.Collections.Generic;
using System.ComponentModel;

namespace Spectrum.Base {

  /**
   * A reference to a single shared Configuration is passed around all over
   * the Spectrum projects. It is updated by the user through the UI (native
   * GUI, MIDI bindings, or the web gateway).
   *
   * This interface carries persisted settings only. Live runtime state and
   * services live with their owners and are exposed by the Operator: the
   * BeatBroadcaster tempo service, FPS counters on RuntimeTelemetry, the MIDI
   * log on MidiInput, the dome-mapping calibration selection on
   * DomeCalibrationState, and the simulator frame queue on LEDDomeOutput
   * itself. Per-visualizer tuning lives in each dome layer's
   * DomeLayerSettings.Params bag inside domeLayerStack.
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

    string domeBeagleboneOPCAddress { get; set; }

    // Configuration params for the dome
    bool domeSimulationEnabled { get; set; }
    // Startup feature flag for the browser dome simulator. When false the web
    // host does not map its geometry or WebSocket endpoints at all.
    bool webDomeSimulatorEnabled { get; set; }
    double domeMaxBrightness { get; set; }
    double domeBrightness { get; set; }
    // 0 - None, 1 - Flash colors by strut, 2 - Iterate through struts, 3 - Strip test
    int domeTestPattern { get; set; }

    // The dome's compositing stack, index 0 = background (bottom), last = front.
    // Persisted. Writers replace the whole list (snapshot swap) so PropertyChanged
    // fires and the operator thread never observes a mid-mutation list. A missing
    // / invalid value is synthesized on config load (LegacyLayerParamMigration,
    // which also reads the retired domeActiveVis selector out of old files).
    List<DomeLayerSettings> domeLayerStack { get; set; }

    // Dome cable mapping. The dome is wired as 10 cables (5 control boxes, each
    // with two ethernet cables: A = strands 0-3, B = strands 4-7), identified by
    // box*2 + half (half 0 = A, 1 = B). domeCableMapping[c] records, for each
    // controller cable c, the physical dome endpoint (same box*2 + half labeling
    // under the hard-coded layout) that actually lights when c is driven.
    // Identity {0..9} = the legacy hard-coded wiring. Discovered by the "Set
    // Dome Mapping" calibration window.
    int[] domeCableMapping { get; set; }

    // Cross-layer visual state: every stack layer applies the same fade /
    // hue-rotate speed to its own buffer. Per-visualizer tuning does NOT belong
    // here — it lives in each layer's DomeLayerSettings.Params bag (see
    // DomeLayerSettings.ParamsFor; LegacyLayerParamMigration seeds the bags
    // from configs that predate the move).
    double domeGlobalFadeSpeed { get; set; }
    double domeGlobalHueSpeed { get; set; }

    // Per-layer manual-fire counters (docs/triggers.md): keyed by
    // DomeLayerSettings.VisualizerKey / LayerKey, bumped by native/web Fire
    // buttons. A monotonic counter rather than a bool so two clients firing
    // concurrently never race over who clears a flag — LayerTrigger just
    // edge-detects "the count changed since I last looked."
    Dictionary<string, int> domeLayerFireCounters { get; set; }

    // Per-layer manual-clear counters, exactly parallel to domeLayerFireCounters:
    // keyed by LayerKey, bumped by the native Clear button. A layer that carries
    // accumulated live state (e.g. Shooting Star's in-flight dots) edge-detects a
    // bump and drops that state — an escape hatch when a layer is dragging the
    // frame rate. Same monotonic-counter rationale as the fire counters.
    Dictionary<string, int> domeLayerClearCounters { get; set; }

    // Named snapshots of the dome look (layer stack + the two globals above),
    // saved/recalled by the VJ. See DomeScene and SceneService. Null by default,
    // following the same null-by-default XSerializer rule as domeLayerStack /
    // domeCableMapping: a non-null initializer double-adds entries on deserialize.
    // Old config files load with domeScenes == null, treated as empty — no
    // migration needed.
    List<DomeScene> domeScenes { get; set; }

    // Named snapshots of one palette bank (eight gradient pairs), saved/recalled
    // by the VJ independently of scenes. Bank-agnostic: Save snapshots the bank
    // currently selected in the palette editor and Apply loads into it. See
    // DomePalette and PaletteService. Null by default, following the same
    // null-by-default XSerializer rule as domeScenes / domeLayerStack: a non-null
    // initializer double-adds entries on deserialize.
    List<DomePalette> domePalettes { get; set; }

    // maps from device ID to preset ID
    bool vjHUDEnabled { get; set; }
    Dictionary<int, int> midiDevices { get; set; }
    Dictionary<int, MidiPreset> midiPresets { get; set; }

    Dictionary<string, ILevelDriverPreset> levelDriverPresets { get; set; }
    Dictionary<int, string> channelToAudioLevelDriverPreset { get; set; }
    Dictionary<int, string> channelToMidiLevelDriverPreset { get; set; }

    // 64 slots = 8 palette banks of 8 gradient pairs. Each palette-consuming
    // dome layer picks its bank via its "palette" param (DomeLayerSettings);
    // the editor edits one bank at a time.
    LEDColorPalette colorPalette { get; set; }
    // Vestigial: the old global bank selector. No longer read or written (bank
    // selection is per-layer now); kept only so old config files still
    // deserialize without error.
    int colorPaletteIndex { get; set; }
    double flashSpeed { get; set; }

    // 0 = human, 1 = Madmom, 2 = Pro DJ Link
    int beatInput { get; set; }

    int orientationDeviceSpotlight { get; set; }
    bool orientationCalibrate { get; set; }

    // COM port of the USB-CDC ESP-NOW wand receiver, "" = no serial input.
    // The receiver reacts live to changes; not in configPropertiesToRebootOn.
    string wandSerialPort { get; set; }
  }
}
