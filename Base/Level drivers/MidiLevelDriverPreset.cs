namespace Spectrum.Base {

  public class MidiLevelDriverPreset : LevelDriverPreset {

    // Times in ms; levels from 0-1
    public int AttackTime { get; set; }
    public double PeakLevel { get; set; }
    public int DecayTime { get; set; }
    public double SustainLevel { get; set; }
    public int ReleaseTime { get; set; }

    public MidiLevelDriverPreset() {
      this.Source = LevelDriverSource.Midi;
    }

    public override object Clone() {
      return new MidiLevelDriverPreset() {
        Name = this.Name,
        AttackTime = this.AttackTime,
        PeakLevel = this.PeakLevel,
        DecayTime = this.DecayTime,
        SustainLevel = this.SustainLevel,
        ReleaseTime = this.ReleaseTime,
      };
    }

  }

}