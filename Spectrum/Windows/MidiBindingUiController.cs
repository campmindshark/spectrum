using System;
using System.Windows;
using System.Windows.Controls;
using Spectrum.Base;

namespace Spectrum {

  internal sealed class MidiBindingEntry {
    public string BindingName { get; init; } = string.Empty;
    public string BindingTypeName { get; init; } = string.Empty;
  }

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

  /**
   * Owns the binding half of the WPF MIDI setup surface. The controller keeps
   * the selected preset identity explicit so binding edits cannot accidentally
   * depend on list positions owned by the parent device/preset controller.
   */
  internal sealed class MidiBindingUiController {
    private readonly SpectrumConfiguration config;
    private readonly MidiBindingSetupView view;
    private readonly Func<string, string, bool> confirmDestructiveAction;
    private int? selectedPresetId;
    private int? currentlyEditingBinding;

    internal MidiBindingUiController(
      SpectrumConfiguration config,
      MidiBindingSetupView view,
      Func<string, string, bool> confirmDestructiveAction
    ) {
      this.config = config ??
        throw new ArgumentNullException(nameof(config));
      this.view = view ??
        throw new ArgumentNullException(nameof(view));
      this.confirmDestructiveAction = confirmDestructiveAction ??
        throw new ArgumentNullException(
          nameof(confirmDestructiveAction));
    }

    internal void ShowPreset(int? presetId) {
      this.view.Bindings.Items.Clear();
      this.CancelEdit();
      this.selectedPresetId = null;

      if (!presetId.HasValue ||
          !this.config.midiPresets.TryGetValue(
            presetId.Value, out MidiPresetView? preset)) {
        this.view.Save.IsEnabled = false;
        return;
      }

      this.selectedPresetId = presetId.Value;
      this.view.Save.IsEnabled = true;
      foreach (IMidiBindingView binding in preset.Bindings) {
        this.view.Bindings.Items.Add(new MidiBindingEntry {
          BindingName =
            binding.BindingName ?? "(unnamed binding)",
          BindingTypeName = this.BindingTypeName(
            binding.BindingType),
        });
      }
    }

    internal void BindingTypeSelectionChanged() {
      this.ClearValidation();
      int selectedType = this.view.Type.SelectedIndex;
      this.view.TapTempoPanel.Visibility =
        selectedType == 0 ? Visibility.Visible : Visibility.Collapsed;
      this.view.ContinuousKnobPanel.Visibility =
        selectedType == 1 ? Visibility.Visible : Visibility.Collapsed;
      this.view.DiscreteKnobPanel.Visibility =
        selectedType == 2 ? Visibility.Visible : Visibility.Collapsed;
      this.view.LogarithmicKnobPanel.Visibility =
        selectedType == 3 ? Visibility.Visible : Visibility.Collapsed;
      this.view.AdsrLevelDriverPanel.Visibility =
        selectedType == 4 ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void Save() {
      if (!this.selectedPresetId.HasValue) {
        return;
      }

      MidiBindingDraft draft = this.CaptureDraft();
      if (!MidiBindingEditor.TryCreate(
          draft,
          out IMidiBindingConfig? newBinding,
          out MidiBindingValidationError? error)) {
        this.ShowValidation(error);
        return;
      }

      int presetId = this.selectedPresetId.Value;
      if (!this.config.midiPresets.TryGetValue(
          presetId, out MidiPresetView? presetView)) {
        this.ShowPreset(null);
        return;
      }

      bool editing = this.currentlyEditingBinding.HasValue;
      int bindingIndex =
        this.currentlyEditingBinding.GetValueOrDefault();
      MidiPreset preset = presetView.ToPreset();
      if (editing) {
        if (bindingIndex < 0 ||
            bindingIndex >= preset.Bindings.Count) {
          this.ShowPreset(presetId);
          return;
        }
        preset.Bindings[bindingIndex] = newBinding;
      } else {
        preset.Bindings.Add(newBinding);
      }
      this.config.UpsertMidiPreset(presetId, preset);

      var entry = new MidiBindingEntry {
        BindingName = newBinding.BindingName ?? string.Empty,
        BindingTypeName = this.BindingTypeName(draft.BindingType),
      };
      if (editing) {
        this.view.Bindings.Items[bindingIndex] = entry;
      } else {
        this.view.Bindings.Items.Add(entry);
      }

      this.ClearFields(draft.BindingType);
      this.currentlyEditingBinding = null;
      this.view.EditLabel.Content = "Add binding";
      this.view.Save.Content = "Add binding";
      this.view.Cancel.Visibility = Visibility.Collapsed;
      this.view.Type.SelectedIndex = -1;
      this.view.Name.Text = string.Empty;
      this.ClearValidation();
    }

    internal void SelectionChanged() {
      bool selected =
        this.selectedPresetId.HasValue &&
        this.view.Bindings.SelectedIndex >= 0;
      this.view.DeleteBinding.IsEnabled = selected;
      this.view.EditBinding.IsEnabled = selected;
      this.CancelEdit();
    }

    internal void DeleteSelected() {
      if (!this.selectedPresetId.HasValue) {
        return;
      }
      int bindingIndex = this.view.Bindings.SelectedIndex;
      if (bindingIndex < 0) {
        return;
      }
      string name =
        (this.view.Bindings.SelectedItem as MidiBindingEntry)
          ?.BindingName ?? "this binding";
      if (!this.confirmDestructiveAction(
          $"Delete binding “{name}”?",
          "Delete MIDI binding")) {
        return;
      }

      int presetId = this.selectedPresetId.Value;
      if (!this.config.midiPresets.TryGetValue(
          presetId, out MidiPresetView? presetView)) {
        this.ShowPreset(null);
        return;
      }
      MidiPreset preset = presetView.ToPreset();
      if (bindingIndex >= preset.Bindings.Count) {
        this.ShowPreset(presetId);
        return;
      }
      preset.Bindings.RemoveAt(bindingIndex);
      this.config.UpsertMidiPreset(presetId, preset);
      this.view.Bindings.Items.RemoveAt(bindingIndex);
    }

    internal void BeginEdit() {
      if (!this.selectedPresetId.HasValue) {
        return;
      }
      int bindingIndex = this.view.Bindings.SelectedIndex;
      if (bindingIndex < 0) {
        return;
      }
      int presetId = this.selectedPresetId.Value;
      if (!this.config.midiPresets.TryGetValue(
          presetId, out MidiPresetView? preset) ||
          bindingIndex >= preset.Bindings.Length) {
        this.ShowPreset(
          this.config.midiPresets.ContainsKey(presetId)
            ? presetId
            : null);
        return;
      }
      IMidiBindingView binding = preset.Bindings[bindingIndex];
      this.currentlyEditingBinding = bindingIndex;

      this.view.EditLabel.Content = "Edit binding";
      this.view.Save.Content = "Save";
      this.view.Cancel.Visibility = Visibility.Visible;
      this.view.Name.Text = binding.BindingName ?? string.Empty;
      this.view.Name.Focus();
      this.view.Name.SelectionStart = this.view.Name.Text.Length;
      this.view.Name.SelectionLength = 0;

      this.view.Type.SelectedIndex = binding.BindingType;
      switch (binding) {
        case TapTempoMidiBindingView tapTempo:
          this.view.TapTempoButtonType.SelectedIndex =
            MidiBindingEditor.CommandTypeIndex(tapTempo.ButtonType);
          this.view.TapTempoButtonIndex.Text =
            tapTempo.ButtonIndex.ToString();
          break;
        case ContinuousKnobMidiBindingView continuous:
          this.view.ContinuousKnobIndex.Text =
            continuous.KnobIndex.ToString();
          this.view.ContinuousKnobPropertyName.Text =
            continuous.ConfigPropertyName;
          this.view.ContinuousKnobStartValue.Text =
            continuous.StartValue.ToString();
          this.view.ContinuousKnobEndValue.Text =
            continuous.EndValue.ToString();
          break;
        case DiscreteKnobMidiBindingView discrete:
          this.view.DiscreteKnobIndex.Text =
            discrete.KnobIndex.ToString();
          this.view.DiscreteKnobPropertyName.Text =
            discrete.ConfigPropertyName;
          this.view.DiscreteKnobNumPossibleValues.Text =
            discrete.NumPossibleValues.ToString();
          break;
        case DiscreteLogarithmicKnobMidiBindingView logarithmic:
          this.view.LogarithmicKnobIndex.Text =
            logarithmic.KnobIndex.ToString();
          this.view.LogarithmicKnobPropertyName.Text =
            logarithmic.ConfigPropertyName;
          this.view.LogarithmicKnobNumPossibleValues.Text =
            logarithmic.NumPossibleValues.ToString();
          this.view.LogarithmicKnobStartValue.Text =
            logarithmic.StartValue.ToString();
          break;
        case AdsrLevelDriverMidiBindingView adsr:
          this.view.AdsrLevelDriverIndexRangeStart.Text =
            adsr.IndexRangeStart.ToString();
          break;
      }
    }

    internal void CancelEdit() {
      if (!this.currentlyEditingBinding.HasValue) {
        return;
      }
      this.currentlyEditingBinding = null;
      this.view.EditLabel.Content = "Add binding";
      this.view.Save.Content = "Add binding";
      this.view.Cancel.Visibility = Visibility.Collapsed;
      this.view.Name.Text = string.Empty;
      this.view.Type.SelectedIndex = -1;
      this.ClearAllFields();
      this.ClearValidation();
    }

    private string BindingTypeName(int bindingType) {
      if (bindingType >= 0 &&
          bindingType < this.view.Type.Items.Count &&
          this.view.Type.Items[bindingType] is
            ComboBoxItem { Content: string name }) {
        return name;
      }
      return "Unknown binding";
    }

    private MidiBindingDraft CaptureDraft() =>
      new MidiBindingDraft {
        BindingName = this.view.Name.Text,
        BindingType = this.view.Type.SelectedIndex,
        TapTempoButtonType =
          this.view.TapTempoButtonType.SelectedIndex,
        TapTempoButtonIndex =
          this.view.TapTempoButtonIndex.Text,
        ContinuousKnobIndex =
          this.view.ContinuousKnobIndex.Text,
        ContinuousKnobPropertyName =
          this.view.ContinuousKnobPropertyName.Text,
        ContinuousKnobStartValue =
          this.view.ContinuousKnobStartValue.Text,
        ContinuousKnobEndValue =
          this.view.ContinuousKnobEndValue.Text,
        DiscreteKnobIndex =
          this.view.DiscreteKnobIndex.Text,
        DiscreteKnobPropertyName =
          this.view.DiscreteKnobPropertyName.Text,
        DiscreteKnobNumPossibleValues =
          this.view.DiscreteKnobNumPossibleValues.Text,
        LogarithmicKnobIndex =
          this.view.LogarithmicKnobIndex.Text,
        LogarithmicKnobPropertyName =
          this.view.LogarithmicKnobPropertyName.Text,
        LogarithmicKnobNumPossibleValues =
          this.view.LogarithmicKnobNumPossibleValues.Text,
        LogarithmicKnobStartValue =
          this.view.LogarithmicKnobStartValue.Text,
        AdsrLevelDriverIndexRangeStart =
          this.view.AdsrLevelDriverIndexRangeStart.Text,
      };

    private void ShowValidation(MidiBindingValidationError error) {
      this.view.ValidationMessage.Text = error.Message;
      this.view.ValidationMessage.Visibility = Visibility.Visible;
      Control control = error.Field switch {
        MidiBindingEditorField.BindingName => this.view.Name,
        MidiBindingEditorField.BindingType => this.view.Type,
        MidiBindingEditorField.TapTempoButtonType =>
          this.view.TapTempoButtonType,
        MidiBindingEditorField.TapTempoButtonIndex =>
          this.view.TapTempoButtonIndex,
        MidiBindingEditorField.ContinuousKnobIndex =>
          this.view.ContinuousKnobIndex,
        MidiBindingEditorField.ContinuousKnobPropertyName =>
          this.view.ContinuousKnobPropertyName,
        MidiBindingEditorField.ContinuousKnobStartValue =>
          this.view.ContinuousKnobStartValue,
        MidiBindingEditorField.ContinuousKnobEndValue =>
          this.view.ContinuousKnobEndValue,
        MidiBindingEditorField.DiscreteKnobIndex =>
          this.view.DiscreteKnobIndex,
        MidiBindingEditorField.DiscreteKnobPropertyName =>
          this.view.DiscreteKnobPropertyName,
        MidiBindingEditorField.DiscreteKnobNumPossibleValues =>
          this.view.DiscreteKnobNumPossibleValues,
        MidiBindingEditorField.LogarithmicKnobIndex =>
          this.view.LogarithmicKnobIndex,
        MidiBindingEditorField.LogarithmicKnobPropertyName =>
          this.view.LogarithmicKnobPropertyName,
        MidiBindingEditorField.LogarithmicKnobNumPossibleValues =>
          this.view.LogarithmicKnobNumPossibleValues,
        MidiBindingEditorField.LogarithmicKnobStartValue =>
          this.view.LogarithmicKnobStartValue,
        MidiBindingEditorField.AdsrLevelDriverIndexRangeStart =>
          this.view.AdsrLevelDriverIndexRangeStart,
        _ => this.view.Type,
      };
      control.Focus();
    }

    private void ClearValidation() {
      this.view.ValidationMessage.Text = string.Empty;
      this.view.ValidationMessage.Visibility = Visibility.Collapsed;
    }

    private void ClearFields(int bindingType) {
      switch (bindingType) {
        case 0:
          this.view.TapTempoButtonType.SelectedIndex = -1;
          this.view.TapTempoButtonIndex.Text = string.Empty;
          break;
        case 1:
          this.view.ContinuousKnobIndex.Text = string.Empty;
          this.view.ContinuousKnobPropertyName.Text = string.Empty;
          this.view.ContinuousKnobStartValue.Text = string.Empty;
          this.view.ContinuousKnobEndValue.Text = string.Empty;
          break;
        case 2:
          this.view.DiscreteKnobIndex.Text = string.Empty;
          this.view.DiscreteKnobPropertyName.Text = string.Empty;
          this.view.DiscreteKnobNumPossibleValues.Text = string.Empty;
          break;
        case 3:
          this.view.LogarithmicKnobIndex.Text = string.Empty;
          this.view.LogarithmicKnobPropertyName.Text = string.Empty;
          this.view.LogarithmicKnobNumPossibleValues.Text =
            string.Empty;
          this.view.LogarithmicKnobStartValue.Text = string.Empty;
          break;
        case 4:
          this.view.AdsrLevelDriverIndexRangeStart.Text =
            string.Empty;
          break;
      }
    }

    private void ClearAllFields() {
      for (int bindingType = 0; bindingType <= 4; bindingType++) {
        this.ClearFields(bindingType);
      }
    }
  }
}
