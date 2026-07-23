using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Spectrum.Base {

  public enum MidiBindingEditorField {
    BindingName,
    BindingType,
    TapTempoButtonType,
    TapTempoButtonIndex,
    ContinuousKnobIndex,
    ContinuousKnobPropertyName,
    ContinuousKnobStartValue,
    ContinuousKnobEndValue,
    DiscreteKnobIndex,
    DiscreteKnobPropertyName,
    DiscreteKnobNumPossibleValues,
    LogarithmicKnobIndex,
    LogarithmicKnobPropertyName,
    LogarithmicKnobNumPossibleValues,
    LogarithmicKnobStartValue,
    AdsrLevelDriverIndexRangeStart,
  }

  public sealed class MidiBindingValidationError {

    internal MidiBindingValidationError(
      MidiBindingEditorField field, string message
    ) {
      this.Field = field;
      this.Message = message;
    }

    public MidiBindingEditorField Field { get; }
    public string Message { get; }
  }

  /**
   * UI-neutral text model for one MIDI binding editor form. Keeping raw text
   * here lets native and future web editors share the same parsing and domain
   * validation without throwing exceptions for normal input mistakes.
   */
  public sealed class MidiBindingDraft {
    public string BindingName { get; set; } = "";
    public int BindingType { get; set; } = -1;
    public int TapTempoButtonType { get; set; } = -1;
    public string TapTempoButtonIndex { get; set; } = "";
    public string ContinuousKnobIndex { get; set; } = "";
    public string ContinuousKnobPropertyName { get; set; } = "";
    public string ContinuousKnobStartValue { get; set; } = "";
    public string ContinuousKnobEndValue { get; set; } = "";
    public string DiscreteKnobIndex { get; set; } = "";
    public string DiscreteKnobPropertyName { get; set; } = "";
    public string DiscreteKnobNumPossibleValues { get; set; } = "";
    public string LogarithmicKnobIndex { get; set; } = "";
    public string LogarithmicKnobPropertyName { get; set; } = "";
    public string LogarithmicKnobNumPossibleValues { get; set; } = "";
    public string LogarithmicKnobStartValue { get; set; } = "";
    public string AdsrLevelDriverIndexRangeStart { get; set; } = "";
  }

  /**
   * Parses and validates MIDI editor input into a persistence model. Validation
   * failures identify the exact field and carry an operator-facing message;
   * invalid user input is an expected result, never an exception.
   */
  public static class MidiBindingEditor {

    public static bool TryCreate(
      MidiBindingDraft draft,
      [NotNullWhen(true)] out IMidiBindingConfig? binding,
      [NotNullWhen(false)] out MidiBindingValidationError? error
    ) {
      if (draft == null) {
        throw new ArgumentNullException(nameof(draft));
      }

      binding = null;
      error = null;
      string name = draft.BindingName.Trim();
      if (name.Length == 0) {
        error = Invalid(
          MidiBindingEditorField.BindingName,
          "Enter a name for the MIDI binding.");
        return false;
      }

      switch (draft.BindingType) {
        case 0:
          if (!TryCommandType(
              draft.TapTempoButtonType, out MidiCommandType buttonType)) {
            error = Invalid(
              MidiBindingEditorField.TapTempoButtonType,
              "Choose the MIDI button message type.");
            return false;
          }
          if (!TryInt(
              draft.TapTempoButtonIndex,
              MidiBindingEditorField.TapTempoButtonIndex,
              "Enter a whole-number MIDI button index.",
              out int buttonIndex,
              out error)) {
            return false;
          }
          binding = new TapTempoMidiBindingConfig {
            BindingName = name,
            buttonType = buttonType,
            buttonIndex = buttonIndex,
          };
          return true;

        case 1:
          if (!TryConfigurationProperty(
              draft.ContinuousKnobPropertyName,
              typeof(double),
              MidiBindingEditorField.ContinuousKnobPropertyName,
              out string continuousProperty,
              out error) ||
              !TryInt(
                draft.ContinuousKnobIndex,
                MidiBindingEditorField.ContinuousKnobIndex,
                "Enter a whole-number MIDI knob index.",
                out int continuousIndex,
                out error) ||
              !TryDouble(
                draft.ContinuousKnobStartValue,
                MidiBindingEditorField.ContinuousKnobStartValue,
                "Enter a numeric start value.",
                out double startValue,
                out error) ||
              !TryDouble(
                draft.ContinuousKnobEndValue,
                MidiBindingEditorField.ContinuousKnobEndValue,
                "Enter a numeric end value.",
                out double endValue,
                out error)) {
            return false;
          }
          if (endValue < startValue) {
            error = Invalid(
              MidiBindingEditorField.ContinuousKnobEndValue,
              "The end value must be greater than or equal to the start value.");
            return false;
          }
          binding = new ContinuousKnobMidiBindingConfig {
            BindingName = name,
            knobIndex = continuousIndex,
            configPropertyName = continuousProperty,
            startValue = startValue,
            endValue = endValue,
          };
          return true;

        case 2:
          if (!TryConfigurationProperty(
              draft.DiscreteKnobPropertyName,
              typeof(int),
              MidiBindingEditorField.DiscreteKnobPropertyName,
              out string discreteProperty,
              out error) ||
              !TryInt(
                draft.DiscreteKnobIndex,
                MidiBindingEditorField.DiscreteKnobIndex,
                "Enter a whole-number MIDI knob index.",
                out int discreteIndex,
                out error) ||
              !TryPositiveInt(
                draft.DiscreteKnobNumPossibleValues,
                MidiBindingEditorField.DiscreteKnobNumPossibleValues,
                "Enter a positive number of values.",
                out int discreteValueCount,
                out error)) {
            return false;
          }
          binding = new DiscreteKnobMidiBindingConfig {
            BindingName = name,
            knobIndex = discreteIndex,
            configPropertyName = discreteProperty,
            numPossibleValues = discreteValueCount,
          };
          return true;

        case 3:
          if (!TryConfigurationProperty(
              draft.LogarithmicKnobPropertyName,
              typeof(double),
              MidiBindingEditorField.LogarithmicKnobPropertyName,
              out string logarithmicProperty,
              out error) ||
              !TryInt(
                draft.LogarithmicKnobIndex,
                MidiBindingEditorField.LogarithmicKnobIndex,
                "Enter a whole-number MIDI knob index.",
                out int logarithmicIndex,
                out error) ||
              !TryPositiveInt(
                draft.LogarithmicKnobNumPossibleValues,
                MidiBindingEditorField.LogarithmicKnobNumPossibleValues,
                "Enter a positive number of values.",
                out int logarithmicValueCount,
                out error) ||
              !TryDouble(
                draft.LogarithmicKnobStartValue,
                MidiBindingEditorField.LogarithmicKnobStartValue,
                "Enter a numeric starting value.",
                out double logarithmicStart,
                out error)) {
            return false;
          }
          binding = new DiscreteLogarithmicKnobMidiBindingConfig {
            BindingName = name,
            knobIndex = logarithmicIndex,
            configPropertyName = logarithmicProperty,
            numPossibleValues = logarithmicValueCount,
            startValue = logarithmicStart,
          };
          return true;

        case 4:
          if (!TryInt(
              draft.AdsrLevelDriverIndexRangeStart,
              MidiBindingEditorField.AdsrLevelDriverIndexRangeStart,
              "Enter a whole-number starting MIDI note.",
              out int rangeStart,
              out error)) {
            return false;
          }
          binding = new AdsrLevelDriverMidiBindingConfig {
            BindingName = name,
            indexRangeStart = rangeStart,
          };
          return true;

        default:
          error = Invalid(
            MidiBindingEditorField.BindingType,
            "Choose a MIDI binding type.");
          return false;
      }
    }

    public static int CommandTypeIndex(MidiCommandType commandType) =>
      commandType switch {
        MidiCommandType.Knob => 0,
        MidiCommandType.Program => 1,
        MidiCommandType.Note => 2,
        _ => throw new ArgumentOutOfRangeException(
          nameof(commandType), commandType, "Unknown MIDI command type."),
      };

    private static bool TryCommandType(
      int index, out MidiCommandType commandType
    ) {
      switch (index) {
        case 0:
          commandType = MidiCommandType.Knob;
          return true;
        case 1:
          commandType = MidiCommandType.Program;
          return true;
        case 2:
          commandType = MidiCommandType.Note;
          return true;
        default:
          commandType = default;
          return false;
      }
    }

    private static bool TryConfigurationProperty(
      string raw,
      Type assignedType,
      MidiBindingEditorField field,
      out string propertyName,
      [NotNullWhen(false)] out MidiBindingValidationError? error
    ) {
      propertyName = raw.Trim();
      string? propertyError = MidiBindingConfig.ConfigurationPropertyError(
        propertyName, assignedType);
      if (propertyError == null) {
        error = null;
        return true;
      }
      error = Invalid(field, propertyError);
      return false;
    }

    private static bool TryInt(
      string raw,
      MidiBindingEditorField field,
      string message,
      out int value,
      [NotNullWhen(false)] out MidiBindingValidationError? error
    ) {
      if (int.TryParse(
          raw.Trim(),
          NumberStyles.Integer,
          CultureInfo.CurrentCulture,
          out value)) {
        error = null;
        return true;
      }
      error = Invalid(field, message);
      return false;
    }

    private static bool TryPositiveInt(
      string raw,
      MidiBindingEditorField field,
      string message,
      out int value,
      [NotNullWhen(false)] out MidiBindingValidationError? error
    ) {
      if (TryInt(raw, field, message, out value, out error) && value > 0) {
        return true;
      }
      error = Invalid(field, message);
      return false;
    }

    private static bool TryDouble(
      string raw,
      MidiBindingEditorField field,
      string message,
      out double value,
      [NotNullWhen(false)] out MidiBindingValidationError? error
    ) {
      if (double.TryParse(
          raw.Trim(),
          NumberStyles.Float | NumberStyles.AllowThousands,
          CultureInfo.CurrentCulture,
          out value) &&
          double.IsFinite(value)) {
        error = null;
        return true;
      }
      error = Invalid(field, message);
      return false;
    }

    private static MidiBindingValidationError Invalid(
      MidiBindingEditorField field, string message
    ) => new MidiBindingValidationError(field, message);
  }
}
