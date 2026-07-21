using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Spectrum.Base {

  using BindingKey = Tuple<MidiCommandType, int>;

  public enum MidiCommandType : byte { Knob, Note, Program }

  public readonly struct BindingInvocation {
    public string Message { get; }
    public Task Completion { get; }

    public BindingInvocation(string message, Task completion = null) {
      this.Message = message;
      this.Completion = completion;
    }
  }

  public struct Binding {
    public BindingKey key;
    public delegate BindingInvocation bindingCallback(int index, double val);
    public bindingCallback callback;
    public IMidiBindingConfig config;
  }

  public class MidiPreset : ICloneable {
    public int id { get; set; }
    public string Name { get; set; }
    public List<IMidiBindingConfig> Bindings { get; set; } = new List<IMidiBindingConfig>();

    public object Clone() {
      var newBindings = new List<IMidiBindingConfig>();
      foreach (var binding in this.Bindings) {
        newBindings.Add((IMidiBindingConfig)binding.Clone());
      }
      return new MidiPreset() {
        id = this.id,
        Name = this.Name,
        Bindings = newBindings,
      };
    }
  }

  [XmlInclude(typeof(TapTempoMidiBindingConfig))]
  [XmlInclude(typeof(ContinuousKnobMidiBindingConfig))]
  [XmlInclude(typeof(DiscreteKnobMidiBindingConfig))]
  [XmlInclude(typeof(DiscreteLogarithmicKnobMidiBindingConfig))]
  [XmlInclude(typeof(AdsrLevelDriverMidiBindingConfig))]
  public interface IMidiBindingConfig : ICloneable {

    int BindingType { get; set; }
    string BindingName { get; set; }

    // `beat` is the live tempo service (owned by the Operator, not part of
    // Configuration); only the tap-tempo and ADSR bindings use it.
    Binding[] GetBindings(
      Configuration config,
      BeatBroadcaster beat,
      ApplicationStateDispatcher stateDispatcher);

  }

  public abstract class MidiBindingConfig : IMidiBindingConfig {

    public int BindingType { get; set; }
    public string BindingName { get; set; }

    public abstract object Clone();

    public abstract Binding[] GetBindings(
      Configuration config,
      BeatBroadcaster beat,
      ApplicationStateDispatcher stateDispatcher);

    public static string ConfigurationPropertyError(
      string propertyName, Type assignedType
    ) {
      if (string.IsNullOrWhiteSpace(propertyName)) {
        return "configuration property name is required";
      }
      PropertyInfo property = typeof(Configuration).GetProperty(propertyName);
      if (property == null) {
        return "config property \"" + propertyName +
          "\" no longer exists; rebind this knob";
      }
      if (!property.CanWrite) {
        return "config property \"" + propertyName +
          "\" is read-only; rebind this knob";
      }
      if (property.PropertyType != assignedType) {
        return "config property \"" + propertyName + "\" has type " +
          property.PropertyType.Name + ", but this binding assigns " +
          assignedType.Name;
      }
      return null;
    }

    protected static PropertyInfo ResolveConfigurationProperty(
      string propertyName, Type assignedType
    ) {
      string error = ConfigurationPropertyError(propertyName, assignedType);
      if (error != null) {
        throw new InvalidOperationException(error);
      }
      return typeof(Configuration).GetProperty(propertyName);
    }

  }

}
