using System;
using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.MIDI;

namespace Spectrum {

  /** Windows adapter composition retained by the WPF frontend. */
  internal sealed class WindowsSpectrumInputFactory : ISpectrumInputFactory {
    private readonly bool connectHardware;

    public WindowsSpectrumInputFactory(bool connectHardware = true) {
      this.connectHardware = connectHardware;
    }

    public IAudioLevelInput CreateAudioInput(
      Configuration config,
      BeatBroadcaster beat
    ) => new AudioInput(config, beat, this.connectHardware);

    public IMidiControlInput CreateMidiInput(
      Configuration config,
      BeatBroadcaster beat,
      ApplicationStateDispatcher stateDispatcher
    ) => new MidiInput(
      config, beat, stateDispatcher, this.connectHardware);
  }
}
