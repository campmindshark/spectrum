using System;
using System.Collections.Generic;
using Spectrum.Base;

namespace Spectrum {

  /**
   * One scalar configuration property's runtime and persistence contract.
   * Collection-valued branches retain their explicit replacement APIs because
   * they require deep-copy and snapshot publication rather than scalar Set().
   */
  public abstract class ConfigurationPropertyMetadata {

    protected ConfigurationPropertyMetadata(
      string key,
      Type valueType,
      object? defaultValue,
      bool persisted,
      bool requiresRestart,
      ParameterDescriptor? webParameter,
      bool nativeWindowOnly
    ) {
      this.Key = key;
      this.ValueType = valueType;
      this.DefaultValue = defaultValue;
      this.Persisted = persisted;
      this.RequiresRestart = requiresRestart;
      this.WebParameter = webParameter;
      this.NativeWindowOnly = nativeWindowOnly;
    }

    public string Key { get; }
    public Type ValueType { get; }
    public object? DefaultValue { get; }
    public bool Persisted { get; }
    public bool RequiresRestart { get; }
    internal ParameterDescriptor? WebParameter { get; }
    internal bool NativeWindowOnly { get; }

    public abstract object? Read(Configuration configuration);
  }

  public sealed class ConfigurationPropertyMetadata<T> :
      ConfigurationPropertyMetadata {

    private readonly Func<Configuration, T> get;

    internal ConfigurationPropertyMetadata(
      string key,
      T defaultValue,
      bool persisted,
      bool requiresRestart,
      Func<Configuration, T> get,
      ParameterDescriptor? webParameter = null,
      bool nativeWindowOnly = false
    ) : base(
        key,
        typeof(T),
        defaultValue,
        persisted,
        requiresRestart,
        webParameter,
        nativeWindowOnly) {
      this.TypedDefaultValue = defaultValue;
      this.get = get;
    }

    public T TypedDefaultValue { get; }

    public override object? Read(Configuration configuration) =>
      this.get(configuration);
  }

  /**
   * Canonical metadata for every scalar SpectrumConfiguration property.
   *
   * This catalog owns stable property identifiers, code defaults, web
   * validation/display metadata, persistence participation, and engine restart
   * policy. The serializer DTO remains explicit for XSerializer, while tests
   * enforce an exhaustive one-to-one mapping between its scalar properties,
   * Configuration, and this catalog.
   */
  public static class SpectrumConfigurationSchema {

    public const bool DomeEnabledDefault = false;
    public const bool MidiInputEnabledDefault = false;
    public const bool DomeOutputInSeparateThreadDefault = false;
    public const string DomeBeagleboneOpcAddressDefault = "";
    public const bool DomeSimulationEnabledDefault = false;
    public const bool WebDomeSimulatorEnabledDefault = true;
    public const double DomeMaxBrightnessDefault = 0.5;
    public const double DomeBrightnessDefault = 0.1;
    public const int DomeTestPatternDefault = 0;
    public const double DomeGlobalFadeSpeedDefault = 0.0;
    public const double DomeGlobalHueSpeedDefault = 1.0;
    public const bool VjHudEnabledDefault = false;
    public const double FlashSpeedDefault = 0.0;
    public const int BeatInputDefault = 0;
    public const int OrientationDeviceSpotlightDefault = 0;
    public const bool OrientationCalibrateDefault = false;
    public const string WandSerialPortDefault = "";

    public const double DomeGlobalFadeSpeedMinimum = 0.0;
    public const double DomeGlobalFadeSpeedMaximum = 3.0;
    public const double DomeGlobalHueSpeedMinimum = 0.0;
    public const double DomeGlobalHueSpeedMaximum = 3.0;
    public const double FlashSpeedMinimum = 0.0;
    public const double FlashSpeedMaximum = 32.0;
    public const double DomeBrightnessMinimum = 0.0;
    public const double DomeBrightnessMaximum = 1.0;

    private static readonly IReadOnlyList<string> BeatInputNames =
      Array.AsReadOnly(new[] {
        "Human",
        "Madmom",
        "Pro DJ Link",
      });

    public static ConfigurationPropertyMetadata<string?> AudioDeviceId {
      get;
    } = OptionalString(
      nameof(Configuration.audioDeviceID),
      defaultValue: null,
      c => c.audioDeviceID,
      (c, value) => c.audioDeviceID = value,
      ControlRole.Maintenance,
      label: "Audio capture device",
      description:
        "Stable platform capture-device identifier; empty disables capture.",
      normalize: value => value.Trim(),
      requiresRestart: true);

    public static ConfigurationPropertyMetadata<bool> DomeEnabled {
      get;
    } = Bool(
      nameof(Configuration.domeEnabled),
      DomeEnabledDefault,
      c => c.domeEnabled,
      (c, value) => c.domeEnabled = value,
      ControlRole.Maintenance,
      label: "Enable dome output",
      description: "Send live frames to the configured OPC controller.");

    public static ConfigurationPropertyMetadata<bool> MidiInputEnabled {
      get;
    } = Bool(
      nameof(Configuration.midiInputEnabled),
      MidiInputEnabledDefault,
      c => c.midiInputEnabled,
      (c, value) => c.midiInputEnabled = value,
      ControlRole.Maintenance,
      label: "Enable MIDI input",
      description: "Listen to configured MIDI devices.");

    public static ConfigurationPropertyMetadata<bool>
      DomeOutputInSeparateThread { get; } = Bool(
        nameof(Configuration.domeOutputInSeparateThread),
        DomeOutputInSeparateThreadDefault,
        c => c.domeOutputInSeparateThread,
        (c, value) => c.domeOutputInSeparateThread = value,
        ControlRole.Maintenance,
        label: "Send dome output on a separate thread",
        description: "Advanced: restarts the engine when changed.",
        requiresRestart: true);

    public static ConfigurationPropertyMetadata<string>
      DomeBeagleboneOpcAddress { get; } = String(
        nameof(Configuration.domeBeagleboneOPCAddress),
        DomeBeagleboneOpcAddressDefault,
        c => c.domeBeagleboneOPCAddress,
        (c, value) => c.domeBeagleboneOPCAddress = value,
        ControlRole.Maintenance,
        label: "OPC host and port",
        description:
          "Controller address in host:port or host:port:channel format.",
        normalize: NormalizeOpcAddress);

    public static ConfigurationPropertyMetadata<bool> DomeSimulationEnabled {
      get;
    } = Bool(
      nameof(Configuration.domeSimulationEnabled),
      DomeSimulationEnabledDefault,
      c => c.domeSimulationEnabled,
      (c, value) => c.domeSimulationEnabled = value,
      ControlRole.Maintenance,
      label: "Show dome simulator",
      description: "Render the dome output in a resizable preview window.",
      nativeWindowOnly: true);

    public static ConfigurationPropertyMetadata<bool> WebDomeSimulatorEnabled {
      get;
    } = Bool(
      nameof(Configuration.webDomeSimulatorEnabled),
      WebDomeSimulatorEnabledDefault,
      c => c.webDomeSimulatorEnabled,
      (c, value) => c.webDomeSimulatorEnabled = value);

    public static ConfigurationPropertyMetadata<double> DomeMaxBrightness {
      get;
    } = Double(
      nameof(Configuration.domeMaxBrightness),
      DomeMaxBrightnessDefault,
      c => c.domeMaxBrightness,
      (c, value) => c.domeMaxBrightness = value,
      ControlRole.Maintenance,
      DomeBrightnessMinimum,
      DomeBrightnessMaximum,
      label: "Maximum brightness",
      description: "Safety ceiling applied to dome output.",
      unit: "%");

    public static ConfigurationPropertyMetadata<double> DomeBrightness {
      get;
    } = Double(
      nameof(Configuration.domeBrightness),
      DomeBrightnessDefault,
      c => c.domeBrightness,
      (c, value) => c.domeBrightness = value,
      ControlRole.Maintenance,
      DomeBrightnessMinimum,
      DomeBrightnessMaximum,
      label: "Brightness",
      description: "Current dome output level.",
      unit: "%");

    public static ConfigurationPropertyMetadata<int> DomeTestPattern {
      get;
    } = Enum(
      nameof(Configuration.domeTestPattern),
      DomeTestPatternDefault,
      c => c.domeTestPattern,
      (c, value) => c.domeTestPattern = value,
      ControlRole.Maintenance,
      DomeTestPatterns.Names,
      label: "Dome test pattern",
      description:
        "Overrides the live look while a diagnostic pattern is active.");

    public static ConfigurationPropertyMetadata<double> DomeGlobalFadeSpeed {
      get;
    } = Double(
      nameof(Configuration.domeGlobalFadeSpeed),
      DomeGlobalFadeSpeedDefault,
      c => c.domeGlobalFadeSpeed,
      (c, value) => c.domeGlobalFadeSpeed = value,
      ControlRole.User,
      DomeGlobalFadeSpeedMinimum,
      DomeGlobalFadeSpeedMaximum,
      label: "Fade speed",
      description: "How quickly active layers fade between frames.");

    public static ConfigurationPropertyMetadata<double> DomeGlobalHueSpeed {
      get;
    } = Double(
      nameof(Configuration.domeGlobalHueSpeed),
      DomeGlobalHueSpeedDefault,
      c => c.domeGlobalHueSpeed,
      (c, value) => c.domeGlobalHueSpeed = value,
      ControlRole.User,
      DomeGlobalHueSpeedMinimum,
      DomeGlobalHueSpeedMaximum,
      label: "Hue speed",
      description: "How quickly the live palette rotates through hues.");

    public static ConfigurationPropertyMetadata<bool> VjHudEnabled {
      get;
    } = Bool(
      nameof(Configuration.vjHUDEnabled),
      VjHudEnabledDefault,
      c => c.vjHUDEnabled,
      (c, value) => c.vjHUDEnabled = value,
      ControlRole.Maintenance,
      label: "Show performance HUD",
      description: "Open the native live-performance window.",
      nativeWindowOnly: true);

    public static ConfigurationPropertyMetadata<double> FlashSpeed {
      get;
    } = Double(
      nameof(Configuration.flashSpeed),
      FlashSpeedDefault,
      c => c.flashSpeed,
      (c, value) => c.flashSpeed = value,
      ControlRole.User,
      FlashSpeedMinimum,
      FlashSpeedMaximum,
      label: "Flash rate",
      description: "Flash multiplier relative to the active tempo.");

    public static ConfigurationPropertyMetadata<int> BeatInput {
      get;
    } = Enum(
      nameof(Configuration.beatInput),
      BeatInputDefault,
      c => c.beatInput,
      (c, value) => c.beatInput = value,
      ControlRole.Maintenance,
      BeatInputNames,
      label: "Tempo source",
      description: "Source used for the live BPM.");

    public static ConfigurationPropertyMetadata<int>
      OrientationDeviceSpotlight { get; } = Int(
        nameof(Configuration.orientationDeviceSpotlight),
        OrientationDeviceSpotlightDefault,
        c => c.orientationDeviceSpotlight,
        (c, value) => c.orientationDeviceSpotlight = value);

    public static ConfigurationPropertyMetadata<bool> OrientationCalibrate {
      get;
    } = Bool(
      nameof(Configuration.orientationCalibrate),
      OrientationCalibrateDefault,
      c => c.orientationCalibrate,
      (c, value) => c.orientationCalibrate = value);

    public static ConfigurationPropertyMetadata<string> WandSerialPort {
      get;
    } = String(
      nameof(Configuration.wandSerialPort),
      WandSerialPortDefault,
      c => c.wandSerialPort,
      (c, value) => c.wandSerialPort = value,
      ControlRole.Maintenance,
      label: "Wand receiver port",
      description: "USB receiver serial port; leave empty to disable it.");

    public static IReadOnlyList<ConfigurationPropertyMetadata> All { get; } =
      Array.AsReadOnly(new ConfigurationPropertyMetadata[] {
        AudioDeviceId,
        DomeEnabled,
        MidiInputEnabled,
        DomeOutputInSeparateThread,
        DomeBeagleboneOpcAddress,
        DomeSimulationEnabled,
        WebDomeSimulatorEnabled,
        DomeMaxBrightness,
        DomeBrightness,
        DomeTestPattern,
        DomeGlobalFadeSpeed,
        DomeGlobalHueSpeed,
        VjHudEnabled,
        FlashSpeed,
        BeatInput,
        OrientationDeviceSpotlight,
        OrientationCalibrate,
        WandSerialPort,
      });

    public static IReadOnlyList<string> RestartPropertyNames { get; } =
      BuildRestartPropertyNames();

    public static ParameterRegistry BuildParameterRegistry(
      bool nativeWindowControlsAvailable = true
    ) {
      var descriptors = new List<ParameterDescriptor>();
      foreach (ConfigurationPropertyMetadata property in All) {
        ParameterDescriptor? descriptor = property.WebParameter;
        if (descriptor != null &&
            (nativeWindowControlsAvailable || !property.NativeWindowOnly)) {
          descriptors.Add(descriptor);
        }
      }
      return new ParameterRegistry(descriptors);
    }

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
      if (parts.Length == 3 && !byte.TryParse(parts[2], out _)) {
        throw new ArgumentException("channel must be between 0 and 255");
      }
      return value;
    }

    private static IReadOnlyList<string> BuildRestartPropertyNames() {
      var names = new List<string>();
      foreach (ConfigurationPropertyMetadata property in All) {
        if (property.RequiresRestart) {
          names.Add(property.Key);
        }
      }
      return names.AsReadOnly();
    }

    private static ConfigurationPropertyMetadata<bool> Bool(
      string key,
      bool defaultValue,
      Func<Configuration, bool> get,
      Action<Configuration, bool> set,
      ControlRole? role = null,
      string? label = null,
      string? description = null,
      string? unit = null,
      bool requiresRestart = false,
      bool nativeWindowOnly = false
    ) => new ConfigurationPropertyMetadata<bool>(
      key,
      defaultValue,
      persisted: true,
      requiresRestart,
      get,
      role == null
        ? null
        : new BoolParameter(
          key, role.Value, get, set, label, description, unit),
      nativeWindowOnly);

    private static ConfigurationPropertyMetadata<double> Double(
      string key,
      double defaultValue,
      Func<Configuration, double> get,
      Action<Configuration, double> set,
      ControlRole? role = null,
      double min = double.MinValue,
      double max = double.MaxValue,
      string? label = null,
      string? description = null,
      string? unit = null
    ) => new ConfigurationPropertyMetadata<double>(
      key,
      defaultValue,
      persisted: true,
      requiresRestart: false,
      get,
      role == null
        ? null
        : new DoubleParameter(
          key, role.Value, min, max, get, set, label, description, unit));

    private static ConfigurationPropertyMetadata<int> Int(
      string key,
      int defaultValue,
      Func<Configuration, int> get,
      Action<Configuration, int> set
    ) => new ConfigurationPropertyMetadata<int>(
      key,
      defaultValue,
      persisted: true,
      requiresRestart: false,
      get);

    private static ConfigurationPropertyMetadata<int> Enum(
      string key,
      int defaultValue,
      Func<Configuration, int> get,
      Action<Configuration, int> set,
      ControlRole role,
      IReadOnlyList<string> options,
      string? label = null,
      string? description = null,
      string? unit = null
    ) => new ConfigurationPropertyMetadata<int>(
      key,
      defaultValue,
      persisted: true,
      requiresRestart: false,
      get,
      new EnumIntParameter(
        key, role, options, get, set, label, description, unit));

    private static ConfigurationPropertyMetadata<string> String(
      string key,
      string defaultValue,
      Func<Configuration, string> get,
      Action<Configuration, string> set,
      ControlRole? role = null,
      string? label = null,
      string? description = null,
      string? unit = null,
      Func<string, string>? normalize = null
    ) => new ConfigurationPropertyMetadata<string>(
      key,
      defaultValue,
      persisted: true,
      requiresRestart: false,
      get,
      role == null
        ? null
        : new StringParameter(
          key, role.Value, get, set,
          label: label,
          description: description,
          unit: unit,
          normalize: normalize));

    private static ConfigurationPropertyMetadata<string?> OptionalString(
      string key,
      string? defaultValue,
      Func<Configuration, string?> get,
      Action<Configuration, string?> set,
      ControlRole role,
      string? label = null,
      string? description = null,
      string? unit = null,
      Func<string, string>? normalize = null,
      bool requiresRestart = false
    ) => new ConfigurationPropertyMetadata<string?>(
      key,
      defaultValue,
      persisted: true,
      requiresRestart,
      get,
      new StringParameter(
        key,
        role,
        configuration => get(configuration) ?? "",
        (configuration, value) => set(configuration, value),
        label: label,
        description: description,
        unit: unit,
        normalize: normalize));
  }
}
