using System;
using System.Xml.Serialization;

namespace Spectrum.Base {

  public enum LevelDriverSource : byte { Audio, Midi }

  [XmlInclude(typeof(AudioLevelDriverPreset))]
  [XmlInclude(typeof(MidiLevelDriverPreset))]
  public interface ILevelDriverPreset : ICloneable {

    string Name { get; set; }
    LevelDriverSource Source { get; set; }

  }

  public abstract class LevelDriverPreset : ILevelDriverPreset {

    public string Name { get; set; }
    public LevelDriverSource Source { get; set; }

    public abstract object Clone();

  }

}
