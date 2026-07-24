using System;
using System.Windows.Controls;

namespace Spectrum {

  internal sealed record MidiPresetSetupView(
    ListBox Presets,
    Button ClonePreset,
    Button RenamePreset,
    Button DeletePreset,
    Label EditLabel,
    TextBox Name,
    Button Save,
    Button Cancel
  );

  internal sealed record MidiSetupView(
    MidiDeviceSetupView Device,
    MidiPresetSetupView Preset,
    MidiBindingSetupView Binding
  );

  /**
   * Coordinates the MIDI device, preset, and binding UI controllers behind
   * the event surface consumed by MainWindow.
   */
  internal sealed class MidiSetupUiController {
    private readonly MidiDeviceUiController deviceController;
    private readonly MidiPresetUiController presetController;

    internal MidiSetupUiController(
      SpectrumConfiguration config,
      MidiSetupView view,
      Func<int> deviceCount,
      Func<int, string> getDeviceName,
      Func<string, string, bool> confirmDestructiveAction
    ) {
      ArgumentNullException.ThrowIfNull(config);
      ArgumentNullException.ThrowIfNull(view);
      ArgumentNullException.ThrowIfNull(deviceCount);
      ArgumentNullException.ThrowIfNull(getDeviceName);
      ArgumentNullException.ThrowIfNull(confirmDestructiveAction);
      this.deviceController = new MidiDeviceUiController(
        config,
        view.Device,
        deviceCount,
        getDeviceName,
        confirmDestructiveAction);
      this.presetController = new MidiPresetUiController(
        config,
        view.Preset,
        view.Device.NewDevicePreset,
        view.Device.NewPresetNameContainer,
        view.Device.NewPresetName,
        view.Binding,
        confirmDestructiveAction,
        this.deviceController.LoadConfiguredDevices);
    }

    internal void Start() {
      this.deviceController.Start();
      this.presetController.Start();
    }

    internal void RefreshDevices() =>
      this.deviceController.RefreshDevices();

    internal void NewDevicePresetSelectionChanged() =>
      this.presetController.DevicePresetSelectionChanged();

    internal void NewDevicePresetNameLostFocus() =>
      this.presetController.DevicePresetNameLostFocus();

    internal void NewDevicePresetNameGotFocus() =>
      this.presetController.DevicePresetNameGotFocus();

    internal void AddDevice() {
      if (!this.presetController.EnsureDevicePresetSelected()) {
        return;
      }
      if (!this.deviceController.EnsureAvailableDeviceSelected()) {
        return;
      }
      if (!this.presetController.TryResolveDevicePreset(
          out int presetId)) {
        return;
      }

      if (!this.deviceController.TryAssignSelectedDevice(presetId)) {
        return;
      }

      this.presetController.ResetDevicePresetEntry();
      this.presetController.RefreshDeletionState(presetId);
    }

    internal void DeleteSelectedDevice() {
      if (this.deviceController.TryDeleteSelectedDevice(
          out int presetId)) {
        this.presetController.RefreshDeletionState(presetId);
      }
    }

    internal void LoadSelectedDevicePreset() {
      if (this.deviceController.TryGetSelectedPreset(
          out int presetId)) {
        this.presetController.SelectPreset(presetId);
      }
    }

    internal void ConfiguredDeviceSelectionChanged() =>
      this.deviceController.SelectionChanged();

    internal void SavePreset() =>
      this.presetController.Save();

    internal void DeleteSelectedPreset() =>
      this.presetController.DeleteSelected();

    internal void PresetSelectionChanged() =>
      this.presetController.SelectionChanged();

    internal void CloneSelectedPreset() =>
      this.presetController.CloneSelected();

    internal void BeginPresetRename() =>
      this.presetController.BeginRename();

    internal void CancelPresetEdit() =>
      this.presetController.CancelEdit();

    internal void PresetNameLostFocus() =>
      this.presetController.NameLostFocus();

    internal void PresetNameGotFocus() =>
      this.presetController.NameGotFocus();

    internal void BindingTypeSelectionChanged() =>
      this.presetController.BindingTypeSelectionChanged();

    internal void SaveBinding() =>
      this.presetController.SaveBinding();

    internal void BindingSelectionChanged() =>
      this.presetController.BindingSelectionChanged();

    internal void DeleteSelectedBinding() =>
      this.presetController.DeleteSelectedBinding();

    internal void BeginBindingEdit() =>
      this.presetController.BeginBindingEdit();

    internal void CancelBindingEdit() =>
      this.presetController.CancelBindingEdit();
  }
}
