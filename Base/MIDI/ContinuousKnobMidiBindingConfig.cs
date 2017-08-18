using System;
using System.Reflection;

namespace Spectrum.Base {

  using BindingKey = Tuple<MidiCommandType, int>;

  public class ContinuousKnobMidiBindingConfig : MidiBindingConfig {

    public int knobIndex { get; set; }
    public string configPropertyName { get; set; }
    public double startValue { get; set; }
    public double endValue { get; set; }

    public ContinuousKnobMidiBindingConfig() {
      this.BindingType = 2;
    }

    public override object Clone() {
      return new ContinuousKnobMidiBindingConfig() {
        BindingName = this.BindingName,
        knobIndex = this.knobIndex,
        configPropertyName = this.configPropertyName,
        startValue = this.startValue,
        endValue = this.endValue,
      };
    }

    public override Binding[] GetBindings(Configuration config) {
      Binding binding = new Binding();
      binding.key = new BindingKey(MidiCommandType.Knob, this.knobIndex);
      binding.config = this;
      binding.callback = (index, val) => {
        double transformedValue = ContinuousKnob(val, this.startValue, this.endValue);
        Type configType = typeof(Configuration);
        PropertyInfo myPropInfo = configType.GetProperty(this.configPropertyName);
        myPropInfo.SetValue(config, transformedValue, null);
        return "config property \"" + this.configPropertyName +
          "\" updated to " + transformedValue.ToString();
      };
      return new Binding[] { binding };
    }

    private static double ContinuousKnob(double value, double from, double to) {
      return from + (to - from) * value;
    }

  }

}