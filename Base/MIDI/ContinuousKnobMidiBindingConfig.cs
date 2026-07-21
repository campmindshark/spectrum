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
      this.BindingType = 1;
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

    public override Binding[] GetBindings(
      Configuration config,
      BeatBroadcaster beat,
      ApplicationStateDispatcher stateDispatcher
    ) {
      PropertyInfo property = ResolveConfigurationProperty(
        this.configPropertyName, typeof(double));
      Binding binding = new Binding();
      binding.key = new BindingKey(MidiCommandType.Knob, this.knobIndex);
      binding.config = this;
      binding.callback = (index, val) => {
        double transformedValue = ContinuousKnob(val, this.startValue, this.endValue);
        System.Threading.Tasks.Task completion = stateDispatcher.InvokeAsync(
          () => property.SetValue(config, transformedValue, null));
        return new BindingInvocation(
          "config property \"" + this.configPropertyName +
            "\" updated to " + transformedValue.ToString(),
          completion);
      };
      return new Binding[] { binding };
    }

    private static double ContinuousKnob(double value, double from, double to) {
      return from + (to - from) * value;
    }

  }

}
