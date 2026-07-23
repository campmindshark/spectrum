using System;
using System.Globalization;
using Spectrum.Base;
using static Spectrum.LayerPipeline.Tests.TestAssertions;

namespace Spectrum.LayerPipeline.Tests {

  public static class MidiBindingEditorTests {

    public static void Register(Action<string, Action> run) {
      run(nameof(MidiBindingDraftsCreateTypedModels),
        MidiBindingDraftsCreateTypedModels);
      run(nameof(MidiBindingValidationIdentifiesFields),
        MidiBindingValidationIdentifiesFields);
    }

    private static void MidiBindingDraftsCreateTypedModels() {
      string fractional = 1.5.ToString(CultureInfo.CurrentCulture);
      MidiBindingDraft[] drafts = {
        new MidiBindingDraft {
          BindingName = " Tap ",
          BindingType = 0,
          TapTempoButtonType = 2,
          TapTempoButtonIndex = "32",
        },
        new MidiBindingDraft {
          BindingName = "Continuous",
          BindingType = 1,
          ContinuousKnobIndex = "4",
          ContinuousKnobPropertyName =
            nameof(Configuration.domeBrightness),
          ContinuousKnobStartValue = "0",
          ContinuousKnobEndValue = fractional,
        },
        new MidiBindingDraft {
          BindingName = "Discrete",
          BindingType = 2,
          DiscreteKnobIndex = "5",
          DiscreteKnobPropertyName =
            nameof(Configuration.domeTestPattern),
          DiscreteKnobNumPossibleValues = "6",
        },
        new MidiBindingDraft {
          BindingName = "Logarithmic",
          BindingType = 3,
          LogarithmicKnobIndex = "6",
          LogarithmicKnobPropertyName =
            nameof(Configuration.domeBrightness),
          LogarithmicKnobNumPossibleValues = "8",
          LogarithmicKnobStartValue = fractional,
        },
        new MidiBindingDraft {
          BindingName = "ADSR",
          BindingType = 4,
          AdsrLevelDriverIndexRangeStart = "48",
        },
      };

      var bindings = new IMidiBindingConfig[drafts.Length];
      for (int i = 0; i < drafts.Length; i++) {
        bool created = MidiBindingEditor.TryCreate(
          drafts[i],
          out IMidiBindingConfig? binding,
          out MidiBindingValidationError? error);
        Assert(created && binding != null && error == null,
          "valid MIDI draft " + i + " was rejected");
        bindings[i] = binding;
      }

      Assert(bindings[0] is TapTempoMidiBindingConfig tap &&
          tap.BindingName == "Tap" &&
          tap.buttonType == MidiCommandType.Note &&
          tap.buttonIndex == 32,
        "tap-tempo draft mapped incorrectly");
      Assert(bindings[1] is ContinuousKnobMidiBindingConfig continuous &&
          continuous.knobIndex == 4 &&
          continuous.configPropertyName ==
            nameof(Configuration.domeBrightness) &&
          continuous.startValue == 0 &&
          continuous.endValue == 1.5,
        "continuous-knob draft mapped incorrectly");
      Assert(bindings[2] is DiscreteKnobMidiBindingConfig discrete &&
          discrete.knobIndex == 5 &&
          discrete.configPropertyName ==
            nameof(Configuration.domeTestPattern) &&
          discrete.numPossibleValues == 6,
        "discrete-knob draft mapped incorrectly");
      Assert(
        bindings[3] is DiscreteLogarithmicKnobMidiBindingConfig logarithmic &&
          logarithmic.knobIndex == 6 &&
          logarithmic.configPropertyName ==
            nameof(Configuration.domeBrightness) &&
          logarithmic.numPossibleValues == 8 &&
          logarithmic.startValue == 1.5,
        "logarithmic-knob draft mapped incorrectly");
      Assert(bindings[4] is AdsrLevelDriverMidiBindingConfig adsr &&
          adsr.indexRangeStart == 48,
        "ADSR draft mapped incorrectly");
      Assert(
        MidiBindingEditor.CommandTypeIndex(MidiCommandType.Knob) == 0 &&
        MidiBindingEditor.CommandTypeIndex(MidiCommandType.Program) == 1 &&
        MidiBindingEditor.CommandTypeIndex(MidiCommandType.Note) == 2,
        "MIDI command-type indices are not reversible");
    }

    private static void MidiBindingValidationIdentifiesFields() {
      AssertError(
        new MidiBindingDraft(),
        MidiBindingEditorField.BindingName);
      AssertError(
        new MidiBindingDraft {
          BindingName = "Missing type",
        },
        MidiBindingEditorField.BindingType);
      AssertError(
        new MidiBindingDraft {
          BindingName = "Bad property",
          BindingType = 1,
          ContinuousKnobPropertyName =
            nameof(Configuration.domeTestPattern),
          ContinuousKnobIndex = "1",
          ContinuousKnobStartValue = "0",
          ContinuousKnobEndValue = "1",
        },
        MidiBindingEditorField.ContinuousKnobPropertyName);
      AssertError(
        new MidiBindingDraft {
          BindingName = "Reverse range",
          BindingType = 1,
          ContinuousKnobPropertyName =
            nameof(Configuration.domeBrightness),
          ContinuousKnobIndex = "1",
          ContinuousKnobStartValue = "2",
          ContinuousKnobEndValue = "1",
        },
        MidiBindingEditorField.ContinuousKnobEndValue);
      AssertError(
        new MidiBindingDraft {
          BindingName = "No values",
          BindingType = 2,
          DiscreteKnobPropertyName =
            nameof(Configuration.domeTestPattern),
          DiscreteKnobIndex = "1",
          DiscreteKnobNumPossibleValues = "0",
        },
        MidiBindingEditorField.DiscreteKnobNumPossibleValues);
      AssertError(
        new MidiBindingDraft {
          BindingName = "Not finite",
          BindingType = 3,
          LogarithmicKnobPropertyName =
            nameof(Configuration.domeBrightness),
          LogarithmicKnobIndex = "1",
          LogarithmicKnobNumPossibleValues = "4",
          LogarithmicKnobStartValue = "NaN",
        },
        MidiBindingEditorField.LogarithmicKnobStartValue);
    }

    private static void AssertError(
      MidiBindingDraft draft, MidiBindingEditorField expectedField
    ) {
      bool created = MidiBindingEditor.TryCreate(
        draft,
        out IMidiBindingConfig? binding,
        out MidiBindingValidationError? error);
      Assert(!created && binding == null && error != null &&
          error.Field == expectedField &&
          !string.IsNullOrWhiteSpace(error.Message),
        "MIDI validation did not identify " + expectedField);
    }
  }
}
