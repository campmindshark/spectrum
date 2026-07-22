namespace Spectrum.Base {

  // Persisted ADSR envelope for one MIDI level-driver channel.
  public class MidiLevelDriverPreset : System.ICloneable {

    // Times in ms; levels from 0-1
    public int AttackTime { get; set; }
    public double PeakLevel { get; set; }
    public int DecayTime { get; set; }
    public double SustainLevel { get; set; }
    public int ReleaseTime { get; set; }

    public object Clone() {
      return new MidiLevelDriverPreset() {
        AttackTime = this.AttackTime,
        PeakLevel = this.PeakLevel,
        DecayTime = this.DecayTime,
        SustainLevel = this.SustainLevel,
        ReleaseTime = this.ReleaseTime,
      };
    }

  }

}
