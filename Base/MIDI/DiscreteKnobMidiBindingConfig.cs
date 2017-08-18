using System;
using System.Reflection;

namespace Spectrum.Base {

  using BindingKey = Tuple<MidiCommandType, int>;

  public class DiscreteKnobMidiBindingConfig : MidiBindingConfig {

    public int knobIndex { get; set; }
    public string configPropertyName { get; set; }
    public int numPossibleValues { get; set; }

    public DiscreteKnobMidiBindingConfig() {
      this.BindingType = 3;
    }

    public override object Clone() {
      return new DiscreteKnobMidiBindingConfig() {
        BindingName = this.BindingName,
        knobIndex = this.knobIndex,
        configPropertyName = this.configPropertyName,
        numPossibleValues = this.numPossibleValues,
      };
    }

    public override Binding[] GetBindings(Configuration config) {
      Binding binding = new Binding();
      binding.key = new BindingKey(MidiCommandType.Knob, this.knobIndex);
      binding.config = this;
      binding.callback = (index, val) => {
        int transformedValue = DiscretizeKnob(val, this.numPossibleValues);
        Type configType = typeof(Configuration);
        PropertyInfo myPropInfo = configType.GetProperty(this.configPropertyName);
        myPropInfo.SetValue(config, transformedValue, null);
        return "config property \"" + this.configPropertyName +
          "\" updated to " + transformedValue.ToString();
      };
      return new Binding[] { binding };
    }

    public static int DiscretizeKnob(double value, int numPossibleValues) {
      // Start and end get a bit more space
      double step = 1.0 / (numPossibleValues + 2);
      int numSteps = (int)(value / step);
      if (numSteps == 0) {
        return 0;
      }
      numSteps--;
      if (numSteps >= numPossibleValues) {
        return numPossibleValues - 1;
      }
      return numSteps;
    }
 
  }

}