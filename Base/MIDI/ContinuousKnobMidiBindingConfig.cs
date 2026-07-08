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

    public override Binding[] GetBindings(Configuration config, BeatBroadcaster beat) {
      Binding binding = new Binding();
      binding.key = new BindingKey(MidiCommandType.Knob, this.knobIndex);
      binding.config = this;
      binding.callback = (index, val) => {
        double transformedValue = ContinuousKnob(val, this.startValue, this.endValue);
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

    private static double ContinuousKnob(double value, double from, double to) {
      return from + (to - from) * value;
    }

  }

}