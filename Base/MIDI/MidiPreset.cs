using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Spectrum.Base {

  using BindingKey = Tuple<MidiCommandType, int>;

  public enum MidiCommandType : byte { Knob, Note, Program }

  public struct Binding {
    public BindingKey key;
    public delegate string bindingCallback(int index, double val);
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

  [XmlInclude(typeof(ColorPaletteMidiBindingConfig))]
  [XmlInclude(typeof(TapTempoMidiBindingConfig))]
  [XmlInclude(typeof(ContinuousKnobMidiBindingConfig))]
  [XmlInclude(typeof(DiscreteKnobMidiBindingConfig))]
  [XmlInclude(typeof(DiscreteLogarithmicKnobMidiBindingConfig))]
  public interface IMidiBindingConfig : ICloneable {
    
    int BindingType { get; set; }
    string BindingName { get; set; }

    Binding[] GetBindings(Configuration config);

  }

  public abstract class MidiBindingConfig : IMidiBindingConfig {

    public int BindingType { get; set; }
    public string BindingName { get; set; }

    public abstract object Clone();
    
    public abstract Binding[] GetBindings(Configuration config);

  }

}