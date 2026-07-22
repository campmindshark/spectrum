using Spectrum.Base;

namespace Spectrum.Platform.Linux {

  /** Linux platform composition. ALSA supplies audio; MIDI remains disabled. */
  public sealed class LinuxSpectrumInputFactory : ISpectrumInputFactory {
    private readonly DisabledSpectrumInputFactory disabled =
      new DisabledSpectrumInputFactory();

    public IAudioLevelInput CreateAudioInput(
      Configuration config,
      BeatBroadcaster beat
    ) => new AlsaAudioLevelInput(config);

    public IMidiControlInput CreateMidiInput(
      Configuration config,
      BeatBroadcaster beat,
      ApplicationStateDispatcher stateDispatcher
    ) => this.disabled.CreateMidiInput(config, beat, stateDispatcher);
  }
}
