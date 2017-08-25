namespace Spectrum.Base {

  public class AudioLevelDriverPreset : LevelDriverPreset {

    // Filter range points from 0-1
    public double FilterRangeStart { get; set; }
    public double FilterRangeEnd { get; set; }

    public AudioLevelDriverPreset() {
      this.Source = LevelDriverSource.Audio;
    }

    public override object Clone() {
      return new AudioLevelDriverPreset() {
        Name = this.Name,
        FilterRangeStart = this.FilterRangeStart,
        FilterRangeEnd = this.FilterRangeEnd,
      };
    }

  }

}