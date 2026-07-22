using System.Collections.Generic;
using System.Collections.Immutable;

namespace Spectrum.Base {

  // High-rate controls captured exactly once at an operator-frame boundary.
  // The dictionaries are never mutated after publication, so every renderer in
  // a frame sees one complete command generation without any per-frame copies.
  public sealed record DomeRuntimeFrameSnapshot(
    long Generation,
    int TestPattern,
    double MaxBrightness,
    double Brightness,
    int SpotlightDeviceId,
    ImmutableDictionary<string, int> FireGenerations,
    ImmutableDictionary<string, int> ClearGenerations
  ) {
    public static DomeRuntimeFrameSnapshot Empty { get; } =
      new DomeRuntimeFrameSnapshot(
        0, 0, 0.5, 0.1, 0,
        ImmutableDictionary<string, int>.Empty,
        ImmutableDictionary<string, int>.Empty);

    public int FireGeneration(string instanceId) =>
      instanceId != null && this.FireGenerations.TryGetValue(
        instanceId, out int generation) ? generation : 0;

    public int ClearGeneration(string instanceId) =>
      instanceId != null && this.ClearGenerations.TryGetValue(
        instanceId, out int generation) ? generation : 0;
  }

  public sealed record AudioSettingsSnapshot(
    long Generation,
    string DeviceId,
    int BeatInput
  ) {
    public static AudioSettingsSnapshot Empty { get; } =
      new AudioSettingsSnapshot(0, null, 0);
  }

  // Presets are private clones of the serializer DTOs. They retain the DTO
  // shape needed by the existing binding compilers, but no published object is
  // reachable from Configuration and therefore no writer can mutate it.
  public sealed record MidiSettingsSnapshot(
    long Generation,
    long DeviceGeneration,
    long BindingGeneration,
    bool Enabled,
    ImmutableDictionary<int, int> Devices,
    ImmutableDictionary<int, MidiPreset> Presets
  ) {
    public static MidiSettingsSnapshot Empty { get; } =
      new MidiSettingsSnapshot(
        0, 0, 0, false,
        ImmutableDictionary<int, int>.Empty,
        ImmutableDictionary<int, MidiPreset>.Empty);
  }

  public sealed record OrientationSettingsSnapshot(
    long Generation,
    int SpotlightDeviceId,
    bool Calibrate,
    string WandSerialPort
  ) {
    public static OrientationSettingsSnapshot Empty { get; } =
      new OrientationSettingsSnapshot(0, 0, false, "");
  }

  // One immutable wiring and transport generation for LEDDomeOutput. Nested
  // immutable arrays make the port mappings safe to retain across threads.
  public sealed record DomeOutputSettingsSnapshot(
    long Generation,
    long MappingGeneration,
    long TransportGeneration,
    bool Enabled,
    bool SimulationEnabled,
    string OpcAddress,
    bool OutputInSeparateThread,
    ImmutableArray<int> CableMapping,
    ImmutableArray<ImmutableArray<int>> PortMappings
  ) {
    public static DomeOutputSettingsSnapshot Empty { get; } =
      new DomeOutputSettingsSnapshot(
        0, 0, 0, false, false, "", false,
        ImmutableArray<int>.Empty,
        ImmutableArray<ImmutableArray<int>>.Empty);
  }

  public readonly record struct MidiLevelDriverSettingsSnapshot(
    int AttackTime,
    double PeakLevel,
    int DecayTime,
    double SustainLevel,
    int ReleaseTime
  );

  public sealed record BeatSettingsSnapshot(
    long Generation,
    double FlashSpeed,
    ImmutableDictionary<int, MidiLevelDriverSettingsSnapshot> MidiChannels
  ) {
    public static BeatSettingsSnapshot Empty { get; } =
      new BeatSettingsSnapshot(
        0, 0,
        ImmutableDictionary<int, MidiLevelDriverSettingsSnapshot>.Empty);

    public bool TryGetMidiPreset(
      int channelIndex,
      out MidiLevelDriverSettingsSnapshot preset
    ) => this.MidiChannels.TryGetValue(channelIndex, out preset);
  }

  public sealed record SceneRetentionSnapshot(
    long Generation,
    ImmutableHashSet<string> LayerInstanceIds
  ) {
    public static SceneRetentionSnapshot Empty { get; } =
      new SceneRetentionSnapshot(0, ImmutableHashSet<string>.Empty);
  }

  // Runtime consumers depend on these typed views instead of repeatedly
  // reading the live Configuration API. SpectrumConfiguration publishes a new
  // reference only for the affected subsystem.
  public interface IRuntimeSettingsConfiguration {
    DomeRuntimeFrameSnapshot DomeRuntimeFrameSnapshot { get; }
    AudioSettingsSnapshot AudioSettingsSnapshot { get; }
    MidiSettingsSnapshot MidiSettingsSnapshot { get; }
    OrientationSettingsSnapshot OrientationSettingsSnapshot { get; }
    DomeOutputSettingsSnapshot DomeOutputSettingsSnapshot { get; }
    BeatSettingsSnapshot BeatSettingsSnapshot { get; }
    SceneRetentionSnapshot SceneRetentionSnapshot { get; }
  }
}
