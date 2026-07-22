using System.Threading.Tasks;

namespace Spectrum.Base {

  /**
   * Platform-neutral audio signal consumed by renderers and readiness views.
   * Capture device discovery and ownership stay behind the platform factory.
   */
  public interface IAudioLevelInput : Input {
    float Volume { get; }
  }

  /** A normalized MIDI message emitted by any platform backend. */
  public struct MidiCommand {
    public int deviceIndex;
    public MidiCommandType type;
    public int index;
    public double value;
  }

  /**
   * Platform-neutral MIDI orchestration surface used by Operator. Driver APIs
   * and device enumeration remain implementation details of each backend.
   */
  public interface IMidiControlInput : Input {
    ObservableMidiLog MidiLog { get; }
    long AppliedDeviceGeneration { get; }
    Task DispatchBindingsAsync(MidiCommand command);
  }

  /**
   * Composition boundary for platform audio and MIDI implementations. A Linux
   * host can supply disabled or native backends without changing Operator.
   */
  public interface ISpectrumInputFactory {
    IAudioLevelInput CreateAudioInput(
      Configuration config,
      BeatBroadcaster beat);

    IMidiControlInput CreateMidiInput(
      Configuration config,
      BeatBroadcaster beat,
      ApplicationStateDispatcher stateDispatcher);
  }
}
