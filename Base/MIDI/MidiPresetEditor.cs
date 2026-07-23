using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Spectrum.Base {

  /**
   * Owns MIDI preset identity, naming, cloning, deletion, and device assignment.
   * UI layers render the resulting configuration snapshots but do not recreate
   * these persistence rules or mutate the dictionaries themselves.
   */
  public sealed class MidiPresetEditor {
    public const string NewPresetPlaceholder = "New preset name";

    private readonly Configuration configuration;
    private readonly ConfigurationEditor editor;

    public MidiPresetEditor(Configuration configuration) {
      this.configuration = configuration ??
        throw new ArgumentNullException(nameof(configuration));
      this.editor = configuration as ConfigurationEditor ??
        throw new ArgumentException(
          "MIDI preset editing requires mutable configuration ownership.",
          nameof(configuration));
    }

    public bool TryCreatePreset(
      string? rawName,
      [NotNullWhen(true)] out MidiPreset? preset
    ) {
      if (!this.TryNormalizeName(rawName, null, out string? name)) {
        preset = null;
        return false;
      }
      preset = new MidiPreset {
        id = this.NextPresetId(),
        Name = name,
      };
      this.editor.UpsertMidiPreset(preset.id, preset);
      return true;
    }

    public bool TryRenamePreset(
      int presetId,
      string? rawName,
      [NotNullWhen(true)] out MidiPreset? renamed
    ) {
      if (!this.configuration.midiPresets.TryGetValue(
          presetId, out MidiPresetView? existing) ||
          existing == null ||
          !this.TryNormalizeName(rawName, presetId, out string? name)) {
        renamed = null;
        return false;
      }
      renamed = existing.ToPreset();
      renamed.Name = name;
      this.editor.UpsertMidiPreset(presetId, renamed);
      return true;
    }

    public bool TryClonePreset(
      int presetId,
      [NotNullWhen(true)] out MidiPreset? clone
    ) {
      if (!this.configuration.midiPresets.TryGetValue(
          presetId, out MidiPresetView? existing) ||
          existing == null) {
        clone = null;
        return false;
      }
      clone = existing.ToPreset();
      clone.id = this.NextPresetId();
      string baseName = existing.Name ?? "";
      string name = baseName + " (clone)";
      int suffix = 1;
      while (this.PresetNameExists(name)) {
        name = baseName + " (clone " + ++suffix + ")";
      }
      clone.Name = name;
      this.editor.UpsertMidiPreset(clone.id, clone);
      return true;
    }

    public bool CanDeletePreset(int presetId) =>
      this.configuration.midiPresets.ContainsKey(presetId) &&
      !this.configuration.midiDevices.ContainsValue(presetId);

    public bool TryDeletePreset(int presetId) {
      if (!this.CanDeletePreset(presetId)) {
        return false;
      }
      this.editor.RemoveMidiPreset(presetId);
      return true;
    }

    public bool TryAssignDevice(int deviceId, int presetId) {
      if (this.configuration.midiDevices.ContainsKey(deviceId) ||
          !this.configuration.midiPresets.ContainsKey(presetId)) {
        return false;
      }
      var devices = new Dictionary<int, int>(
        this.configuration.midiDevices) {
        [deviceId] = presetId,
      };
      this.editor.ReplaceMidiDevices(devices);
      return true;
    }

    public bool TryRemoveDevice(int deviceId, out int presetId) {
      if (!this.configuration.midiDevices.TryGetValue(
          deviceId, out presetId)) {
        return false;
      }
      var devices = new Dictionary<int, int>(
        this.configuration.midiDevices);
      devices.Remove(deviceId);
      this.editor.ReplaceMidiDevices(devices);
      return true;
    }

    public bool PresetNameExists(string name) =>
      this.PresetNameExists(name, null);

    private int NextPresetId() =>
      this.configuration.midiPresets.Count == 0
        ? 0
        : this.configuration.midiPresets.Keys.Max() + 1;

    private bool TryNormalizeName(
      string? rawName,
      int? exceptPresetId,
      [NotNullWhen(true)] out string? name
    ) {
      name = rawName?.Trim();
      if (string.IsNullOrEmpty(name) ||
          string.Equals(name, NewPresetPlaceholder, StringComparison.Ordinal) ||
          this.PresetNameExists(name, exceptPresetId)) {
        name = null;
        return false;
      }
      return true;
    }

    private bool PresetNameExists(string name, int? exceptPresetId) {
      foreach (KeyValuePair<int, MidiPresetView> pair in
          this.configuration.midiPresets) {
        if (pair.Key != exceptPresetId &&
            string.Equals(
              pair.Value.Name, name, StringComparison.Ordinal)) {
          return true;
        }
      }
      return false;
    }
  }
}
