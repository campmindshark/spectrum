using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Spectrum.Base;
using System.ComponentModel;
using System.Threading;

namespace Spectrum {

  public class SpectrumConfiguration : Configuration,
      ILayerStackSnapshotSource, IDomeShowStateConfiguration,
      IRuntimeSettingsConfiguration, ConfigurationEditor {

    public override event PropertyChangedEventHandler? PropertyChanged;

    // Set once by the application composition root after deserialization.
    // Tests and serializer-only callers may leave it unset and use the
    // configuration synchronously on their own thread.
    private ApplicationStateDispatcher? mutationDispatcher;

    public void AttachMutationDispatcher(
      ApplicationStateDispatcher dispatcher
    ) {
      if (dispatcher == null) {
        throw new System.ArgumentNullException(nameof(dispatcher));
      }
      if (!dispatcher.CheckAccess()) {
        throw new System.InvalidOperationException(
          "The configuration dispatcher must be attached on its owner thread.");
      }
      ApplicationStateDispatcher? existing = Interlocked.CompareExchange(
        ref this.mutationDispatcher, dispatcher, null);
      if (existing != null && !ReferenceEquals(existing, dispatcher)) {
        throw new System.InvalidOperationException(
          "A configuration dispatcher is already attached.");
      }
    }

    private bool DispatchMutationIfRequired<T>(
      string propertyName, T value
    ) {
      ApplicationStateDispatcher? dispatcher =
        Volatile.Read(ref this.mutationDispatcher);
      if (dispatcher == null || dispatcher.CheckAccess()) {
        return false;
      }

      // Property setters converge here even when a future producer forgets to
      // use the dispatcher explicitly. Resolve the setter before queueing so a
      // programming error is reported on the calling thread.
      System.Reflection.PropertyInfo? property =
        this.GetType().GetProperty(propertyName);
      if (property == null || !property.CanWrite) {
        throw new System.InvalidOperationException(
          "Configuration property is not writable: " + propertyName);
      }
      dispatcher.Post(() => property.SetValue(this, value));
      return true;
    }

    private bool DispatchMutationIfRequired(Action mutation) {
      ApplicationStateDispatcher? dispatcher =
        Volatile.Read(ref this.mutationDispatcher);
      if (dispatcher == null || dispatcher.CheckAccess()) {
        return false;
      }
      dispatcher.Post(mutation);
      return true;
    }

    protected override bool DispatchScalarMutation<T>(
      string propertyName, T value
    ) => this.DispatchMutationIfRequired(propertyName, value);

    protected override void ScalarChanged(string propertyName) {
      switch (propertyName) {
        case nameof(this.audioDeviceID):
        case nameof(this.beatInput):
          this.PublishAudioSettings();
          break;
        case nameof(this.domeEnabled):
        case nameof(this.domeOutputInSeparateThread):
        case nameof(this.domeBeagleboneOPCAddress):
          this.PublishDomeTransportSettings();
          break;
        case nameof(this.midiInputEnabled):
          this.PublishMidiEnabledSettings();
          break;
        case nameof(this.domeSimulationEnabled):
          this.PublishDomeOutputState();
          break;
        case nameof(this.domeMaxBrightness):
        case nameof(this.domeBrightness):
        case nameof(this.domeTestPattern):
          this.PublishDomeRuntimeFrameSettings();
          break;
        case nameof(this.domeGlobalFadeSpeed):
        case nameof(this.domeGlobalHueSpeed):
          this.PublishDomeShowStateSnapshot();
          break;
        case nameof(this.flashSpeed):
          this.PublishBeatSettings();
          break;
        case nameof(this.orientationDeviceSpotlight):
          this.PublishOrientationAndFrameSettings();
          break;
        case nameof(this.orientationCalibrate):
        case nameof(this.wandSerialPort):
          this.PublishOrientationSettings();
          break;
      }

      this.RaisePropertyChanged(propertyName);
      if (propertyName == nameof(this.domeGlobalFadeSpeed) ||
          propertyName == nameof(this.domeGlobalHueSpeed)) {
        this.RaisePropertyChanged(
          DomeShowStateSnapshot.NotificationPropertyName);
      }
    }

    protected override void ScalarsReplaced() {
      this.PublishDomeRuntimeFrameSettings();
      this.PublishDomeShowStateSnapshot();
      this.PublishAudioSettings();
      this.PublishMidiEnabledSettings();
      this.PublishOrientationSettings();
      this.PublishDomeTransportSettings();
      this.PublishBeatSettings();
    }

    // Empty or omitted document collections are projected as cached empty
    // immutable views.
    private List<DomeLayerSettings> _domeLayerStack = new();
    private ImmutableArray<DomeLayerView> _domeLayerStackView =
      ImmutableArray<DomeLayerView>.Empty;
    private static readonly LayerStackService layerStackService =
      new LayerStackService(BuiltInDomeLayerCatalog.Metadata);
    private LayerStackSnapshot _domeLayerStackSnapshot =
      LayerStackSnapshot.Empty;
    public override ImmutableArray<DomeLayerView> domeLayerStack =>
      this._domeLayerStackView;

    public void ReplaceDomeLayerStack(
      IReadOnlyList<DomeLayerSettings>? value
    ) {
      List<DomeLayerSettings> detached =
        ConfigurationGraphCopy.Layers(value);
      if (this.DispatchMutationIfRequired(
          () => this.ReplaceDomeLayerStack(detached))) {
        return;
      }
      (List<DomeLayerSettings> published, LayerStackSnapshot snapshot) =
        this.PrepareLayerStack(detached);
      this._domeLayerStack = published;
      this._domeLayerStackView = DomeLayerView.Compile(published);
      Volatile.Write(ref this._domeLayerStackSnapshot, snapshot);
      this.PublishDomeShowStateSnapshot();
      this.RaisePropertyChanged(nameof(this.domeLayerStack));
      this.RaisePropertyChanged(
        DomeShowStateSnapshot.NotificationPropertyName);
    }
    LayerStackSnapshot ILayerStackSnapshotSource.DomeLayerStackSnapshot =>
      Volatile.Read(ref this._domeLayerStackSnapshot);

    private (List<DomeLayerSettings> published, LayerStackSnapshot snapshot)
      PrepareLayerStack(List<DomeLayerSettings> value) {
      List<DomeLayerSettings> published = value;
      if (NeedsLayerInstanceIds(value)) {
        (List<DomeLayerSettings>? normalized, string? error) =
            layerStackService.Normalize(value);
        if (error == null && normalized != null) {
          published = normalized;
        }
      }
      (LayerStackSnapshot? snapshot, string? snapshotError) =
        layerStackService.CreateSnapshot(published);
      if (snapshotError != null || snapshot == null) {
        snapshot = LayerStackSnapshot.Empty;
      }
      return (published, snapshot);
    }

    private static bool NeedsLayerInstanceIds(
      IReadOnlyList<DomeLayerSettings> layers
    ) {
      if (layers == null) {
        return false;
      }
      for (int i = 0; i < layers.Count; i++) {
        DomeLayerSettings layer = layers[i];
        if (layer != null && string.IsNullOrWhiteSpace(layer.InstanceId)) {
          return true;
        }
      }
      return false;
    }
    // An empty mapping remains empty in the live view; LEDDomeOutput treats it
    // as identity wiring.
    private int[] _domeCableMapping = System.Array.Empty<int>();
    private ImmutableArray<int> _domeCableMappingView =
      ImmutableArray<int>.Empty;
    public override ImmutableArray<int> domeCableMapping =>
      this._domeCableMappingView;

    public void ReplaceDomeCableMapping(IReadOnlyList<int>? value) {
      int[] detached = ConfigurationGraphCopy.Array(value);
      if (this.DispatchMutationIfRequired(
          () => this.ReplaceDomeCableMapping(detached))) {
        return;
      }
      this._domeCableMapping = detached;
      this._domeCableMappingView = detached == null
        ? ImmutableArray<int>.Empty
        : ImmutableArray.Create(detached);
      this.PublishDomeMappingSettings();
      this.RaisePropertyChanged(nameof(this.domeCableMapping));
    }
    // Five independently owned mappings, one for each dome-side box. Detached
    // document DTOs are deep-cloned into private storage and immutable views.
    private DomePortMapping?[] _domePortMappings =
      System.Array.Empty<DomePortMapping?>();
    private ImmutableArray<ImmutableArray<int>> _domePortMappingsView =
      ImmutableArray<ImmutableArray<int>>.Empty;
    public override ImmutableArray<ImmutableArray<int>> domePortMappings =>
      this._domePortMappingsView;

    public void ReplaceDomePortMappings(
      IReadOnlyList<DomePortMapping?>? value
    ) {
      DomePortMapping?[] detached =
        ConfigurationGraphCopy.PortMappings(value);
      if (this.DispatchMutationIfRequired(
          () => this.ReplaceDomePortMappings(detached))) {
        return;
      }
      this._domePortMappings = detached;
      this._domePortMappingsView = CompilePortMappings(detached);
      this.PublishDomeMappingSettings();
      this.RaisePropertyChanged(nameof(this.domePortMappings));
    }

    private static ImmutableArray<ImmutableArray<int>> CompilePortMappings(
      IReadOnlyList<DomePortMapping?>? mappings
    ) {
      if (mappings == null || mappings.Count == 0) {
        return ImmutableArray<ImmutableArray<int>>.Empty;
      }
      var result = ImmutableArray.CreateBuilder<ImmutableArray<int>>(
        mappings.Count);
      foreach (DomePortMapping? mapping in mappings) {
        result.Add(mapping?.ports == null
          ? ImmutableArray<int>.Empty
          : ImmutableArray.CreateRange(mapping.ports));
      }
      return result.MoveToImmutable();
    }

    // Null private storage is projected as an empty immutable view.
    private List<DomeScene> _domeScenes = new();
    private ImmutableArray<DomeSceneView> _domeScenesView =
      ImmutableArray<DomeSceneView>.Empty;
    public override ImmutableArray<DomeSceneView> domeScenes =>
      this._domeScenesView;

    public void ReplaceDomeScenes(IReadOnlyList<DomeScene>? value) {
      List<DomeScene> detached = ConfigurationGraphCopy.Scenes(value);
      if (this.DispatchMutationIfRequired(
          () => this.ReplaceDomeScenes(detached))) {
        return;
      }
      this._domeScenes = detached;
      this._domeScenesView = DomeSceneView.Compile(detached);
      this.PublishSceneRetentionSettings();
      this.RaisePropertyChanged(nameof(this.domeScenes));
    }
    // Null private storage is projected as an empty immutable view.
    private List<DomePalette> _domePalettes = new();
    public override ImmutableArray<DomePaletteSnapshot> domePalettes =>
      this.compiledDomePalettes;

    public void ReplaceDomePalettes(IReadOnlyList<DomePalette>? value) {
      List<DomePalette> detached = ConfigurationGraphCopy.Palettes(value);
      if (this.DispatchMutationIfRequired(
          () => this.ReplaceDomePalettes(detached))) {
        return;
      }
      this._domePalettes = detached;
      this.CompileDomePalettes();
      this.PublishDomeShowStateSnapshot();
      this.RaisePropertyChanged(nameof(this.domePalettes));
      this.RaisePropertyChanged(
        DomeShowStateSnapshot.NotificationPropertyName);
    }

    private long domeShowStateGeneration;
    private ImmutableArray<DomePaletteSnapshot> compiledDomePalettes =
      ImmutableArray<DomePaletteSnapshot>.Empty;
    private DomeShowStateSnapshot _domeShowStateSnapshot =
      DomeShowStateSnapshot.Empty;

    DomeShowStateSnapshot
      IDomeShowStateConfiguration.DomeShowStateSnapshot =>
        Volatile.Read(ref this._domeShowStateSnapshot);

    void IDomeShowStateConfiguration.ApplyDomeShowState(
      DomeShowStateUpdate update
    ) => this.ApplyDomeShowState(update);

    private void ApplyDomeShowState(DomeShowStateUpdate update) {
      if (update == null) {
        throw new System.ArgumentNullException(nameof(update));
      }
      var detached = new DomeShowStateUpdate(
        ConfigurationGraphCopy.Layers(update.Layers),
        update.PalettesChanged
          ? ConfigurationGraphCopy.Palettes(update.Palettes)
          : null,
        update.GlobalFadeSpeed,
        update.GlobalHueSpeed,
        update.ScenesChanged
          ? ConfigurationGraphCopy.Scenes(update.Scenes)
          : null) {
            PalettesChanged = update.PalettesChanged,
            ScenesChanged = update.ScenesChanged,
          };
      ApplicationStateDispatcher? dispatcher =
        Volatile.Read(ref this.mutationDispatcher);
      if (dispatcher != null && !dispatcher.CheckAccess()) {
        dispatcher.Post(() => this.ApplyDomeShowState(detached));
        return;
      }

      (List<DomeLayerSettings> layers, LayerStackSnapshot layerSnapshot) =
        this.PrepareLayerStack(detached.Layers);
      bool fadeChanged =
        this.domeGlobalFadeSpeed != detached.GlobalFadeSpeed;
      bool hueChanged = this.domeGlobalHueSpeed != detached.GlobalHueSpeed;

      // Assign every persisted field and compile the deep immutable generation
      // before the first notification. Subscribers can read any combination of
      // these properties without observing the transaction halfway through.
      this._domeLayerStack = layers;
      this._domeLayerStackView = DomeLayerView.Compile(layers);
      if (detached.PalettesChanged) {
        this._domePalettes = detached.Palettes ?? new List<DomePalette>();
        this.CompileDomePalettes();
      }
      this.SetDomeGlobalSpeedsWithoutNotification(
        detached.GlobalFadeSpeed, detached.GlobalHueSpeed);
      if (detached.ScenesChanged) {
        this._domeScenes = detached.Scenes ?? new List<DomeScene>();
        this._domeScenesView = DomeSceneView.Compile(this._domeScenes);
      }
      Volatile.Write(ref this._domeLayerStackSnapshot, layerSnapshot);
      this.PublishDomeShowStateSnapshot();

      this.RaisePropertyChanged(nameof(this.domeLayerStack));
      if (detached.PalettesChanged) {
        this.RaisePropertyChanged(nameof(this.domePalettes));
      }
      if (fadeChanged) {
        this.RaisePropertyChanged(nameof(this.domeGlobalFadeSpeed));
      }
      if (hueChanged) {
        this.RaisePropertyChanged(nameof(this.domeGlobalHueSpeed));
      }
      if (detached.ScenesChanged) {
        this.PublishSceneRetentionSettings();
        this.RaisePropertyChanged(nameof(this.domeScenes));
      }
      this.RaisePropertyChanged(
        DomeShowStateSnapshot.NotificationPropertyName);
    }

    private void PublishDomeShowStateSnapshot() {
      long generation = Interlocked.Increment(
        ref this.domeShowStateGeneration);
      var snapshot = new DomeShowStateSnapshot(
        generation,
        Volatile.Read(ref this._domeLayerStackSnapshot),
        this.compiledDomePalettes,
        this.domeGlobalFadeSpeed,
        this.domeGlobalHueSpeed);
      Volatile.Write(ref this._domeShowStateSnapshot, snapshot);
    }

    private void CompileDomePalettes() {
      this.compiledDomePalettes =
        DomeShowStateSnapshot.CompilePalettes(this._domePalettes);
    }

    private void RaisePropertyChanged(string propertyName) {
      this.PropertyChanged?.Invoke(
        this, new PropertyChangedEventArgs(propertyName));
    }
    // Mutable owner storage is never exposed; the public view is rebuilt only
    // when this branch changes.
    private Dictionary<string, int> _domeLayerFireCounters =
      new Dictionary<string, int>();
    private ImmutableDictionary<string, int> _domeLayerFireCountersView =
      ImmutableDictionary<string, int>.Empty;
    public override ImmutableDictionary<string, int> domeLayerFireCounters =>
      this._domeLayerFireCountersView;

    public void ReplaceDomeLayerFireCounters(
      IReadOnlyDictionary<string, int>? value
    ) {
      Dictionary<string, int> detached =
        ConfigurationGraphCopy.Dictionary(value);
      if (this.DispatchMutationIfRequired(
          () => this.ReplaceDomeLayerFireCounters(detached))) {
        return;
      }
      this._domeLayerFireCounters = detached;
      this._domeLayerFireCountersView = detached.ToImmutableDictionary();
      this.PublishDomeRuntimeFrameSettings();
      this.RaisePropertyChanged(nameof(this.domeLayerFireCounters));
    }

    // Parallel to _domeLayerFireCounters (see the Configuration interface): the
    // Clear button bumps these, a layer edge-detects the bump and drops its live
    // state. Mutable owner storage is never exposed.
    private Dictionary<string, int> _domeLayerClearCounters =
      new Dictionary<string, int>();
    private ImmutableDictionary<string, int> _domeLayerClearCountersView =
      ImmutableDictionary<string, int>.Empty;
    public override ImmutableDictionary<string, int> domeLayerClearCounters =>
      this._domeLayerClearCountersView;

    public void ReplaceDomeLayerClearCounters(
      IReadOnlyDictionary<string, int>? value
    ) {
      Dictionary<string, int> detached =
        ConfigurationGraphCopy.Dictionary(value);
      if (this.DispatchMutationIfRequired(
          () => this.ReplaceDomeLayerClearCounters(detached))) {
        return;
      }
      this._domeLayerClearCounters = detached;
      this._domeLayerClearCountersView = detached.ToImmutableDictionary();
      this.PublishDomeRuntimeFrameSettings();
      this.RaisePropertyChanged(nameof(this.domeLayerClearCounters));
    }

    // Maps from device ID to preset ID.
    private Dictionary<int, int> _midiDevices = new Dictionary<int, int>();
    private ImmutableDictionary<int, int> _midiDevicesView =
      ImmutableDictionary<int, int>.Empty;
    public override ImmutableDictionary<int, int> midiDevices =>
      this._midiDevicesView;

    public void ReplaceMidiDevices(IReadOnlyDictionary<int, int>? value) {
      Dictionary<int, int> detached = ConfigurationGraphCopy.Dictionary(value);
      if (this.DispatchMutationIfRequired(
          () => this.ReplaceMidiDevices(detached))) {
        return;
      }
      this._midiDevices = detached;
      this._midiDevicesView = detached.ToImmutableDictionary();
      this.PublishMidiDeviceSettings();
      this.RaisePropertyChanged(nameof(this.midiDevices));
    }
    private Dictionary<int, MidiPreset> _midiPresets = new Dictionary<int, MidiPreset>();
    private ImmutableDictionary<int, MidiPresetView> _midiPresetsView =
      ImmutableDictionary<int, MidiPresetView>.Empty;
    public override ImmutableDictionary<int, MidiPresetView> midiPresets =>
      this._midiPresetsView;

    public void ReplaceMidiPresets(
      IReadOnlyDictionary<int, MidiPreset>? value
    ) {
      Dictionary<int, MidiPreset> detached =
        ConfigurationGraphCopy.MidiPresets(value);
      if (this.DispatchMutationIfRequired(
          () => this.ReplaceMidiPresets(detached))) {
        return;
      }
      this._midiPresets = detached;
      this._midiPresetsView = MidiPresetView.Compile(detached);
      this.PublishMidiBindingSettings();
      this.RaisePropertyChanged(nameof(this.midiPresets));
    }

    public void UpsertMidiPreset(int id, MidiPreset value) {
      if (value == null) {
        throw new System.ArgumentNullException(nameof(value));
      }
      MidiPreset detached = ConfigurationGraphCopy.MidiPreset(value);
      if (this.DispatchMutationIfRequired(
          () => this.UpsertMidiPreset(id, detached))) {
        return;
      }
      var updated = new Dictionary<int, MidiPreset>(this._midiPresets);
      updated[id] = detached;
      this._midiPresets = updated;
      this._midiPresetsView = this._midiPresetsView.SetItem(
        id, MidiPresetView.FromPreset(detached));
      this.PublishMidiBindingSettings();
      this.RaisePropertyChanged(nameof(this.midiPresets));
    }

    public void RemoveMidiPreset(int id) {
      if (this.DispatchMutationIfRequired(() => this.RemoveMidiPreset(id))) {
        return;
      }
      if (!this._midiPresets.ContainsKey(id)) {
        return;
      }
      var updated = new Dictionary<int, MidiPreset>(this._midiPresets);
      updated.Remove(id);
      this._midiPresets = updated;
      this._midiPresetsView = this._midiPresetsView.Remove(id);
      this.PublishMidiBindingSettings();
      this.RaisePropertyChanged(nameof(this.midiPresets));
    }
    private long domeRuntimeFrameGeneration;
    private DomeRuntimeFrameSnapshot _domeRuntimeFrameSnapshot =
      DomeRuntimeFrameSnapshot.Empty;
    private long audioSettingsGeneration;
    private AudioSettingsSnapshot _audioSettingsSnapshot =
      AudioSettingsSnapshot.Empty;
    private long midiSettingsGeneration;
    private long midiDeviceGeneration;
    private long midiBindingGeneration;
    private MidiSettingsSnapshot _midiSettingsSnapshot =
      MidiSettingsSnapshot.Empty;
    private long orientationSettingsGeneration;
    private OrientationSettingsSnapshot _orientationSettingsSnapshot =
      OrientationSettingsSnapshot.Empty;
    private long domeOutputSettingsGeneration;
    private long domeOutputMappingGeneration;
    private long domeOutputTransportGeneration;
    private DomeOutputSettingsSnapshot _domeOutputSettingsSnapshot =
      DomeOutputSettingsSnapshot.Empty;
    private long beatSettingsGeneration;
    private BeatSettingsSnapshot _beatSettingsSnapshot =
      BeatSettingsSnapshot.Empty;
    private long sceneRetentionGeneration;
    private SceneRetentionSnapshot _sceneRetentionSnapshot =
      SceneRetentionSnapshot.Empty;

    DomeRuntimeFrameSnapshot
      IRuntimeSettingsConfiguration.DomeRuntimeFrameSnapshot =>
        Volatile.Read(ref this._domeRuntimeFrameSnapshot);
    AudioSettingsSnapshot
      IRuntimeSettingsConfiguration.AudioSettingsSnapshot =>
        Volatile.Read(ref this._audioSettingsSnapshot);
    MidiSettingsSnapshot
      IRuntimeSettingsConfiguration.MidiSettingsSnapshot =>
        Volatile.Read(ref this._midiSettingsSnapshot);
    OrientationSettingsSnapshot
      IRuntimeSettingsConfiguration.OrientationSettingsSnapshot =>
        Volatile.Read(ref this._orientationSettingsSnapshot);
    DomeOutputSettingsSnapshot
      IRuntimeSettingsConfiguration.DomeOutputSettingsSnapshot =>
        Volatile.Read(ref this._domeOutputSettingsSnapshot);
    BeatSettingsSnapshot
      IRuntimeSettingsConfiguration.BeatSettingsSnapshot =>
        Volatile.Read(ref this._beatSettingsSnapshot);
    SceneRetentionSnapshot
      IRuntimeSettingsConfiguration.SceneRetentionSnapshot =>
        Volatile.Read(ref this._sceneRetentionSnapshot);

    private void PublishDomeRuntimeFrameSettings() {
      Volatile.Write(
        ref this._domeRuntimeFrameSnapshot,
        new DomeRuntimeFrameSnapshot(
          Interlocked.Increment(ref this.domeRuntimeFrameGeneration),
          this.domeTestPattern,
          this.domeMaxBrightness,
          this.domeBrightness,
          this.orientationDeviceSpotlight,
          this._domeLayerFireCounters == null
            ? ImmutableDictionary<string, int>.Empty
            : this._domeLayerFireCounters.ToImmutableDictionary(),
          this._domeLayerClearCounters == null
            ? ImmutableDictionary<string, int>.Empty
            : this._domeLayerClearCounters.ToImmutableDictionary()));
    }

    private void PublishAudioSettings() {
      Volatile.Write(
        ref this._audioSettingsSnapshot,
        new AudioSettingsSnapshot(
          Interlocked.Increment(ref this.audioSettingsGeneration),
          this.audioDeviceID,
          this.beatInput));
    }

    private void PublishMidiEnabledSettings() =>
      this.PublishMidiSettings(false, false);

    private void PublishMidiDeviceSettings() =>
      this.PublishMidiSettings(true, true);

    private void PublishMidiBindingSettings() =>
      this.PublishMidiSettings(false, true);

    private void PublishMidiSettings(
      bool devicesChanged,
      bool bindingsChanged
    ) {
      var presets = ImmutableDictionary.CreateBuilder<int, MidiPreset>();
      foreach (KeyValuePair<int, MidiPreset> pair in this._midiPresets) {
        if (pair.Value != null) {
          presets[pair.Key] = (MidiPreset)pair.Value.Clone();
        }
      }
      Volatile.Write(
        ref this._midiSettingsSnapshot,
        new MidiSettingsSnapshot(
          Interlocked.Increment(ref this.midiSettingsGeneration),
          devicesChanged
            ? Interlocked.Increment(ref this.midiDeviceGeneration)
            : Volatile.Read(ref this.midiDeviceGeneration),
          bindingsChanged
            ? Interlocked.Increment(ref this.midiBindingGeneration)
            : Volatile.Read(ref this.midiBindingGeneration),
          this.midiInputEnabled,
          this._midiDevices == null
            ? ImmutableDictionary<int, int>.Empty
            : this._midiDevices.ToImmutableDictionary(),
          presets.ToImmutable()));
    }

    private void PublishOrientationAndFrameSettings() {
      this.PublishOrientationSettings();
      this.PublishDomeRuntimeFrameSettings();
    }

    private void PublishOrientationSettings() {
      Volatile.Write(
        ref this._orientationSettingsSnapshot,
        new OrientationSettingsSnapshot(
          Interlocked.Increment(ref this.orientationSettingsGeneration),
          this.orientationDeviceSpotlight,
          this.orientationCalibrate,
          this.wandSerialPort));
    }

    private void PublishDomeOutputState() =>
      this.PublishDomeOutputSettings(false, false);

    private void PublishDomeMappingSettings() =>
      this.PublishDomeOutputSettings(true, false);

    private void PublishDomeTransportSettings() =>
      this.PublishDomeOutputSettings(false, true);

    private void PublishDomeOutputSettings(
      bool mappingChanged,
      bool transportChanged
    ) {
      ImmutableArray<int> cables = ImmutableArray.Create(
        this._domeCableMapping);
      var ports = ImmutableArray.CreateBuilder<ImmutableArray<int>>(
        this._domePortMappings.Length);
      foreach (DomePortMapping? mapping in this._domePortMappings) {
        ports.Add(mapping?.ports == null
          ? ImmutableArray<int>.Empty
          : ImmutableArray.CreateRange(mapping.ports));
      }
      Volatile.Write(
        ref this._domeOutputSettingsSnapshot,
        new DomeOutputSettingsSnapshot(
          Interlocked.Increment(ref this.domeOutputSettingsGeneration),
          mappingChanged
            ? Interlocked.Increment(ref this.domeOutputMappingGeneration)
            : Volatile.Read(ref this.domeOutputMappingGeneration),
          transportChanged
            ? Interlocked.Increment(ref this.domeOutputTransportGeneration)
            : Volatile.Read(ref this.domeOutputTransportGeneration),
          this.domeEnabled,
          this.domeSimulationEnabled,
          this.domeBeagleboneOPCAddress,
          this.domeOutputInSeparateThread,
          cables,
          ports.MoveToImmutable()));
    }

    private void PublishBeatSettings() {
      Volatile.Write(
        ref this._beatSettingsSnapshot,
        new BeatSettingsSnapshot(
          Interlocked.Increment(ref this.beatSettingsGeneration),
          this.flashSpeed));
    }

    private void PublishSceneRetentionSettings() {
      var retained = ImmutableHashSet.CreateBuilder<string>();
      foreach (DomeScene scene in this._domeScenes) {
        if (scene.Layers == null) {
          continue;
        }
        foreach (DomeLayerSettings? layer in scene.Layers) {
          if (layer != null &&
              !string.IsNullOrWhiteSpace(layer.InstanceId)) {
            retained.Add(layer.InstanceId);
          }
        }
      }
      Volatile.Write(
        ref this._sceneRetentionSnapshot,
        new SceneRetentionSnapshot(
          Interlocked.Increment(ref this.sceneRetentionGeneration),
          retained.ToImmutable()));
    }

    internal SpectrumConfigurationDocument CreateDocument() {
      var document = new SpectrumConfigurationDocument {
        domeLayerStack = ConfigurationGraphCopy.Layers(
          this._domeLayerStack),
        domeCableMapping = ConfigurationGraphCopy.Array(
          this._domeCableMapping),
        domePortMappings = ConfigurationGraphCopy.PortMappings(
          this._domePortMappings),
        domeLayerFireCounters = ConfigurationGraphCopy.Dictionary(
          this._domeLayerFireCounters),
        domeLayerClearCounters = ConfigurationGraphCopy.Dictionary(
          this._domeLayerClearCounters),
        domeScenes = ConfigurationGraphCopy.Scenes(this._domeScenes),
        domePalettes = ConfigurationGraphCopy.Palettes(this._domePalettes),
        midiDevices = ConfigurationGraphCopy.Dictionary(this._midiDevices),
        midiPresets = ConfigurationGraphCopy.MidiPresets(this._midiPresets),
      };
      this.CopyScalarsTo(document);
      return document;
    }
  }

}
