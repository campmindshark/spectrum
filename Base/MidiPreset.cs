using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Spectrum.Base {

  public class MidiPreset : ICloneable {
    public int id { get; set; }
    public string Name { get; set; }

    public object Clone() {
      return new MidiPreset() { id = this.id, Name = this.Name };
    }
  }

}