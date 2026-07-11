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

    // Dome test patterns (domeTestPattern index).
    private static readonly IReadOnlyList<string> DomeTestPatternNames = new[] {
      "None", "Flash Colors By Strut", "Iterate Through Struts",
      "Strip Test", "Full Color Flash",
    };
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

        // Brightness
        new DoubleParameter("domeBrightness", user, 0.0, 1.0,
          c => c.domeBrightness, (c, v) => c.domeBrightness = v),
        new DoubleParameter("domeMaxBrightness", user, 0.0, 1.0,
          c => c.domeMaxBrightness, (c, v) => c.domeMaxBrightness = v),

        // Dome global speeds — cross-layer scene state. Per-visualizer tuning
        // (radial size, ripple steps, twinkle density, ...) lives in each
        // layer's Params bag, served by LayersController rather than here.
        new DoubleParameter("domeGlobalFadeSpeed", user, 0.0, 3.0,
          c => c.domeGlobalFadeSpeed, (c, v) => c.domeGlobalFadeSpeed = v),
        new DoubleParameter("domeGlobalHueSpeed", user, 0.0, 3.0,
          c => c.domeGlobalHueSpeed, (c, v) => c.domeGlobalHueSpeed = v),

        // The dome layer stack replaces the old single-visualizer selector; it
        // is compound state served by LayersController (GET/PUT /api/layers) and
        // broadcast on the SSE "layers" frame, not a field-level parameter here.

        // Global flash
        new DoubleParameter("flashSpeed", user, 0.0, 32.0,
          c => c.flashSpeed, (c, v) => c.flashSpeed = v),

        // The color palette replaces the retired 8-bank colorPaletteIndex
        // switcher: it is compound state served by PaletteController
        // (GET/PUT /api/palette + /api/palettes) and broadcast on the SSE
        // "palette"/"palettes" frames, not a field-level parameter here.

        // ---- Maintenance surface: device wiring & diagnostics ----

        // BPM source (Human tap-tempo / Madmom beat tracker / Pro DJ Link)
        new EnumIntParameter("beatInput", maint, BeatInputNames,
          c => c.beatInput, (c, v) => c.beatInput = v),

        // Device enable flags
        new BoolParameter("domeEnabled", maint,
          c => c.domeEnabled, (c, v) => c.domeEnabled = v),
        new BoolParameter("midiInputEnabled", maint,
          c => c.midiInputEnabled, (c, v) => c.midiInputEnabled = v),
        new BoolParameter("vjHUDEnabled", maint,
          c => c.vjHUDEnabled, (c, v) => c.vjHUDEnabled = v),

        // Simulators
        new BoolParameter("domeSimulationEnabled", maint,
          c => c.domeSimulationEnabled, (c, v) => c.domeSimulationEnabled = v),

        // OPC addresses
        new StringParameter("domeBeagleboneOPCAddress", maint,
          c => c.domeBeagleboneOPCAddress, (c, v) => c.domeBeagleboneOPCAddress = v),

        // Wand USB-CDC receiver COM port ("" = no serial input). The receiver
        // reacts live via PropertyChanged; the port list is served separately by
        // GET /api/maintenance/wands/serial.
        new StringParameter("wandSerialPort", maint,
          c => c.wandSerialPort, (c, v) => c.wandSerialPort = v),

        // Threading flags (these trigger an Operator reboot in MainWindow)
        new BoolParameter("midiInputInSeparateThread", maint,
          c => c.midiInputInSeparateThread, (c, v) => c.midiInputInSeparateThread = v),
        new BoolParameter("domeOutputInSeparateThread", maint,
          c => c.domeOutputInSeparateThread, (c, v) => c.domeOutputInSeparateThread = v),

        // Test patterns (modal — will get advisory locks in step 5)
        new EnumIntParameter("domeTestPattern", maint, DomeTestPatternNames,
          c => c.domeTestPattern, (c, v) => c.domeTestPattern = v),
      };

      return new ParameterRegistry(descriptors);
    }
  }
}
