using System;
using System.Reflection;

namespace Spectrum.Base {

  using BindingKey = Tuple<MidiCommandType, int>;

  public class DiscreteLogarithmicKnobMidiBindingConfig : MidiBindingConfig {

    public int knobIndex { get; set; }
    public string configPropertyName { get; set; }
    public int numPossibleValues { get; set; }
    public double startValue { get; set; }

    public DiscreteLogarithmicKnobMidiBindingConfig() {
      this.BindingType = 3;
    }

    public override object Clone() {
      return new DiscreteLogarithmicKnobMidiBindingConfig() {
        BindingName = this.BindingName,
        knobIndex = this.knobIndex,
        configPropertyName = this.configPropertyName,
        numPossibleValues = this.numPossibleValues,
        startValue = this.startValue,
      };
    }

    public override Binding[] GetBindings(Configuration config, BeatBroadcaster beat) {
      Binding binding = new Binding();
      binding.key = new BindingKey(MidiCommandType.Knob, this.knobIndex);
      binding.config = this;
      binding.callback = (index, val) => {
        double transformedValue = DiscretizeLogarithmicKnob(
          val,
          this.numPossibleValues,
          this.startValue,
          true
        );
        PropertyInfo myPropInfo =
          typeof(Configuration).GetProperty(this.configPropertyName);
        // A preset can outlive the property it binds (e.g. tuning knobs retired
        // into per-layer params). This callback runs on the MIDI driver thread,
        // where a throw kills the process — log and drop instead.
        if (myPropInfo == null) {
          return "config property \"" + this.configPropertyName +
            "\" no longer exists; rebind this knob";
        }
        myPropInfo.SetValue(config, transformedValue, null);
        return "config property \"" + this.configPropertyName +
          "\" updated to " + transformedValue.ToString();
      };
      return new Binding[] { binding };
    }

    /**
     * If includeZero is on, the first value is 0.0. numPossibleValues does not
     * include a hypothetical zero in its count.
     */
    private static double DiscretizeLogarithmicKnob(
      double value,
      int numPossibleValues,
      double startingValue,
      bool includeZero
    ) {
      if (includeZero) {
        numPossibleValues++;
      }
      int index = DiscreteKnobMidiBindingConfig.DiscretizeKnob(
        value,
        numPossibleValues
      );
      if (includeZero) {
        if (index == 0) {
          return 0.0;
        }
        index--;
      }
      return Math.Pow(2.0, index) * startingValue;
    }

  }

}
