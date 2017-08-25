using System;

namespace Spectrum.Base {

  using BindingKey = Tuple<MidiCommandType, int>;

  public class AdsrLevelDriverMidiBindingConfig : MidiBindingConfig {

    public int indexRangeStart { get; set; }

    public AdsrLevelDriverMidiBindingConfig() {
      this.BindingType = 5;
    }

    public override object Clone() {
      return new AdsrLevelDriverMidiBindingConfig() {
        BindingName = this.BindingName,
        indexRangeStart = this.indexRangeStart,
      };
    }

    public override Binding[] GetBindings(Configuration config) {
      Binding binding = new Binding();
      binding.key = new BindingKey(MidiCommandType.Note, -1);
      binding.config = this;
      binding.callback = (index, val) => {
        if (index < this.indexRangeStart || index > (this.indexRangeStart + 7)) {
          return null;
        }
        int channelIndex = index - this.indexRangeStart;
        if (val == 0.0) {
          config.beatBroadcaster.MidiReleaseOnChannel(channelIndex);
          return "MIDI received release on channel index " + channelIndex;
        } else {
          config.beatBroadcaster.MidiPress(new MidiLevelDriverInstance() {
            ChannelIndex = channelIndex,
            PressTimestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond,
            PressVelocity = val,
          });
          return "MIDI received press on channel index " + channelIndex;
        }
      };
      return new Binding[] { binding };
    }

  }

}