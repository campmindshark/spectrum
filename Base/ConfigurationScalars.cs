using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Spectrum.Base {

  /**
   * Canonical storage and declarations for every persisted scalar setting.
   * Both the live Configuration and the XML document inherit this surface, so
   * defaults, property types, backing state, and scalar copy plumbing cannot
   * drift apart. Derived live configurations supply mutation dispatch and
   * snapshot-publication hooks; serializer documents use the no-op defaults.
   */
  public class ConfigurationScalars {

    public const string? AudioDeviceIdDefault = null;
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

    private ScalarValues values = ScalarValues.Default;

    public virtual string? audioDeviceID {
      get => this.values.AudioDeviceId;
      set => this.SetScalar(ref this.values.AudioDeviceId, value);
    }

    public virtual bool domeEnabled {
      get => this.values.DomeEnabled;
      set => this.SetScalar(ref this.values.DomeEnabled, value);
    }

    public virtual bool midiInputEnabled {
      get => this.values.MidiInputEnabled;
      set => this.SetScalar(ref this.values.MidiInputEnabled, value);
    }

    public virtual bool domeOutputInSeparateThread {
      get => this.values.DomeOutputInSeparateThread;
      set => this.SetScalar(
        ref this.values.DomeOutputInSeparateThread, value);
    }

    public virtual string domeBeagleboneOPCAddress {
      get => this.values.DomeBeagleboneOpcAddress;
      set => this.SetScalar(
        ref this.values.DomeBeagleboneOpcAddress,
        value ?? DomeBeagleboneOpcAddressDefault);
    }

    public virtual bool domeSimulationEnabled {
      get => this.values.DomeSimulationEnabled;
      set => this.SetScalar(ref this.values.DomeSimulationEnabled, value);
    }

    public virtual bool webDomeSimulatorEnabled {
      get => this.values.WebDomeSimulatorEnabled;
      set => this.SetScalar(ref this.values.WebDomeSimulatorEnabled, value);
    }

    public virtual double domeMaxBrightness {
      get => this.values.DomeMaxBrightness;
      set => this.SetScalar(ref this.values.DomeMaxBrightness, value);
    }

    public virtual double domeBrightness {
      get => this.values.DomeBrightness;
      set => this.SetScalar(ref this.values.DomeBrightness, value);
    }

    public virtual int domeTestPattern {
      get => this.values.DomeTestPattern;
      set => this.SetScalar(ref this.values.DomeTestPattern, value);
    }

    public virtual double domeGlobalFadeSpeed {
      get => this.values.DomeGlobalFadeSpeed;
      set => this.SetScalar(ref this.values.DomeGlobalFadeSpeed, value);
    }

    public virtual double domeGlobalHueSpeed {
      get => this.values.DomeGlobalHueSpeed;
      set => this.SetScalar(ref this.values.DomeGlobalHueSpeed, value);
    }

    public virtual bool vjHUDEnabled {
      get => this.values.VjHudEnabled;
      set => this.SetScalar(ref this.values.VjHudEnabled, value);
    }

    public virtual double flashSpeed {
      get => this.values.FlashSpeed;
      set => this.SetScalar(ref this.values.FlashSpeed, value);
    }

    public virtual int beatInput {
      get => this.values.BeatInput;
      set => this.SetScalar(ref this.values.BeatInput, value);
    }

    public virtual int orientationDeviceSpotlight {
      get => this.values.OrientationDeviceSpotlight;
      set => this.SetScalar(
        ref this.values.OrientationDeviceSpotlight, value);
    }

    public virtual bool orientationCalibrate {
      get => this.values.OrientationCalibrate;
      set => this.SetScalar(ref this.values.OrientationCalibrate, value);
    }

    public virtual string wandSerialPort {
      get => this.values.WandSerialPort;
      set => this.SetScalar(
        ref this.values.WandSerialPort,
        value ?? WandSerialPortDefault);
    }

    // Compound show-state transactions publish both globals atomically and
    // raise their exact notifications themselves after the full state commits.
    protected void SetDomeGlobalSpeedsWithoutNotification(
      double fadeSpeed, double hueSpeed
    ) {
      this.values.DomeGlobalFadeSpeed = fadeSpeed;
      this.values.DomeGlobalHueSpeed = hueSpeed;
    }

    // Copies the complete scalar value object in one operation. The target's
    // hook publishes one coherent set of live snapshots when appropriate.
    protected void CopyScalarsTo(ConfigurationScalars target) {
      if (target == null) {
        throw new ArgumentNullException(nameof(target));
      }
      target.values = this.values;
      target.ScalarsReplaced();
    }

    protected virtual bool DispatchScalarMutation<T>(
      string propertyName, T value
    ) => false;

    protected virtual void ScalarChanged(string propertyName) { }

    protected virtual void ScalarsReplaced() { }

    private void SetScalar<T>(
      ref T field,
      T value,
      [CallerMemberName] string propertyName = ""
    ) {
      if (this.DispatchScalarMutation(propertyName, value) ||
          EqualityComparer<T>.Default.Equals(field, value)) {
        return;
      }
      field = value;
      this.ScalarChanged(propertyName);
    }

    private struct ScalarValues {
      public string? AudioDeviceId;
      public bool DomeEnabled;
      public bool MidiInputEnabled;
      public bool DomeOutputInSeparateThread;
      public string DomeBeagleboneOpcAddress;
      public bool DomeSimulationEnabled;
      public bool WebDomeSimulatorEnabled;
      public double DomeMaxBrightness;
      public double DomeBrightness;
      public int DomeTestPattern;
      public double DomeGlobalFadeSpeed;
      public double DomeGlobalHueSpeed;
      public bool VjHudEnabled;
      public double FlashSpeed;
      public int BeatInput;
      public int OrientationDeviceSpotlight;
      public bool OrientationCalibrate;
      public string WandSerialPort;

      public static ScalarValues Default => new ScalarValues {
        AudioDeviceId = AudioDeviceIdDefault,
        DomeEnabled = DomeEnabledDefault,
        MidiInputEnabled = MidiInputEnabledDefault,
        DomeOutputInSeparateThread = DomeOutputInSeparateThreadDefault,
        DomeBeagleboneOpcAddress = DomeBeagleboneOpcAddressDefault,
        DomeSimulationEnabled = DomeSimulationEnabledDefault,
        WebDomeSimulatorEnabled = WebDomeSimulatorEnabledDefault,
        DomeMaxBrightness = DomeMaxBrightnessDefault,
        DomeBrightness = DomeBrightnessDefault,
        DomeTestPattern = DomeTestPatternDefault,
        DomeGlobalFadeSpeed = DomeGlobalFadeSpeedDefault,
        DomeGlobalHueSpeed = DomeGlobalHueSpeedDefault,
        VjHudEnabled = VjHudEnabledDefault,
        FlashSpeed = FlashSpeedDefault,
        BeatInput = BeatInputDefault,
        OrientationDeviceSpotlight = OrientationDeviceSpotlightDefault,
        OrientationCalibrate = OrientationCalibrateDefault,
        WandSerialPort = WandSerialPortDefault,
      };
    }
  }
}
