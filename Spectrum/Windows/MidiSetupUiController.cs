using System;
using System.Collections.Generic;
using System.Windows.Controls;
using Spectrum.Base;

namespace Spectrum {

  internal sealed class MidiDeviceEntry {
    public int DeviceID { get; init; }
    public int PresetID { get; init; }
    public string DeviceName { get; init; } = string.Empty;
    public string PresetName { get; init; } = string.Empty;
  }

  internal sealed record MidiDeviceSetupView(
    ListView ConfiguredDevices,
    Button LoadPreset,
    Button DeleteDevice,
    ComboBox NewDevicePreset,
    Grid NewPresetNameContainer,
    TextBox NewPresetName,
    ComboBox AvailableDevices
  );

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
   * Owns MIDI device discovery and assignment. Preset and binding identity,
   * presentation, editing, and persistence live behind the preset controller.
   */
  internal sealed class MidiSetupUiController {
    private readonly SpectrumConfiguration config;
    private readonly MidiSetupView view;
    private readonly MidiPresetEditor presetEditor;
    private readonly Func<int> deviceCount;
    private readonly Func<int, string> getDeviceName;
    private readonly Func<string, string, bool> confirmDestructiveAction;
    private readonly MidiPresetUiController presetController;
    private readonly List<int> deviceIds = new();

    internal MidiSetupUiController(
      SpectrumConfiguration config,
      MidiSetupView view,
      Func<int> deviceCount,
      Func<int, string> getDeviceName,
      Func<string, string, bool> confirmDestructiveAction
    ) {
      this.config = config ??
        throw new ArgumentNullException(nameof(config));
      this.view = view ??
        throw new ArgumentNullException(nameof(view));
      this.deviceCount = deviceCount ??
        throw new ArgumentNullException(nameof(deviceCount));
      this.getDeviceName = getDeviceName ??
        throw new ArgumentNullException(nameof(getDeviceName));
      this.confirmDestructiveAction = confirmDestructiveAction ??
        throw new ArgumentNullException(
          nameof(confirmDestructiveAction));
      this.presetEditor = new MidiPresetEditor(config);
      this.presetController = new MidiPresetUiController(
        config,
        view.Preset,
        view.Device.NewDevicePreset,
        view.Device.NewPresetNameContainer,
        view.Device.NewPresetName,
        view.Binding,
        confirmDestructiveAction,
        this.LoadConfiguredDevices);
    }

    internal void Start() {
      this.RefreshDevices();
      this.presetController.Start();
    }

    internal void RefreshDevices() {
      object? currentDevice =
        this.view.Device.AvailableDevices.SelectedItem;
      this.view.Device.AvailableDevices.Items.Clear();
      this.deviceIds.Clear();
      int count = this.deviceCount();
      for (int deviceId = 0; deviceId < count; deviceId++) {
        if (this.config.midiDevices.ContainsKey(deviceId)) {
          continue;
        }
        this.view.Device.AvailableDevices.Items.Add(
          this.getDeviceName(deviceId));
        this.deviceIds.Add(deviceId);
      }
      this.view.Device.AvailableDevices.SelectedItem = currentDevice;
      this.LoadConfiguredDevices();
    }

    internal void NewDevicePresetSelectionChanged() =>
      this.presetController.DevicePresetSelectionChanged();

    internal void NewDevicePresetNameLostFocus() =>
      this.presetController.DevicePresetNameLostFocus();

    internal void NewDevicePresetNameGotFocus() =>
      this.presetController.DevicePresetNameGotFocus();

    internal void AddDevice() {
      if (this.view.Device.NewDevicePreset.SelectedIndex < 0) {
        this.view.Device.NewDevicePreset.Focus();
        return;
      }
      int selectedDeviceIndex =
        this.view.Device.AvailableDevices.SelectedIndex;
      if (selectedDeviceIndex < 0 ||
          selectedDeviceIndex >= this.deviceIds.Count) {
        this.view.Device.AvailableDevices.Focus();
        return;
      }
      if (!this.presetController.TryResolveDevicePreset(
          out int presetId)) {
        return;
      }

      int deviceId = this.deviceIds[selectedDeviceIndex];
      if (!this.presetEditor.TryAssignDevice(deviceId, presetId)) {
        this.RefreshDevices();
        return;
      }

      this.view.Device.AvailableDevices.SelectedIndex = -1;
      this.presetController.ResetDevicePresetEntry();
      this.RefreshDevices();
      this.presetController.RefreshDeletionState(presetId);
    }

    internal void DeleteSelectedDevice() {
      if (this.view.Device.ConfiguredDevices.SelectedItem is not
          MidiDeviceEntry selected) {
        return;
      }
      if (!this.confirmDestructiveAction(
          $"Remove {selected.DeviceName} from Spectrum? " +
            "The preset will be kept.",
          "Remove MIDI device")) {
        return;
      }
      if (!this.presetEditor.TryRemoveDevice(
          selected.DeviceID, out int presetId)) {
        this.LoadConfiguredDevices();
        return;
      }
      this.RefreshDevices();
      this.presetController.RefreshDeletionState(presetId);
    }

    internal void LoadSelectedDevicePreset() {
      if (this.view.Device.ConfiguredDevices.SelectedItem is
          MidiDeviceEntry selected) {
        this.presetController.SelectPreset(selected.PresetID);
      }
    }

    internal void ConfiguredDeviceSelectionChanged() {
      bool selected =
        this.view.Device.ConfiguredDevices.SelectedIndex >= 0;
      this.view.Device.DeleteDevice.IsEnabled = selected;
      this.view.Device.LoadPreset.IsEnabled = selected;
    }

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

    private void LoadConfiguredDevices() {
      this.view.Device.ConfiguredDevices.Items.Clear();
      int count = this.deviceCount();
      foreach (KeyValuePair<int, int> pair in
          this.config.midiDevices) {
        string deviceName = pair.Key >= 0 && pair.Key < count
          ? this.getDeviceName(pair.Key)
          : "< DISCONNECTED >";
        string presetName =
          this.config.midiPresets.TryGetValue(
            pair.Value, out MidiPresetView? preset)
            ? preset.Name ?? "(unnamed preset)"
            : "(missing preset)";
        this.view.Device.ConfiguredDevices.Items.Add(
          new MidiDeviceEntry {
            DeviceID = pair.Key,
            PresetID = pair.Value,
            DeviceName = deviceName,
            PresetName = presetName,
          });
      }
    }
  }
}
