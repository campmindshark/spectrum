using Spectrum.Base;

namespace Spectrum.Platform.Linux {

  /**
   * Linux composition. ALSA owns capture and feeds Madmom; MIDI stays disabled.
   */
  public sealed class LinuxSpectrumInputFactory : ISpectrumInputFactory {
    private readonly DisabledSpectrumInputFactory disabled =
      new DisabledSpectrumInputFactory();

    public IAudioLevelInput CreateAudioInput(
      Configuration config,
      BeatBroadcaster beat
    ) => new AlsaAudioLevelInput(config, beat);

    public IMidiControlInput CreateMidiInput(
      Configuration config,
      BeatBroadcaster beat,
      ApplicationStateDispatcher stateDispatcher
    ) => this.disabled.CreateMidiInput(config, beat, stateDispatcher);
  }
}
