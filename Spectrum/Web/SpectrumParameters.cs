using System;
using System.Collections.Generic;
using Spectrum.Base;

namespace Spectrum.Web {

  /**
   * The concrete catalog of web-controllable parameters for
   * SpectrumConfiguration. This is the one place that decides which
   * Configuration properties are reachable over HTTP, their valid ranges, and
   * which surface (user | maintenance) each belongs to.
   *
   * Ranges mirror the Minimum/Maximum on the WPF sliders/combos (VJHUDWindow /
   * MainWindow XAML) so web writes are clamped exactly as the native GUI
   * constrains them. Keep this in sync when a slider range changes.
   *
   * Deliberately NOT exposed here: the *InSeparateThread reboot flags are
   * maintenance-only and included; but raw command queues, cable mappings,
   * midi/level-driver dictionaries, and other compound/collection state stay
   * out entirely (see docs/web_architecture.md — compound writes stay
   * maintenance-only and go through the gateway, not field-level LWW).
   */
  public static class SpectrumParameters {

    public static string NormalizeOpcAddress(string raw) {
      string value = (raw ?? "").Trim();
      string[] parts = value.Split(':');
      if ((parts.Length != 2 && parts.Length != 3) ||
          string.IsNullOrWhiteSpace(parts[0])) {
        throw new ArgumentException(
          "address must use host:port or host:port:channel");
      }
      if (!int.TryParse(parts[1], out int port) || port < 1 || port > 65535) {
        throw new ArgumentException("port must be between 1 and 65535");
      }
      if (parts.Length == 3 &&
          (!byte.TryParse(parts[2], out _))) {
        throw new ArgumentException("channel must be between 0 and 255");
      }
      return value;
    }

    // BPM source selection (beatInput index) — mirrors the Tempo radio group in
    // the VJ HUD (VJHUDWindow: tempoSelectorHuman/Madmom/Link).
    private static readonly IReadOnlyList<string> BeatInputNames = new[] {
      "Human", "Madmom", "Pro DJ Link",
    };

    public static ParameterRegistry BuildRegistry() {
      const ControlRole user = ControlRole.User;
      const ControlRole maint = ControlRole.Maintenance;

      var descriptors = new List<ParameterDescriptor> {

        // ---- User surface: the curated VJ HUD knobs ----

        // Dome global speeds — cross-layer scene state. Per-visualizer tuning
        // (radial size, ripple steps, twinkle density, ...) lives in each
        // layer's namespaced parameter bags, served by LayersController.
        new DoubleParameter("domeGlobalFadeSpeed", user, 0.0, 3.0,
          c => c.domeGlobalFadeSpeed, (c, v) => c.domeGlobalFadeSpeed = v,
          label: "Fade speed",
          description: "How quickly active layers fade between frames."),
        new DoubleParameter("domeGlobalHueSpeed", user, 0.0, 3.0,
          c => c.domeGlobalHueSpeed, (c, v) => c.domeGlobalHueSpeed = v,
          label: "Hue speed",
          description: "How quickly the live palette rotates through hues."),

        // The dome layer stack replaces the old single-visualizer selector; it
        // is compound state served by LayersController (GET/PUT /api/layers) and
        // broadcast on the SSE "layers" frame, not a field-level parameter here.

        // Global flash
        new DoubleParameter("flashSpeed", user, 0.0, 32.0,
          c => c.flashSpeed, (c, v) => c.flashSpeed = v,
          label: "Flash rate",
          description: "Flash multiplier relative to the active tempo."),

        // Named live palettes are compound state served by PaletteController
        // under /api/palettes and broadcast on the SSE "palettes" frame, not a
        // field-level parameter here.

        // ---- Maintenance surface: device wiring & diagnostics ----

        // Brightness
        new DoubleParameter("domeBrightness", maint, 0.0, 1.0,
          c => c.domeBrightness, (c, v) => c.domeBrightness = v,
          label: "Brightness", description: "Current dome output level.",
          unit: "%"),
        new DoubleParameter("domeMaxBrightness", maint, 0.0, 1.0,
          c => c.domeMaxBrightness, (c, v) => c.domeMaxBrightness = v,
          label: "Maximum brightness",
          description: "Safety ceiling applied to dome output.", unit: "%"),

        // BPM source (Human tap-tempo / Madmom beat tracker / Pro DJ Link)
        new EnumIntParameter("beatInput", maint, BeatInputNames,
          c => c.beatInput, (c, v) => c.beatInput = v,
          label: "Tempo source",
          description: "Source used for the live BPM."),

        // Device enable flags
        new BoolParameter("domeEnabled", maint,
          c => c.domeEnabled, (c, v) => c.domeEnabled = v,
          label: "Enable dome output",
          description: "Send live frames to the configured OPC controller."),
        new BoolParameter("midiInputEnabled", maint,
          c => c.midiInputEnabled, (c, v) => c.midiInputEnabled = v,
          label: "Enable MIDI input",
          description: "Listen to configured MIDI devices."),
        new BoolParameter("vjHUDEnabled", maint,
          c => c.vjHUDEnabled, (c, v) => c.vjHUDEnabled = v,
          label: "Show performance HUD",
          description: "Open the native live-performance window."),

        // Simulators
        new BoolParameter("domeSimulationEnabled", maint,
          c => c.domeSimulationEnabled, (c, v) => c.domeSimulationEnabled = v,
          label: "Show dome simulator",
          description: "Render the dome output in a resizable preview window."),

        // OPC addresses
        new StringParameter("domeBeagleboneOPCAddress", maint,
          c => c.domeBeagleboneOPCAddress, (c, v) => c.domeBeagleboneOPCAddress = v,
          label: "OPC host and port",
          description: "Controller address in host:port or host:port:channel format.",
          normalize: NormalizeOpcAddress),

        // Wand USB-CDC receiver COM port ("" = no serial input). The receiver
        // reacts live via PropertyChanged; the port list is served separately by
        // GET /api/maintenance/wands/serial.
        new StringParameter("wandSerialPort", maint,
          c => c.wandSerialPort, (c, v) => c.wandSerialPort = v,
          label: "Wand receiver port",
          description: "USB receiver serial port; leave empty to disable it."),

        // Threading flag (triggers an Operator reboot in MainWindow)
        new BoolParameter("domeOutputInSeparateThread", maint,
          c => c.domeOutputInSeparateThread, (c, v) => c.domeOutputInSeparateThread = v,
          label: "Send dome output on a separate thread",
          description: "Advanced: restarts the engine when changed."),

        // Test patterns (modal — will get advisory locks in step 5)
        new EnumIntParameter("domeTestPattern", maint, DomeTestPatterns.Names,
          c => c.domeTestPattern, (c, v) => c.domeTestPattern = v,
          label: "Dome test pattern",
          description: "Overrides the live look while a diagnostic pattern is active."),
      };

      return new ParameterRegistry(descriptors);
    }
  }
}
