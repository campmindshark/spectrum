using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Spectrum.Audio;

namespace Spectrum {

  internal sealed record OperatorSettingsView(
    ComboBox AudioDevices,
    CheckBox MidiInputEnabled,
    CheckBox DomeOutputInSeparateThread,
    Label OperatorFps,
    TextBlock DomeOpcFps,
    TextBlock HomeDomeOpcFps,
    CheckBox DomeEnabled,
    CheckBox DomeSimulationEnabled,
    ComboBox DomeTestPattern,
    Slider DomeMaxBrightness,
    Label DomeMaxBrightnessLabel,
    Slider DomeBrightness,
    Label DomeBrightnessLabel,
    CheckBox VjHudEnabled
  );

  /**
   * Owns the ordinary settings and telemetry bindings presented by
   * MainWindow, together with audio-device discovery and selection. Device
   * population is guarded so programmatic selection changes never rewrite the
   * configured device.
   */
  internal sealed class OperatorSettingsUiController {
    private readonly SpectrumConfiguration config;
    private readonly Operator runtime;
    private readonly OperatorSettingsView view;
    private readonly Func<IReadOnlyList<AudioDevice>> discoverAudioDevices;
    private readonly Action refreshReadiness;
    private bool started;
    private bool populatingAudioDevices;

    internal OperatorSettingsUiController(
      SpectrumConfiguration config,
      Operator runtime,
      OperatorSettingsView view,
      Func<IReadOnlyList<AudioDevice>> discoverAudioDevices,
      Action refreshReadiness
    ) {
      this.config = config ??
        throw new ArgumentNullException(nameof(config));
      this.runtime = runtime ??
        throw new ArgumentNullException(nameof(runtime));
      this.view = view ??
        throw new ArgumentNullException(nameof(view));
      this.discoverAudioDevices = discoverAudioDevices ??
        throw new ArgumentNullException(nameof(discoverAudioDevices));
      this.refreshReadiness = refreshReadiness ??
        throw new ArgumentNullException(nameof(refreshReadiness));
    }

    internal void Start() {
      if (this.started) {
        return;
      }
      this.started = true;
      this.InitializeBindings();
      this.RefreshAudioDevices();
    }

    internal void RefreshAudioDevices() {
      this.runtime.Enabled = false;
      IReadOnlyList<AudioDevice> devices = this.discoverAudioDevices();

      this.populatingAudioDevices = true;
      try {
        this.view.AudioDevices.Items.Clear();
        int selectedIndex = -1;
        for (int index = 0; index < devices.Count; index++) {
          AudioDevice device = devices[index];
          this.view.AudioDevices.Items.Add(device);
          if (device.id == this.config.audioDeviceID) {
            selectedIndex = index;
          }
        }
        this.view.AudioDevices.SelectedIndex = selectedIndex;
      } finally {
        this.populatingAudioDevices = false;
      }

      this.refreshReadiness();
    }

    internal void ApplySelectedAudioDevice() {
      if (this.populatingAudioDevices ||
          this.view.AudioDevices.SelectedItem is not AudioDevice device) {
        return;
      }
      this.config.audioDeviceID = device.id;
      this.refreshReadiness();
    }

    private void InitializeBindings() {
      this.Bind(
        nameof(this.config.midiInputEnabled),
        this.view.MidiInputEnabled,
        ToggleButton.IsCheckedProperty);
      this.Bind(
        nameof(this.config.domeOutputInSeparateThread),
        this.view.DomeOutputInSeparateThread,
        ToggleButton.IsCheckedProperty);
      this.Bind(
        nameof(this.runtime.Telemetry.OperatorFPS),
        this.view.OperatorFps,
        ContentControl.ContentProperty,
        BindingMode.OneWay,
        source: this.runtime.Telemetry);
      this.Bind(
        nameof(this.runtime.Telemetry.OperatorFPS),
        this.view.OperatorFps,
        Control.ForegroundProperty,
        BindingMode.OneWay,
        new FPSToBrushConverter(),
        this.runtime.Telemetry);
      this.Bind(
        nameof(this.runtime.Telemetry.DomeBeagleboneOPCFPS),
        this.view.DomeOpcFps,
        TextBlock.TextProperty,
        BindingMode.OneWay,
        source: this.runtime.Telemetry);
      this.Bind(
        nameof(this.runtime.Telemetry.DomeBeagleboneOPCFPS),
        this.view.DomeOpcFps,
        TextBlock.ForegroundProperty,
        BindingMode.OneWay,
        new FPSToBrushConverter(),
        this.runtime.Telemetry);
      this.Bind(
        nameof(this.runtime.Telemetry.DomeBeagleboneOPCFPS),
        this.view.HomeDomeOpcFps,
        TextBlock.TextProperty,
        BindingMode.OneWay,
        source: this.runtime.Telemetry);
      this.Bind(
        nameof(this.config.domeEnabled),
        this.view.DomeEnabled,
        ToggleButton.IsCheckedProperty);
      this.Bind(
        nameof(this.config.domeSimulationEnabled),
        this.view.DomeSimulationEnabled,
        ToggleButton.IsCheckedProperty);
      this.view.DomeTestPattern.ItemsSource = DomeTestPatterns.Names;
      this.Bind(
        nameof(this.config.domeTestPattern),
        this.view.DomeTestPattern,
        Selector.SelectedIndexProperty);
      this.Bind(
        nameof(this.config.domeMaxBrightness),
        this.view.DomeMaxBrightness,
        RangeBase.ValueProperty);
      this.Bind(
        nameof(this.config.domeMaxBrightness),
        this.view.DomeMaxBrightnessLabel,
        ContentControl.ContentProperty,
        BindingMode.OneWay,
        new NormalizedPercentConverter());
      this.Bind(
        nameof(this.config.domeBrightness),
        this.view.DomeBrightness,
        RangeBase.ValueProperty);
      this.Bind(
        nameof(this.config.domeBrightness),
        this.view.DomeBrightnessLabel,
        ContentControl.ContentProperty,
        BindingMode.OneWay,
        new NormalizedPercentConverter());
      this.Bind(
        nameof(this.config.vjHUDEnabled),
        this.view.VjHudEnabled,
        ToggleButton.IsCheckedProperty);
    }

    private void Bind(
      string path,
      FrameworkElement element,
      DependencyProperty property,
      BindingMode mode = BindingMode.TwoWay,
      IValueConverter? converter = null,
      object? source = null
    ) {
      var binding = new Binding(path) {
        Source = source ?? this.config,
        Mode = mode,
        Converter = converter,
      };
      element.SetBinding(property, binding);
    }
  }
}
