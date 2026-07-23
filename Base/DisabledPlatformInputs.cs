#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Spectrum.Base {

  /**
   * Portable no-hardware input factory for headless hosts, development, and
   * tests. The inputs remain logically enabled so audio-reactive layers still
   * render deterministically at silence instead of disappearing from the plan.
   */
  public sealed class DisabledSpectrumInputFactory : ISpectrumInputFactory {
    public IAudioLevelInput CreateAudioInput(
      Configuration config,
      BeatBroadcaster beat
    ) => new DisabledAudioLevelInput();

    public IMidiControlInput CreateMidiInput(
      Configuration config,
      BeatBroadcaster beat,
      ApplicationStateDispatcher stateDispatcher
    ) => new DisabledMidiControlInput();

    private sealed class DisabledAudioLevelInput :
      IAudioLevelInput, IAudioDeviceProvider {
      public bool Active { get; set; }
      public bool AlwaysActive => true;
      public bool Enabled => true;
      public float Volume => 0;
      public string BackendName => "Disabled";
      public string? LastError => null;
      public IReadOnlyList<AudioCaptureDevice> GetAvailableDevices() =>
        Array.Empty<AudioCaptureDevice>();
      public void OperatorUpdate() { }
    }

    private sealed class DisabledMidiControlInput : IMidiControlInput {
      public bool Active { get; set; }
      public bool AlwaysActive => true;
      public bool Enabled => true;
      public ObservableMidiLog MidiLog { get; } = new ObservableMidiLog();
      public long AppliedDeviceGeneration => 0;
      public Task DispatchBindingsAsync(MidiCommand command) =>
        Task.CompletedTask;
      public void OperatorUpdate() { }
    }
  }
}
