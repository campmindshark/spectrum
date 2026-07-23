using System;
using System.Collections.Generic;
using Spectrum.Base;
using static Spectrum.LayerPipeline.Tests.TestAssertions;

namespace Spectrum.LayerPipeline.Tests {

  public static class MidiPresetEditorTests {
    public static void Register(Action<string, Action> run) {
      run("MIDI preset editor owns stable sparse identities and names",
        IdentityAndNaming);
      run("MIDI preset editor clones bindings without configuration aliases",
        CloneIsolation);
      run("MIDI preset editor protects assigned presets during device lifecycle",
        DeviceAssignmentLifecycle);
    }

    private static void IdentityAndNaming() {
      var config = new global::Spectrum.SpectrumConfiguration();
      config.ReplaceMidiPresets(new Dictionary<int, MidiPreset> {
        [4] = new MidiPreset { id = 4, Name = "Warm" },
        [9] = new MidiPreset { id = 9, Name = "Cool" },
      });
      var editor = new MidiPresetEditor(config);

      Assert(editor.TryCreatePreset("  Prism  ", out MidiPreset? created) &&
          created.id == 10 &&
          created.Name == "Prism" &&
          config.midiPresets[10].Name == "Prism",
        "new preset did not receive the next stable sparse identity");
      Assert(!editor.TryCreatePreset("Warm", out _) &&
          !editor.TryCreatePreset(
            MidiPresetEditor.NewPresetPlaceholder, out _),
        "preset editor accepted a duplicate or placeholder name");

      Assert(editor.TryRenamePreset(
          9, "  Ocean  ", out MidiPreset? renamed) &&
          renamed.id == 9 &&
          config.midiPresets[9].Name == "Ocean" &&
          config.midiPresets[4].Name == "Warm",
        "rename did not preserve the selected preset identity");
      Assert(!editor.TryRenamePreset(9, "Warm", out _),
        "rename accepted another preset's name");
    }

    private static void CloneIsolation() {
      var source = new MidiPreset {
        id = 2,
        Name = "Pulse",
        Bindings = new List<IMidiBindingConfig> {
          new TapTempoMidiBindingConfig {
            BindingName = "Tap",
            buttonType = MidiCommandType.Note,
            buttonIndex = 7,
          },
        },
      };
      var config = new global::Spectrum.SpectrumConfiguration();
      config.ReplaceMidiPresets(new Dictionary<int, MidiPreset> {
        [2] = source,
        [6] = new MidiPreset { id = 6, Name = "Pulse (clone)" },
      });
      var editor = new MidiPresetEditor(config);

      Assert(editor.TryClonePreset(2, out MidiPreset? clone) &&
          clone.id == 7 &&
          clone.Name == "Pulse (clone 2)" &&
          clone.Bindings.Count == 1 &&
          config.midiPresets[7].Bindings.Length == 1,
        "clone did not allocate a unique identity, name, and binding graph");
      clone.Bindings.Clear();
      source.Bindings.Clear();
      Assert(config.midiPresets[2].Bindings.Length == 1 &&
          config.midiPresets[7].Bindings.Length == 1,
        "source or returned clone aliases persisted binding state");
    }

    private static void DeviceAssignmentLifecycle() {
      var config = new global::Spectrum.SpectrumConfiguration();
      config.ReplaceMidiPresets(new Dictionary<int, MidiPreset> {
        [3] = new MidiPreset { id = 3, Name = "Controller" },
      });
      var editor = new MidiPresetEditor(config);

      Assert(!editor.TryAssignDevice(5, 99) &&
          editor.TryAssignDevice(5, 3) &&
          !editor.TryAssignDevice(5, 3) &&
          config.midiDevices[5] == 3,
        "device assignment did not validate preset and device identities");
      Assert(!editor.CanDeletePreset(3) &&
          !editor.TryDeletePreset(3),
        "assigned preset was deletable");
      Assert(editor.TryRemoveDevice(5, out int presetId) &&
          presetId == 3 &&
          !config.midiDevices.ContainsKey(5) &&
          editor.CanDeletePreset(3) &&
          editor.TryDeletePreset(3) &&
          !config.midiPresets.ContainsKey(3),
        "device removal did not release the preset for deletion");
    }
  }
}
