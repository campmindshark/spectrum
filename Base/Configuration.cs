using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.Immutable;

namespace Spectrum.Base {

  /**
   * A reference to a single shared Configuration is passed around the Spectrum
   * projects. Scalar values and cached immutable collection views are updated
   * by the owner thread through the native UI, MIDI bindings, or web gateway.
   *
   * This interface carries persisted settings only. Live runtime state and
   * services live with their owners and are exposed by the Operator: the
   * BeatBroadcaster tempo service, FPS counters on RuntimeTelemetry, the MIDI
   * log on MidiInput, the dome-mapping calibration selection on
   * DomeCalibrationState, and the simulator frame queue on LEDDomeOutput
   * itself. Per-visualizer tuning lives in each dome layer's
   * DomeLayerSettings parameter bags inside domeLayerStack.
   */
  public interface Configuration : INotifyPropertyChanged {

    string? audioDeviceID { get; set; }

    bool domeEnabled { get; set; }
    bool midiInputEnabled { get; set; }

    // If true, dome output is polled separately from the visualizer thread.
    bool domeOutputInSeparateThread { get; set; }

    string domeBeagleboneOPCAddress { get; set; }

    // Configuration params for the dome
    bool domeSimulationEnabled { get; set; }
    // Startup feature flag for the browser dome simulator. When false the web
    // host does not map its geometry or WebSocket endpoints at all.
    bool webDomeSimulatorEnabled { get; set; }
    double domeMaxBrightness { get; set; }
    double domeBrightness { get; set; }
    // 0 - None, 1 - Flash colors by strut, 2 - Iterate through struts,
    // 3 - Strip test, 4 - Full color flash, 5 - Quaternion test
    int domeTestPattern { get; set; }

    // The dome's compositing stack, index 0 = background (bottom), last = front.
    // Persisted. ConfigurationEditor replaces the whole list so notifications
    // and runtime snapshot publication cannot be bypassed by in-place edits.
    ImmutableArray<DomeLayerView> domeLayerStack { get; }

    // Dome cable mapping. The dome is wired as 10 cables (5 control boxes, each
    // with two ethernet cables: A = strands 0-3, B = strands 4-7), identified by
    // box*2 + half (half 0 = A, 1 = B). domeCableMapping[c] records, for each
    // controller cable c, the physical dome endpoint (same box*2 + half labeling
    // under the hard-coded layout) that actually lights when c is driven.
    // Identity {0..9} = the legacy hard-coded wiring. Discovered by the "Set
    // Dome Mapping" calibration window.
    ImmutableArray<int> domeCableMapping { get; }

    // One independently owned physical-port -> legacy-path permutation for
    // each of the five dome-side boxes. Missing or invalid entries fall back to
    // identity per box.
    ImmutableArray<ImmutableArray<int>> domePortMappings { get; }

    // Cross-layer visual state: every stack layer applies the same fade /
    // hue-rotate speed to its own buffer. Per-visualizer tuning does NOT belong
    // here — it lives in each layer's renderer parameter bag (see LayerCatalog
    // parameter schemas).
    double domeGlobalFadeSpeed { get; set; }
    double domeGlobalHueSpeed { get; set; }

    // Per-layer manual-fire counters, keyed by stable
    // DomeLayerSettings.InstanceId, bumped by native/web Fire buttons. A
    // monotonic counter rather than a bool so two clients firing
    // concurrently never race over who clears a flag — LayerTrigger just
    // edge-detects "the count changed since I last looked."
    ImmutableDictionary<string, int> domeLayerFireCounters { get; }

    // Per-layer manual-clear counters, exactly parallel to domeLayerFireCounters:
    // keyed by InstanceId, bumped by the native Clear button. A layer that
    // carries accumulated live state (e.g. Shooting Star's in-flight dots)
    // edge-detects a bump and drops that state — an escape hatch when a layer is
    // dragging the frame rate. Same monotonic-counter rationale as fire.
    ImmutableDictionary<string, int> domeLayerClearCounters { get; }

    // Named snapshots of the dome look (layer stack + the two globals above),
    // saved/recalled by the VJ. See DomeScene and SceneService. An empty
    // document collection is projected as an empty immutable view.
    ImmutableArray<DomeSceneView> domeScenes { get; }

    // Ordered named palettes. A layer's "palette" parameter indexes this cached
    // immutable view; palette edits publish a replacement generation through
    // ConfigurationEditor. Current configuration files contain at least one.
    ImmutableArray<DomePaletteSnapshot> domePalettes { get; }

    // maps from device ID to preset ID
    bool vjHUDEnabled { get; set; }
    ImmutableDictionary<int, int> midiDevices { get; }
    ImmutableDictionary<int, MidiPresetView> midiPresets { get; }

    double flashSpeed { get; set; }

    // 0 = human, 1 = Madmom, 2 = Pro DJ Link
    int beatInput { get; set; }

    int orientationDeviceSpotlight { get; set; }
    bool orientationCalibrate { get; set; }

    // Port of the USB-CDC ESP-NOW wand receiver (COM name on Windows, stable
    // /dev/serial alias on Linux), "" = no serial input. The receiver reacts
    // live to changes; not in configPropertiesToRebootOn.
    string wandSerialPort { get; set; }
  }

  // Collection changes are explicit owner-thread operations. Inputs use
  // detached document DTOs for convenient editing; implementations deep-copy
  // the changed branch before publishing one immutable view and notification.
  public interface ConfigurationEditor {
    void ReplaceDomeLayerStack(IReadOnlyList<DomeLayerSettings> value);
    void ReplaceDomeCableMapping(IReadOnlyList<int> value);
    void ReplaceDomePortMappings(IReadOnlyList<DomePortMapping?> value);
    void ReplaceDomeLayerFireCounters(
      IReadOnlyDictionary<string, int> value);
    void ReplaceDomeLayerClearCounters(
      IReadOnlyDictionary<string, int> value);
    void ReplaceDomeScenes(IReadOnlyList<DomeScene> value);
    void ReplaceDomePalettes(IReadOnlyList<DomePalette> value);
    void ReplaceMidiDevices(IReadOnlyDictionary<int, int> value);
    void ReplaceMidiPresets(IReadOnlyDictionary<int, MidiPreset> value);
    void UpsertMidiPreset(int id, MidiPreset value);
    void RemoveMidiPreset(int id);
  }
}
