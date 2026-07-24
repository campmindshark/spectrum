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

  /**
   * Owns MIDI device discovery, configured and available projections,
   * assignment, removal, and device action state. Preset identity and
   * presentation remain outside this controller.
   */
  internal sealed class MidiDeviceUiController {
    private readonly SpectrumConfiguration config;
    private readonly MidiDeviceSetupView view;
    private readonly MidiPresetEditor presetEditor;
    private readonly Func<int> deviceCount;
    private readonly Func<int, string> getDeviceName;
    private readonly Func<string, string, bool> confirmDestructiveAction;
    private readonly List<int> deviceIds = new();

    internal MidiDeviceUiController(
      SpectrumConfiguration config,
      MidiDeviceSetupView view,
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
    }

    internal void Start() {
      this.RefreshDevices();
    }

    internal void RefreshDevices() {
      object? currentDevice =
        this.view.AvailableDevices.SelectedItem;
      this.view.AvailableDevices.Items.Clear();
      this.deviceIds.Clear();
      int count = this.deviceCount();
      for (int deviceId = 0; deviceId < count; deviceId++) {
        if (this.config.midiDevices.ContainsKey(deviceId)) {
          continue;
        }
        this.view.AvailableDevices.Items.Add(
          this.getDeviceName(deviceId));
        this.deviceIds.Add(deviceId);
      }
      this.view.AvailableDevices.SelectedItem = currentDevice;
      this.LoadConfiguredDevices();
    }

    internal bool EnsureAvailableDeviceSelected() {
      int selectedDeviceIndex =
        this.view.AvailableDevices.SelectedIndex;
      if (selectedDeviceIndex >= 0 &&
          selectedDeviceIndex < this.deviceIds.Count) {
        return true;
      }
      this.view.AvailableDevices.Focus();
      return false;
    }

    internal bool TryAssignSelectedDevice(int presetId) {
      int selectedDeviceIndex =
        this.view.AvailableDevices.SelectedIndex;
      if (selectedDeviceIndex < 0 ||
          selectedDeviceIndex >= this.deviceIds.Count) {
        this.view.AvailableDevices.Focus();
        return false;
      }

      int deviceId = this.deviceIds[selectedDeviceIndex];
      if (!this.presetEditor.TryAssignDevice(deviceId, presetId)) {
        this.RefreshDevices();
        return false;
      }

      this.view.AvailableDevices.SelectedIndex = -1;
      this.RefreshDevices();
      return true;
    }

    internal bool TryDeleteSelectedDevice(out int presetId) {
      presetId = default;
      if (this.view.ConfiguredDevices.SelectedItem is not
          MidiDeviceEntry selected) {
        return false;
      }
      if (!this.confirmDestructiveAction(
          $"Remove {selected.DeviceName} from Spectrum? " +
            "The preset will be kept.",
          "Remove MIDI device")) {
        return false;
      }
      if (!this.presetEditor.TryRemoveDevice(
          selected.DeviceID, out presetId)) {
        this.LoadConfiguredDevices();
        return false;
      }
      this.RefreshDevices();
      return true;
    }

    internal bool TryGetSelectedPreset(out int presetId) {
      if (this.view.ConfiguredDevices.SelectedItem is
          MidiDeviceEntry selected) {
        presetId = selected.PresetID;
        return true;
      }
      presetId = default;
      return false;
    }

    internal void SelectionChanged() {
      bool selected =
        this.view.ConfiguredDevices.SelectedIndex >= 0;
      this.view.DeleteDevice.IsEnabled = selected;
      this.view.LoadPreset.IsEnabled = selected;
    }

    internal void LoadConfiguredDevices() {
      this.view.ConfiguredDevices.Items.Clear();
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
        this.view.ConfiguredDevices.Items.Add(
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
