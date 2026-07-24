using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Spectrum.Base;

namespace Spectrum {

  internal sealed class MidiDeviceEntry {
    public int DeviceID { get; init; }
    public string DeviceName { get; init; } = string.Empty;
    public string PresetName { get; init; } = string.Empty;
  }

  internal sealed class MidiBindingEntry {
    public string BindingName { get; init; } = string.Empty;
    public string BindingTypeName { get; init; } = string.Empty;
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

  internal sealed record MidiBindingSetupView(
    ListView Bindings,
    Button EditBinding,
    Button DeleteBinding,
    Label EditLabel,
    TextBox Name,
    ComboBox Type,
    StackPanel TapTempoPanel,
    ComboBox TapTempoButtonType,
    TextBox TapTempoButtonIndex,
    StackPanel ContinuousKnobPanel,
    TextBox ContinuousKnobIndex,
    TextBox ContinuousKnobPropertyName,
    TextBox ContinuousKnobStartValue,
    TextBox ContinuousKnobEndValue,
    StackPanel DiscreteKnobPanel,
    TextBox DiscreteKnobIndex,
    TextBox DiscreteKnobPropertyName,
    TextBox DiscreteKnobNumPossibleValues,
    StackPanel LogarithmicKnobPanel,
    TextBox LogarithmicKnobIndex,
    TextBox LogarithmicKnobPropertyName,
    TextBox LogarithmicKnobNumPossibleValues,
    TextBox LogarithmicKnobStartValue,
    StackPanel AdsrLevelDriverPanel,
    TextBox AdsrLevelDriverIndexRangeStart,
    TextBlock ValidationMessage,
    Button Save,
    Button Cancel
  );

  internal sealed record MidiSetupView(
    MidiDeviceSetupView Device,
    MidiPresetSetupView Preset,
    MidiBindingSetupView Binding
  );

  /**
   * Owns the WPF MIDI setup surface. Persisted identity and validation rules
   * remain in the UI-neutral editors; this controller owns list projections,
   * selection state, edit modes, field presentation, and confirmation
   * boundaries.
   */
  internal sealed class MidiSetupUiController {
    private readonly SpectrumConfiguration config;
    private readonly MidiSetupView view;
    private readonly MidiPresetEditor presetEditor;
    private readonly Func<int> deviceCount;
    private readonly Func<int, string> getDeviceName;
    private readonly Func<string, string, bool> confirmDestructiveAction;
    private readonly List<int> deviceIndices = new();
    private readonly List<int> presetIndices = new();
    private int? currentlyEditingPreset;
    private int? currentlyEditingBinding;

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
        throw new ArgumentNullException(nameof(confirmDestructiveAction));
      this.presetEditor = new MidiPresetEditor(config);
    }

    internal void Start() {
      this.RefreshDevices();
      this.LoadPresets();
    }

    internal void RefreshDevices() {
      object? currentDevice = this.view.Device.AvailableDevices.SelectedItem;
      this.view.Device.AvailableDevices.Items.Clear();
      this.deviceIndices.Clear();
      int count = this.deviceCount();
      for (int deviceId = 0; deviceId < count; deviceId++) {
        if (this.config.midiDevices.ContainsKey(deviceId)) {
          continue;
        }
        this.view.Device.AvailableDevices.Items.Add(
          this.getDeviceName(deviceId));
        this.deviceIndices.Add(deviceId);
      }
      this.view.Device.AvailableDevices.SelectedItem = currentDevice;
      this.LoadConfiguredDevices();
    }

    internal void NewDevicePresetSelectionChanged() {
      Visibility previous =
        this.view.Device.NewPresetNameContainer.Visibility;
      this.view.Device.NewPresetNameContainer.Visibility =
        this.view.Device.NewDevicePreset.SelectedIndex ==
          this.presetIndices.Count
          ? Visibility.Visible
          : Visibility.Collapsed;
      if (previous != this.view.Device.NewPresetNameContainer.Visibility &&
          this.view.Device.NewPresetNameContainer.Visibility ==
            Visibility.Visible) {
        this.view.Device.NewPresetName.Focus();
      }
    }

    internal void NewDevicePresetNameLostFocus() {
      if (string.IsNullOrEmpty(
          this.view.Device.NewPresetName.Text.Trim())) {
        RestorePresetNamePlaceholder(this.view.Device.NewPresetName);
      }
    }

    internal void NewDevicePresetNameGotFocus() =>
      BeginPresetNameEntry(this.view.Device.NewPresetName);

    internal void AddDevice() {
      if (this.view.Device.NewDevicePreset.SelectedIndex < 0) {
        this.view.Device.NewDevicePreset.Focus();
        return;
      }
      if (this.view.Device.AvailableDevices.SelectedIndex < 0) {
        this.view.Device.AvailableDevices.Focus();
        return;
      }

      int presetId;
      if (this.view.Device.NewDevicePreset.SelectedIndex >=
          this.presetIndices.Count) {
        MidiPreset? newPreset = this.AddNewPreset(
          this.view.Device.NewPresetName.Text);
        if (newPreset == null) {
          this.view.Device.NewPresetName.Focus();
          return;
        }
        presetId = newPreset.id;
      } else {
        presetId = this.presetIndices[
          this.view.Device.NewDevicePreset.SelectedIndex];
      }

      int selectedDeviceIndex =
        this.view.Device.AvailableDevices.SelectedIndex;
      int deviceId = this.deviceIndices[selectedDeviceIndex];
      if (!this.presetEditor.TryAssignDevice(deviceId, presetId)) {
        this.RefreshDevices();
        return;
      }

      this.view.Device.AvailableDevices.SelectedIndex = -1;
      this.view.Device.NewDevicePreset.SelectedIndex = -1;
      RestorePresetNamePlaceholder(this.view.Device.NewPresetName);
      this.view.Device.NewPresetNameContainer.Visibility =
        Visibility.Collapsed;
      this.RefreshDevices();
      this.RefreshSelectedPresetDeletionState(presetId);
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
      this.RefreshSelectedPresetDeletionState(presetId);
    }

    internal void LoadSelectedDevicePreset() {
      if (this.view.Device.ConfiguredDevices.SelectedItem is
          MidiDeviceEntry selected) {
        this.view.Preset.Presets.SelectedItem = selected.PresetName;
      }
    }

    internal void ConfiguredDeviceSelectionChanged() {
      bool selected =
        this.view.Device.ConfiguredDevices.SelectedIndex >= 0;
      this.view.Device.DeleteDevice.IsEnabled = selected;
      this.view.Device.LoadPreset.IsEnabled = selected;
    }

    internal void SavePreset() {
      if (this.currentlyEditingPreset.HasValue) {
        int presetId = this.currentlyEditingPreset.Value;
        if (!this.presetEditor.TryRenamePreset(
            presetId,
            this.view.Preset.Name.Text,
            out MidiPreset? renamed)) {
          this.view.Preset.Name.Focus();
          return;
        }
        int presetIndex = this.presetIndices.IndexOf(presetId);
        if (presetIndex < 0) {
          this.LoadPresets();
          this.LoadConfiguredDevices();
          RestorePresetNamePlaceholder(this.view.Preset.Name);
          return;
        }
        this.LoadConfiguredDevices();
        this.view.Preset.Presets.Items[presetIndex] = renamed.Name;
        this.view.Device.NewDevicePreset.Items[presetIndex] =
          renamed.Name;
      } else if (this.AddNewPreset(this.view.Preset.Name.Text) == null) {
        this.view.Preset.Name.Focus();
        return;
      }
      RestorePresetNamePlaceholder(this.view.Preset.Name);
    }

    internal void DeleteSelectedPreset() {
      int selectedIndex = this.view.Preset.Presets.SelectedIndex;
      if (selectedIndex < 0) {
        return;
      }
      string name =
        this.view.Preset.Presets.SelectedItem?.ToString() ??
        "this preset";
      if (!this.confirmDestructiveAction(
          $"Delete {name}? This cannot be undone.",
          "Delete MIDI preset")) {
        return;
      }
      int presetId = this.presetIndices[selectedIndex];
      if (!this.presetEditor.TryDeletePreset(presetId)) {
        this.view.Preset.DeletePreset.IsEnabled = false;
        return;
      }
      this.presetIndices.RemoveAt(selectedIndex);
      this.view.Device.NewDevicePreset.Items.RemoveAt(selectedIndex);
      this.view.Preset.Presets.Items.RemoveAt(selectedIndex);
    }

    internal void PresetSelectionChanged() {
      this.view.Binding.Bindings.Items.Clear();
      this.CancelPresetEdit();
      this.CancelBindingEdit();

      int selectedIndex = this.view.Preset.Presets.SelectedIndex;
      if (selectedIndex < 0) {
        this.view.Preset.DeletePreset.IsEnabled = false;
        this.view.Preset.ClonePreset.IsEnabled = false;
        this.view.Preset.RenamePreset.IsEnabled = false;
        this.view.Binding.Save.IsEnabled = false;
        return;
      }

      int presetId = this.presetIndices[selectedIndex];
      this.view.Preset.DeletePreset.IsEnabled =
        this.presetEditor.CanDeletePreset(presetId);
      this.view.Preset.ClonePreset.IsEnabled = true;
      this.view.Preset.RenamePreset.IsEnabled = true;
      this.view.Binding.Save.IsEnabled = true;
      foreach (IMidiBindingView binding in
          this.config.midiPresets[presetId].Bindings) {
        this.view.Binding.Bindings.Items.Add(new MidiBindingEntry {
          BindingName =
            binding.BindingName ?? "(unnamed binding)",
          BindingTypeName = this.BindingTypeName(
            binding.BindingType),
        });
      }
    }

    internal void CloneSelectedPreset() {
      int selectedIndex = this.view.Preset.Presets.SelectedIndex;
      if (selectedIndex < 0) {
        return;
      }
      int presetId = this.presetIndices[selectedIndex];
      if (this.presetEditor.TryClonePreset(
          presetId, out MidiPreset? clone)) {
        this.AddPresetToControls(clone);
      }
    }

    internal void BeginPresetRename() {
      int selectedIndex = this.view.Preset.Presets.SelectedIndex;
      if (selectedIndex < 0) {
        return;
      }
      int presetId = this.presetIndices[selectedIndex];
      this.currentlyEditingPreset = presetId;
      this.view.Preset.EditLabel.Content = "Rename preset";
      this.view.Preset.Name.Width = 120;
      this.view.Preset.Save.Content = "Save";
      this.view.Preset.Save.Margin = new Thickness(0, 0, 55, 0);
      this.view.Preset.Cancel.Visibility = Visibility.Visible;
      this.view.Preset.Name.Text =
        this.config.midiPresets[presetId].Name ?? string.Empty;
      this.view.Preset.Name.Focus();
      this.view.Preset.Name.SelectionStart =
        this.view.Preset.Name.Text.Length;
      this.view.Preset.Name.SelectionLength = 0;
    }

    internal void CancelPresetEdit() {
      if (!this.currentlyEditingPreset.HasValue) {
        return;
      }
      this.currentlyEditingPreset = null;
      this.view.Preset.EditLabel.Content = "Add preset";
      this.view.Preset.Name.Width = 140;
      this.view.Preset.Save.Content = "Add preset";
      this.view.Preset.Save.Margin = new Thickness(0);
      this.view.Preset.Cancel.Visibility = Visibility.Collapsed;
      RestorePresetNamePlaceholder(this.view.Preset.Name);
    }

    internal void PresetNameLostFocus() {
      if (string.IsNullOrEmpty(this.view.Preset.Name.Text.Trim())) {
        RestorePresetNamePlaceholder(this.view.Preset.Name);
      }
    }

    internal void PresetNameGotFocus() =>
      BeginPresetNameEntry(this.view.Preset.Name);

    internal void BindingTypeSelectionChanged() {
      this.ClearBindingValidation();
      int selectedType = this.view.Binding.Type.SelectedIndex;
      this.view.Binding.TapTempoPanel.Visibility =
        selectedType == 0 ? Visibility.Visible : Visibility.Collapsed;
      this.view.Binding.ContinuousKnobPanel.Visibility =
        selectedType == 1 ? Visibility.Visible : Visibility.Collapsed;
      this.view.Binding.DiscreteKnobPanel.Visibility =
        selectedType == 2 ? Visibility.Visible : Visibility.Collapsed;
      this.view.Binding.LogarithmicKnobPanel.Visibility =
        selectedType == 3 ? Visibility.Visible : Visibility.Collapsed;
      this.view.Binding.AdsrLevelDriverPanel.Visibility =
        selectedType == 4 ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void SaveBinding() {
      int selectedPresetIndex =
        this.view.Preset.Presets.SelectedIndex;
      if (selectedPresetIndex < 0) {
        this.view.Preset.Presets.Focus();
        return;
      }

      MidiBindingDraft draft = this.CaptureBindingDraft();
      if (!MidiBindingEditor.TryCreate(
          draft,
          out IMidiBindingConfig? newBinding,
          out MidiBindingValidationError? error)) {
        this.ShowBindingValidation(error);
        return;
      }

      bool editing = this.currentlyEditingBinding.HasValue;
      int bindingIndex =
        this.currentlyEditingBinding.GetValueOrDefault();
      int presetId = this.presetIndices[selectedPresetIndex];
      MidiPreset preset = this.config.midiPresets[presetId].ToPreset();
      if (editing) {
        if (bindingIndex < 0 ||
            bindingIndex >= preset.Bindings.Count) {
          this.CancelBindingEdit();
          this.PresetSelectionChanged();
          return;
        }
        preset.Bindings[bindingIndex] = newBinding;
      } else {
        preset.Bindings.Add(newBinding);
      }
      this.config.UpsertMidiPreset(presetId, preset);

      string bindingName = newBinding.BindingName ?? string.Empty;
      var entry = new MidiBindingEntry {
        BindingName = bindingName,
        BindingTypeName = this.BindingTypeName(draft.BindingType),
      };
      if (editing) {
        this.view.Binding.Bindings.Items[bindingIndex] = entry;
      } else {
        this.view.Binding.Bindings.Items.Add(entry);
      }

      this.ClearBindingFields(draft.BindingType);
      this.currentlyEditingBinding = null;
      this.view.Binding.EditLabel.Content = "Add binding";
      this.view.Binding.Save.Content = "Add binding";
      this.view.Binding.Cancel.Visibility = Visibility.Collapsed;
      this.view.Binding.Type.SelectedIndex = -1;
      this.view.Binding.Name.Text = string.Empty;
      this.ClearBindingValidation();
    }

    internal void BindingSelectionChanged() {
      bool selected =
        this.view.Preset.Presets.SelectedIndex >= 0 &&
        this.view.Binding.Bindings.SelectedIndex >= 0;
      this.view.Binding.DeleteBinding.IsEnabled = selected;
      this.view.Binding.EditBinding.IsEnabled = selected;
      this.CancelBindingEdit();
    }

    internal void DeleteSelectedBinding() {
      int presetIndex = this.view.Preset.Presets.SelectedIndex;
      int bindingIndex = this.view.Binding.Bindings.SelectedIndex;
      if (presetIndex < 0 || bindingIndex < 0) {
        return;
      }
      string name =
        (this.view.Binding.Bindings.SelectedItem as MidiBindingEntry)
          ?.BindingName ?? "this binding";
      if (!this.confirmDestructiveAction(
          $"Delete binding “{name}”?",
          "Delete MIDI binding")) {
        return;
      }

      int presetId = this.presetIndices[presetIndex];
      MidiPreset preset = this.config.midiPresets[presetId].ToPreset();
      if (bindingIndex >= preset.Bindings.Count) {
        this.PresetSelectionChanged();
        return;
      }
      preset.Bindings.RemoveAt(bindingIndex);
      this.config.UpsertMidiPreset(presetId, preset);
      this.view.Binding.Bindings.Items.RemoveAt(bindingIndex);
    }

    internal void BeginBindingEdit() {
      int presetIndex = this.view.Preset.Presets.SelectedIndex;
      int bindingIndex = this.view.Binding.Bindings.SelectedIndex;
      if (presetIndex < 0 || bindingIndex < 0) {
        return;
      }
      int presetId = this.presetIndices[presetIndex];
      IMidiBindingView binding =
        this.config.midiPresets[presetId].Bindings[bindingIndex];
      this.currentlyEditingBinding = bindingIndex;

      this.view.Binding.EditLabel.Content = "Edit binding";
      this.view.Binding.Save.Content = "Save";
      this.view.Binding.Cancel.Visibility = Visibility.Visible;
      this.view.Binding.Name.Text =
        binding.BindingName ?? string.Empty;
      this.view.Binding.Name.Focus();
      this.view.Binding.Name.SelectionStart =
        this.view.Binding.Name.Text.Length;
      this.view.Binding.Name.SelectionLength = 0;

      this.view.Binding.Type.SelectedIndex = binding.BindingType;
      switch (binding) {
        case TapTempoMidiBindingView tapTempo:
          this.view.Binding.TapTempoButtonType.SelectedIndex =
            MidiBindingEditor.CommandTypeIndex(tapTempo.ButtonType);
          this.view.Binding.TapTempoButtonIndex.Text =
            tapTempo.ButtonIndex.ToString();
          break;
        case ContinuousKnobMidiBindingView continuous:
          this.view.Binding.ContinuousKnobIndex.Text =
            continuous.KnobIndex.ToString();
          this.view.Binding.ContinuousKnobPropertyName.Text =
            continuous.ConfigPropertyName;
          this.view.Binding.ContinuousKnobStartValue.Text =
            continuous.StartValue.ToString();
          this.view.Binding.ContinuousKnobEndValue.Text =
            continuous.EndValue.ToString();
          break;
        case DiscreteKnobMidiBindingView discrete:
          this.view.Binding.DiscreteKnobIndex.Text =
            discrete.KnobIndex.ToString();
          this.view.Binding.DiscreteKnobPropertyName.Text =
            discrete.ConfigPropertyName;
          this.view.Binding.DiscreteKnobNumPossibleValues.Text =
            discrete.NumPossibleValues.ToString();
          break;
        case DiscreteLogarithmicKnobMidiBindingView logarithmic:
          this.view.Binding.LogarithmicKnobIndex.Text =
            logarithmic.KnobIndex.ToString();
          this.view.Binding.LogarithmicKnobPropertyName.Text =
            logarithmic.ConfigPropertyName;
          this.view.Binding.LogarithmicKnobNumPossibleValues.Text =
            logarithmic.NumPossibleValues.ToString();
          this.view.Binding.LogarithmicKnobStartValue.Text =
            logarithmic.StartValue.ToString();
          break;
        case AdsrLevelDriverMidiBindingView adsr:
          this.view.Binding.AdsrLevelDriverIndexRangeStart.Text =
            adsr.IndexRangeStart.ToString();
          break;
      }
    }

    internal void CancelBindingEdit() {
      if (!this.currentlyEditingBinding.HasValue) {
        return;
      }
      this.currentlyEditingBinding = null;
      this.view.Binding.EditLabel.Content = "Add binding";
      this.view.Binding.Save.Content = "Add binding";
      this.view.Binding.Cancel.Visibility = Visibility.Collapsed;
      this.view.Binding.Name.Text = string.Empty;
      this.view.Binding.Type.SelectedIndex = -1;
      this.ClearAllBindingFields();
      this.ClearBindingValidation();
    }

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
            DeviceName = deviceName,
            PresetName = presetName,
          });
      }
    }

    private void LoadPresets() {
      this.view.Device.NewDevicePreset.Items.Clear();
      this.view.Preset.Presets.Items.Clear();
      this.presetIndices.Clear();
      foreach (KeyValuePair<int, MidiPresetView> pair in
          this.config.midiPresets) {
        MidiPresetView preset = pair.Value;
        this.view.Device.NewDevicePreset.Items.Add(preset.Name);
        this.presetIndices.Add(preset.Id);
        this.view.Preset.Presets.Items.Add(preset.Name);
      }
      this.view.Device.NewDevicePreset.Items.Add("New preset");
    }

    private MidiPreset? AddNewPreset(string rawName) {
      if (!this.presetEditor.TryCreatePreset(
          rawName, out MidiPreset? preset)) {
        return null;
      }
      this.AddPresetToControls(preset);
      return preset;
    }

    private void AddPresetToControls(MidiPreset preset) {
      this.view.Device.NewDevicePreset.Items.Insert(
        this.view.Device.NewDevicePreset.Items.Count - 1,
        preset.Name);
      this.presetIndices.Add(preset.id);
      this.view.Preset.Presets.Items.Add(preset.Name);
    }

    private void RefreshSelectedPresetDeletionState(int presetId) {
      int selectedPresetIndex =
        this.view.Preset.Presets.SelectedIndex;
      if (selectedPresetIndex >= 0 &&
          this.presetIndices[selectedPresetIndex] == presetId) {
        this.view.Preset.DeletePreset.IsEnabled =
          this.presetEditor.CanDeletePreset(presetId);
      }
    }

    private static void BeginPresetNameEntry(TextBox textBox) {
      textBox.Foreground = Brushes.Black;
      textBox.FontStyle = FontStyles.Normal;
      if (string.Equals(
          textBox.Text.Trim(),
          MidiPresetEditor.NewPresetPlaceholder,
          StringComparison.Ordinal)) {
        textBox.Text = string.Empty;
      }
    }

    private static void RestorePresetNamePlaceholder(TextBox textBox) {
      textBox.Text = MidiPresetEditor.NewPresetPlaceholder;
      textBox.Foreground = Brushes.Gray;
      textBox.FontStyle = FontStyles.Italic;
    }

    private string BindingTypeName(int bindingType) {
      if (bindingType >= 0 &&
          bindingType < this.view.Binding.Type.Items.Count &&
          this.view.Binding.Type.Items[bindingType] is
            ComboBoxItem { Content: string name }) {
        return name;
      }
      return "Unknown binding";
    }

    private MidiBindingDraft CaptureBindingDraft() =>
      new MidiBindingDraft {
        BindingName = this.view.Binding.Name.Text,
        BindingType = this.view.Binding.Type.SelectedIndex,
        TapTempoButtonType =
          this.view.Binding.TapTempoButtonType.SelectedIndex,
        TapTempoButtonIndex =
          this.view.Binding.TapTempoButtonIndex.Text,
        ContinuousKnobIndex =
          this.view.Binding.ContinuousKnobIndex.Text,
        ContinuousKnobPropertyName =
          this.view.Binding.ContinuousKnobPropertyName.Text,
        ContinuousKnobStartValue =
          this.view.Binding.ContinuousKnobStartValue.Text,
        ContinuousKnobEndValue =
          this.view.Binding.ContinuousKnobEndValue.Text,
        DiscreteKnobIndex =
          this.view.Binding.DiscreteKnobIndex.Text,
        DiscreteKnobPropertyName =
          this.view.Binding.DiscreteKnobPropertyName.Text,
        DiscreteKnobNumPossibleValues =
          this.view.Binding.DiscreteKnobNumPossibleValues.Text,
        LogarithmicKnobIndex =
          this.view.Binding.LogarithmicKnobIndex.Text,
        LogarithmicKnobPropertyName =
          this.view.Binding.LogarithmicKnobPropertyName.Text,
        LogarithmicKnobNumPossibleValues =
          this.view.Binding.LogarithmicKnobNumPossibleValues.Text,
        LogarithmicKnobStartValue =
          this.view.Binding.LogarithmicKnobStartValue.Text,
        AdsrLevelDriverIndexRangeStart =
          this.view.Binding.AdsrLevelDriverIndexRangeStart.Text,
      };

    private void ShowBindingValidation(
      MidiBindingValidationError error
    ) {
      this.view.Binding.ValidationMessage.Text = error.Message;
      this.view.Binding.ValidationMessage.Visibility =
        Visibility.Visible;
      Control control = error.Field switch {
        MidiBindingEditorField.BindingName =>
          this.view.Binding.Name,
        MidiBindingEditorField.BindingType =>
          this.view.Binding.Type,
        MidiBindingEditorField.TapTempoButtonType =>
          this.view.Binding.TapTempoButtonType,
        MidiBindingEditorField.TapTempoButtonIndex =>
          this.view.Binding.TapTempoButtonIndex,
        MidiBindingEditorField.ContinuousKnobIndex =>
          this.view.Binding.ContinuousKnobIndex,
        MidiBindingEditorField.ContinuousKnobPropertyName =>
          this.view.Binding.ContinuousKnobPropertyName,
        MidiBindingEditorField.ContinuousKnobStartValue =>
          this.view.Binding.ContinuousKnobStartValue,
        MidiBindingEditorField.ContinuousKnobEndValue =>
          this.view.Binding.ContinuousKnobEndValue,
        MidiBindingEditorField.DiscreteKnobIndex =>
          this.view.Binding.DiscreteKnobIndex,
        MidiBindingEditorField.DiscreteKnobPropertyName =>
          this.view.Binding.DiscreteKnobPropertyName,
        MidiBindingEditorField.DiscreteKnobNumPossibleValues =>
          this.view.Binding.DiscreteKnobNumPossibleValues,
        MidiBindingEditorField.LogarithmicKnobIndex =>
          this.view.Binding.LogarithmicKnobIndex,
        MidiBindingEditorField.LogarithmicKnobPropertyName =>
          this.view.Binding.LogarithmicKnobPropertyName,
        MidiBindingEditorField.LogarithmicKnobNumPossibleValues =>
          this.view.Binding.LogarithmicKnobNumPossibleValues,
        MidiBindingEditorField.LogarithmicKnobStartValue =>
          this.view.Binding.LogarithmicKnobStartValue,
        MidiBindingEditorField.AdsrLevelDriverIndexRangeStart =>
          this.view.Binding.AdsrLevelDriverIndexRangeStart,
        _ => this.view.Binding.Type,
      };
      control.Focus();
    }

    private void ClearBindingValidation() {
      this.view.Binding.ValidationMessage.Text = string.Empty;
      this.view.Binding.ValidationMessage.Visibility =
        Visibility.Collapsed;
    }

    private void ClearBindingFields(int bindingType) {
      switch (bindingType) {
        case 0:
          this.view.Binding.TapTempoButtonType.SelectedIndex = -1;
          this.view.Binding.TapTempoButtonIndex.Text = string.Empty;
          break;
        case 1:
          this.view.Binding.ContinuousKnobIndex.Text = string.Empty;
          this.view.Binding.ContinuousKnobPropertyName.Text =
            string.Empty;
          this.view.Binding.ContinuousKnobStartValue.Text =
            string.Empty;
          this.view.Binding.ContinuousKnobEndValue.Text =
            string.Empty;
          break;
        case 2:
          this.view.Binding.DiscreteKnobIndex.Text = string.Empty;
          this.view.Binding.DiscreteKnobPropertyName.Text =
            string.Empty;
          this.view.Binding.DiscreteKnobNumPossibleValues.Text =
            string.Empty;
          break;
        case 3:
          this.view.Binding.LogarithmicKnobIndex.Text = string.Empty;
          this.view.Binding.LogarithmicKnobPropertyName.Text =
            string.Empty;
          this.view.Binding.LogarithmicKnobNumPossibleValues.Text =
            string.Empty;
          this.view.Binding.LogarithmicKnobStartValue.Text =
            string.Empty;
          break;
        case 4:
          this.view.Binding.AdsrLevelDriverIndexRangeStart.Text =
            string.Empty;
          break;
      }
    }

    private void ClearAllBindingFields() {
      for (int bindingType = 0; bindingType <= 4; bindingType++) {
        this.ClearBindingFields(bindingType);
      }
    }
  }
}
