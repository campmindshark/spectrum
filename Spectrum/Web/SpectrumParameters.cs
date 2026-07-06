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

    // Labels for the dome active-visualizer dropdown (domeActiveVis index).
    private static readonly IReadOnlyList<string> DomeVisNames = new[] {
      "Volume", "Radial", "Race", "Snakes", "Quaternion Test",
      "Quaternion Multi Test", "Quaternion Paintbrush", "Splat",
    };
    // Labels for the dome radial-effect dropdown (domeRadialEffect index).
    private static readonly IReadOnlyList<string> DomeRadialEffectNames = new[] {
      "Radar", "Pulse", "Spiral", "Bubbles",
    };
    // Dome test patterns (domeTestPattern index).
    private static readonly IReadOnlyList<string> DomeTestPatternNames = new[] {
      "None", "Flash Colors By Strut", "Iterate Through Struts",
      "Strip Test", "Full Color Flash",
    };
    // Bar test patterns use this two-option set.
    private static readonly IReadOnlyList<string> FlashTestPatternNames = new[] {
      "None", "Flash Colors",
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
        new DoubleParameter("barBrightness", user, 0.0, 1.0,
          c => c.barBrightness, (c, v) => c.barBrightness = v),

        // Dome global speeds/effects
        new DoubleParameter("domeGlobalFadeSpeed", user, 0.0, 3.0,
          c => c.domeGlobalFadeSpeed, (c, v) => c.domeGlobalFadeSpeed = v),
        new DoubleParameter("domeGlobalHueSpeed", user, 1.0, 3.0,
          c => c.domeGlobalHueSpeed, (c, v) => c.domeGlobalHueSpeed = v),
        new DoubleParameter("domeTwinkleDensity", user, 0.0, 0.001,
          c => c.domeTwinkleDensity, (c, v) => c.domeTwinkleDensity = v),
        new DoubleParameter("domeRippleCDStep", user, 0.0, 10.0,
          c => c.domeRippleCDStep, (c, v) => c.domeRippleCDStep = v),
        new DoubleParameter("domeRippleStep", user, 0.0, 4.0,
          c => c.domeRippleStep, (c, v) => c.domeRippleStep = v),
        new DoubleParameter("domeVolumeRotationSpeed", user, 0.0, 4.0,
          c => c.domeVolumeRotationSpeed, (c, v) => c.domeVolumeRotationSpeed = v),
        new DoubleParameter("domeGradientSpeed", user, 0.0, 4.0,
          c => c.domeGradientSpeed, (c, v) => c.domeGradientSpeed = v),

        // Dome radial visualizer
        new EnumIntParameter("domeRadialEffect", user, DomeRadialEffectNames,
          c => c.domeRadialEffect, (c, v) => c.domeRadialEffect = v),
        new DoubleParameter("domeRadialSize", user, 0.0, 4.0,
          c => c.domeRadialSize, (c, v) => c.domeRadialSize = v),
        new IntParameter("domeRadialFrequency", user, 1, 12,
          c => c.domeRadialFrequency, (c, v) => c.domeRadialFrequency = v),
        new DoubleParameter("domeRadialCenterAngle", user, -3.14159, 3.14159,
          c => c.domeRadialCenterAngle, (c, v) => c.domeRadialCenterAngle = v),
        new DoubleParameter("domeRadialCenterDistance", user, 0.0, 1.0,
          c => c.domeRadialCenterDistance, (c, v) => c.domeRadialCenterDistance = v),
        new DoubleParameter("domeRadialCenterSpeed", user, 0.0, 4.0,
          c => c.domeRadialCenterSpeed, (c, v) => c.domeRadialCenterSpeed = v),

        // Which dome visualizer is active
        new EnumIntParameter("domeActiveVis", user, DomeVisNames,
          c => c.domeActiveVis, (c, v) => c.domeActiveVis = v),

        // Orientation controls — mirrors the "Display contours" checkbox in the
        // VJ HUD (VJHUDWindow orientationContours), toggling contour lines in the
        // Quaternion Paintbrush visualizer.
        new BoolParameter("orientationShowContours", user,
          c => c.orientationShowContours, (c, v) => c.orientationShowContours = v),

        // Global flash
        new DoubleParameter("flashSpeed", user, 0.0, 32.0,
          c => c.flashSpeed, (c, v) => c.flashSpeed = v),
        new IntParameter("colorPaletteIndex", user, 0, 7,
          c => c.colorPaletteIndex, (c, v) => c.colorPaletteIndex = v),

        // ---- Maintenance surface: device wiring & diagnostics ----

        // BPM source (Human tap-tempo / Madmom beat tracker / Pro DJ Link)
        new EnumIntParameter("beatInput", maint, BeatInputNames,
          c => c.beatInput, (c, v) => c.beatInput = v),

        // Device enable flags
        new BoolParameter("domeEnabled", maint,
          c => c.domeEnabled, (c, v) => c.domeEnabled = v),
        new BoolParameter("barEnabled", maint,
          c => c.barEnabled, (c, v) => c.barEnabled = v),
        new BoolParameter("midiInputEnabled", maint,
          c => c.midiInputEnabled, (c, v) => c.midiInputEnabled = v),
        new BoolParameter("vjHUDEnabled", maint,
          c => c.vjHUDEnabled, (c, v) => c.vjHUDEnabled = v),

        // Simulators
        new BoolParameter("domeSimulationEnabled", maint,
          c => c.domeSimulationEnabled, (c, v) => c.domeSimulationEnabled = v),
        new BoolParameter("barSimulationEnabled", maint,
          c => c.barSimulationEnabled, (c, v) => c.barSimulationEnabled = v),

        // OPC addresses
        new StringParameter("domeBeagleboneOPCAddress", maint,
          c => c.domeBeagleboneOPCAddress, (c, v) => c.domeBeagleboneOPCAddress = v),
        new StringParameter("barBeagleboneOPCAddress", maint,
          c => c.barBeagleboneOPCAddress, (c, v) => c.barBeagleboneOPCAddress = v),

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
        new BoolParameter("barOutputInSeparateThread", maint,
          c => c.barOutputInSeparateThread, (c, v) => c.barOutputInSeparateThread = v),

        // Test patterns (modal — will get advisory locks in step 5)
        new EnumIntParameter("domeTestPattern", maint, DomeTestPatternNames,
          c => c.domeTestPattern, (c, v) => c.domeTestPattern = v),
        new EnumIntParameter("barTestPattern", maint, FlashTestPatternNames,
          c => c.barTestPattern, (c, v) => c.barTestPattern = v),

        // Misc dome/bar geometry & timing
        new IntParameter("domeVolumeAnimationSize", maint, 1, 16,
          c => c.domeVolumeAnimationSize, (c, v) => c.domeVolumeAnimationSize = v),
        new IntParameter("domeAutoFlashDelay", maint, 0, 1000,
          c => c.domeAutoFlashDelay, (c, v) => c.domeAutoFlashDelay = v),
        new IntParameter("barInfinityWidth", maint, 0, 1000,
          c => c.barInfinityWidth, (c, v) => c.barInfinityWidth = v),
        new IntParameter("barInfinityLength", maint, 0, 1000,
          c => c.barInfinityLength, (c, v) => c.barInfinityLength = v),
        new IntParameter("barRunnerLength", maint, 0, 1000,
          c => c.barRunnerLength, (c, v) => c.barRunnerLength = v),
      };

      return new ParameterRegistry(descriptors);
    }
  }
}
