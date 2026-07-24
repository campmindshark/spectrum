using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Spectrum.Base;

namespace Spectrum {

  /**
   * Owns preset identity and presentation across the MIDI preset list, the
   * device-assignment picker, and the binding editor. The device controller
   * asks for stable preset IDs and does not depend on either list's position.
   */
  internal sealed class MidiPresetUiController {
    private readonly SpectrumConfiguration config;
    private readonly MidiPresetSetupView view;
    private readonly ComboBox devicePresetPicker;
    private readonly Grid deviceNewNameContainer;
    private readonly TextBox deviceNewName;
    private readonly MidiPresetEditor presetEditor;
    private readonly Func<string, string, bool> confirmDestructiveAction;
    private readonly Action presetsChanged;
    private readonly MidiBindingUiController bindingController;
    private readonly List<int> presetIds = new();
    private int? currentlyEditingPreset;

    internal MidiPresetUiController(
      SpectrumConfiguration config,
      MidiPresetSetupView view,
      ComboBox devicePresetPicker,
      Grid deviceNewNameContainer,
      TextBox deviceNewName,
      MidiBindingSetupView bindingView,
      Func<string, string, bool> confirmDestructiveAction,
      Action presetsChanged
    ) {
      this.config = config ??
        throw new ArgumentNullException(nameof(config));
      this.view = view ??
        throw new ArgumentNullException(nameof(view));
      this.devicePresetPicker = devicePresetPicker ??
        throw new ArgumentNullException(nameof(devicePresetPicker));
      this.deviceNewNameContainer = deviceNewNameContainer ??
        throw new ArgumentNullException(
          nameof(deviceNewNameContainer));
      this.deviceNewName = deviceNewName ??
        throw new ArgumentNullException(nameof(deviceNewName));
      this.confirmDestructiveAction = confirmDestructiveAction ??
        throw new ArgumentNullException(
          nameof(confirmDestructiveAction));
      this.presetsChanged = presetsChanged ??
        throw new ArgumentNullException(nameof(presetsChanged));
      this.presetEditor = new MidiPresetEditor(config);
      this.bindingController = new MidiBindingUiController(
        config,
        bindingView,
        confirmDestructiveAction);
    }

    internal void Start() {
      this.LoadPresets();
    }

    internal void DevicePresetSelectionChanged() {
      Visibility previous = this.deviceNewNameContainer.Visibility;
      this.deviceNewNameContainer.Visibility =
        this.devicePresetPicker.SelectedIndex == this.presetIds.Count
          ? Visibility.Visible
          : Visibility.Collapsed;
      if (previous != this.deviceNewNameContainer.Visibility &&
          this.deviceNewNameContainer.Visibility == Visibility.Visible) {
        this.deviceNewName.Focus();
      }
    }

    internal void DevicePresetNameLostFocus() {
      if (string.IsNullOrEmpty(this.deviceNewName.Text.Trim())) {
        RestorePresetNamePlaceholder(this.deviceNewName);
      }
    }

    internal void DevicePresetNameGotFocus() =>
      BeginPresetNameEntry(this.deviceNewName);

    internal bool TryResolveDevicePreset(out int presetId) {
      int selectedIndex = this.devicePresetPicker.SelectedIndex;
      if (selectedIndex < 0) {
        this.devicePresetPicker.Focus();
        presetId = default;
        return false;
      }
      if (selectedIndex < this.presetIds.Count) {
        presetId = this.presetIds[selectedIndex];
        return true;
      }
      if (selectedIndex != this.presetIds.Count) {
        this.LoadPresets();
        this.devicePresetPicker.Focus();
        presetId = default;
        return false;
      }

      MidiPreset? newPreset = this.AddNewPreset(
        this.deviceNewName.Text);
      if (newPreset == null) {
        this.deviceNewName.Focus();
        presetId = default;
        return false;
      }
      presetId = newPreset.id;
      return true;
    }

    internal void ResetDevicePresetEntry() {
      this.devicePresetPicker.SelectedIndex = -1;
      RestorePresetNamePlaceholder(this.deviceNewName);
      this.deviceNewNameContainer.Visibility = Visibility.Collapsed;
    }

    internal void SelectPreset(int presetId) {
      int presetIndex = this.presetIds.IndexOf(presetId);
      if (presetIndex < 0) {
        this.LoadPresets();
        presetIndex = this.presetIds.IndexOf(presetId);
      }
      this.view.Presets.SelectedIndex = presetIndex;
    }

    internal void RefreshDeletionState(int presetId) {
      int selectedIndex = this.view.Presets.SelectedIndex;
      if (selectedIndex >= 0 &&
          selectedIndex < this.presetIds.Count &&
          this.presetIds[selectedIndex] == presetId) {
        this.view.DeletePreset.IsEnabled =
          this.presetEditor.CanDeletePreset(presetId);
      }
    }

    internal void Save() {
      if (this.currentlyEditingPreset.HasValue) {
        int presetId = this.currentlyEditingPreset.Value;
        if (!this.presetEditor.TryRenamePreset(
            presetId,
            this.view.Name.Text,
            out MidiPreset? renamed)) {
          this.view.Name.Focus();
          return;
        }
        int presetIndex = this.presetIds.IndexOf(presetId);
        if (presetIndex < 0) {
          this.LoadPresets();
          this.presetsChanged();
          this.CancelEdit();
          return;
        }
        this.view.Presets.Items[presetIndex] = renamed.Name;
        this.devicePresetPicker.Items[presetIndex] = renamed.Name;
        this.view.Presets.SelectedIndex = presetIndex;
        this.presetsChanged();
        this.CancelEdit();
        return;
      }

      if (this.AddNewPreset(this.view.Name.Text) == null) {
        this.view.Name.Focus();
        return;
      }
      RestorePresetNamePlaceholder(this.view.Name);
    }

    internal void DeleteSelected() {
      int selectedIndex = this.view.Presets.SelectedIndex;
      if (selectedIndex < 0 ||
          selectedIndex >= this.presetIds.Count) {
        return;
      }
      string name =
        this.view.Presets.SelectedItem?.ToString() ??
        "this preset";
      if (!this.confirmDestructiveAction(
          $"Delete {name}? This cannot be undone.",
          "Delete MIDI preset")) {
        return;
      }
      int presetId = this.presetIds[selectedIndex];
      if (!this.presetEditor.TryDeletePreset(presetId)) {
        this.view.DeletePreset.IsEnabled = false;
        return;
      }

      this.presetIds.RemoveAt(selectedIndex);
      this.devicePresetPicker.Items.RemoveAt(selectedIndex);
      this.view.Presets.Items.RemoveAt(selectedIndex);
      this.view.Presets.SelectedIndex = -1;
      this.ApplyNoSelection();
    }

    internal void SelectionChanged() {
      this.CancelEdit();

      int selectedIndex = this.view.Presets.SelectedIndex;
      if (selectedIndex < 0 ||
          selectedIndex >= this.presetIds.Count) {
        this.ApplyNoSelection();
        return;
      }

      int presetId = this.presetIds[selectedIndex];
      this.view.DeletePreset.IsEnabled =
        this.presetEditor.CanDeletePreset(presetId);
      this.view.ClonePreset.IsEnabled = true;
      this.view.RenamePreset.IsEnabled = true;
      this.bindingController.ShowPreset(presetId);
    }

    internal void CloneSelected() {
      int selectedIndex = this.view.Presets.SelectedIndex;
      if (selectedIndex < 0 ||
          selectedIndex >= this.presetIds.Count) {
        return;
      }
      int presetId = this.presetIds[selectedIndex];
      if (this.presetEditor.TryClonePreset(
          presetId, out MidiPreset? clone)) {
        this.AddPresetToControls(clone);
      }
    }

    internal void BeginRename() {
      int selectedIndex = this.view.Presets.SelectedIndex;
      if (selectedIndex < 0 ||
          selectedIndex >= this.presetIds.Count) {
        return;
      }
      int presetId = this.presetIds[selectedIndex];
      if (!this.config.midiPresets.TryGetValue(
          presetId, out MidiPresetView? preset)) {
        this.LoadPresets();
        return;
      }

      this.currentlyEditingPreset = presetId;
      this.view.EditLabel.Content = "Rename preset";
      this.view.Name.Width = 120;
      this.view.Save.Content = "Save";
      this.view.Save.Margin = new Thickness(0, 0, 55, 0);
      this.view.Cancel.Visibility = Visibility.Visible;
      this.view.Name.Text = preset.Name ?? string.Empty;
      this.view.Name.Focus();
      this.view.Name.SelectionStart = this.view.Name.Text.Length;
      this.view.Name.SelectionLength = 0;
    }

    internal void CancelEdit() {
      if (!this.currentlyEditingPreset.HasValue) {
        return;
      }
      this.currentlyEditingPreset = null;
      this.view.EditLabel.Content = "Add preset";
      this.view.Name.Width = 140;
      this.view.Save.Content = "Add preset";
      this.view.Save.Margin = new Thickness(0);
      this.view.Cancel.Visibility = Visibility.Collapsed;
      RestorePresetNamePlaceholder(this.view.Name);
    }

    internal void NameLostFocus() {
      if (string.IsNullOrEmpty(this.view.Name.Text.Trim())) {
        RestorePresetNamePlaceholder(this.view.Name);
      }
    }

    internal void NameGotFocus() =>
      BeginPresetNameEntry(this.view.Name);

    internal void BindingTypeSelectionChanged() =>
      this.bindingController.BindingTypeSelectionChanged();

    internal void SaveBinding() {
      if (this.view.Presets.SelectedIndex < 0) {
        this.view.Presets.Focus();
        return;
      }
      this.bindingController.Save();
    }

    internal void BindingSelectionChanged() =>
      this.bindingController.SelectionChanged();

    internal void DeleteSelectedBinding() =>
      this.bindingController.DeleteSelected();

    internal void BeginBindingEdit() =>
      this.bindingController.BeginEdit();

    internal void CancelBindingEdit() =>
      this.bindingController.CancelEdit();

    private void LoadPresets() {
      this.devicePresetPicker.Items.Clear();
      this.view.Presets.Items.Clear();
      this.presetIds.Clear();
      foreach (KeyValuePair<int, MidiPresetView> pair in
          this.config.midiPresets) {
        MidiPresetView preset = pair.Value;
        this.devicePresetPicker.Items.Add(preset.Name);
        this.presetIds.Add(preset.Id);
        this.view.Presets.Items.Add(preset.Name);
      }
      this.devicePresetPicker.Items.Add("New preset");
      this.ApplyNoSelection();
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
      this.devicePresetPicker.Items.Insert(
        this.devicePresetPicker.Items.Count - 1,
        preset.Name);
      this.presetIds.Add(preset.id);
      this.view.Presets.Items.Add(preset.Name);
    }

    private void ApplyNoSelection() {
      this.view.DeletePreset.IsEnabled = false;
      this.view.ClonePreset.IsEnabled = false;
      this.view.RenamePreset.IsEnabled = false;
      this.bindingController.ShowPreset(null);
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
  }
}
