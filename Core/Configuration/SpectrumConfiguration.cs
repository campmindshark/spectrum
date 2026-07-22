using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Spectrum.Base;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Spectrum {

  public class SpectrumConfiguration : Configuration,
      ILayerStackSnapshotSource, IDomeShowStateConfiguration,
      IRuntimeSettingsConfiguration, ConfigurationEditor {

    public event PropertyChangedEventHandler PropertyChanged;

    // Set once by the application composition root after deserialization.
    // Tests and serializer-only callers may leave it unset and use the
    // configuration synchronously on their own thread.
    private ApplicationStateDispatcher mutationDispatcher;

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
      ApplicationStateDispatcher existing = Interlocked.CompareExchange(
        ref this.mutationDispatcher, dispatcher, null);
      if (existing != null && !ReferenceEquals(existing, dispatcher)) {
        throw new System.InvalidOperationException(
          "A configuration dispatcher is already attached.");
      }
    }

    private bool DispatchMutationIfRequired<T>(
      string propertyName, T value
    ) {
      ApplicationStateDispatcher dispatcher =
        Volatile.Read(ref this.mutationDispatcher);
      if (dispatcher == null || dispatcher.CheckAccess()) {
        return false;
      }

      // Property setters converge here even when a future producer forgets to
      // use the dispatcher explicitly. Resolve the setter before queueing so a
      // programming error is reported on the calling thread.
      System.Reflection.PropertyInfo property =
        this.GetType().GetProperty(propertyName);
      if (property == null || !property.CanWrite) {
        throw new System.InvalidOperationException(
          "Configuration property is not writable: " + propertyName);
      }
      dispatcher.Post(() => property.SetValue(this, value));
      return true;
    }

    private bool DispatchMutationIfRequired(Action mutation) {
      ApplicationStateDispatcher dispatcher =
        Volatile.Read(ref this.mutationDispatcher);
      if (dispatcher == null || dispatcher.CheckAccess()) {
        return false;
      }
      dispatcher.Post(mutation);
      return true;
    }

    private void SetField<T>(ref T field, T value,
        Action publish = null,
        [CallerMemberName] string name = null) {
      if (this.DispatchMutationIfRequired(name, value)) {
        return;
      }
      if (EqualityComparer<T>.Default.Equals(field, value)) {
        return;
      }
      field = value;
      publish?.Invoke();
      this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private string _audioDeviceID = null;
    public string audioDeviceID {
      get => _audioDeviceID;
      set => SetField(ref _audioDeviceID, value, this.PublishAudioSettings);
    }

    private bool _domeEnabled = false;
    public bool domeEnabled {
      get => _domeEnabled;
      set => SetField(
        ref _domeEnabled, value, this.PublishDomeTransportSettings);
    }
    private bool _midiInputEnabled = false;
    public bool midiInputEnabled {
      get => _midiInputEnabled;
      set => SetField(
        ref _midiInputEnabled, value,
        this.PublishMidiEnabledSettings);
    }

    private bool _domeOutputInSeparateThread = false;
    public bool domeOutputInSeparateThread {
      get => _domeOutputInSeparateThread;
      set => SetField(
        ref _domeOutputInSeparateThread, value,
        this.PublishDomeTransportSettings);
    }

    private string _domeBeagleboneOPCAddress = "";
    public string domeBeagleboneOPCAddress {
      get => _domeBeagleboneOPCAddress;
      set => SetField(
        ref _domeBeagleboneOPCAddress, value,
        this.PublishDomeTransportSettings);
    }

    private bool _domeSimulationEnabled = false;
    public bool domeSimulationEnabled {
      get => _domeSimulationEnabled;
      set => SetField(
        ref _domeSimulationEnabled, value,
        this.PublishDomeOutputState);
    }
    private bool _webDomeSimulatorEnabled = true;
    public bool webDomeSimulatorEnabled {
      get => _webDomeSimulatorEnabled;
      set => SetField(ref _webDomeSimulatorEnabled, value);
    }
    private double _domeMaxBrightness = 0.5;
    public double domeMaxBrightness {
      get => _domeMaxBrightness;
      set => SetField(
        ref _domeMaxBrightness, value,
        this.PublishDomeRuntimeFrameSettings);
    }
    private double _domeBrightness = 0.1;
    public double domeBrightness {
      get => _domeBrightness;
      set => SetField(
        ref _domeBrightness, value,
        this.PublishDomeRuntimeFrameSettings);
    }
    private int _domeTestPattern = 0;
    public int domeTestPattern {
      get => _domeTestPattern;
      set => SetField(
        ref _domeTestPattern, value,
        this.PublishDomeRuntimeFrameSettings);
    }
    // Empty or omitted document collections are projected as cached empty
    // immutable views.
    private List<DomeLayerSettings> _domeLayerStack = null;
    private ImmutableArray<DomeLayerView> _domeLayerStackView =
      ImmutableArray<DomeLayerView>.Empty;
    private static readonly LayerStackService layerStackService =
      new LayerStackService(BuiltInDomeLayerCatalog.Metadata);
    private LayerStackSnapshot _domeLayerStackSnapshot =
      LayerStackSnapshot.Empty;
    public ImmutableArray<DomeLayerView> domeLayerStack =>
      this._domeLayerStackView;

    public void ReplaceDomeLayerStack(
      IReadOnlyList<DomeLayerSettings> value
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
        (List<DomeLayerSettings> normalized, string error) =
            layerStackService.Normalize(value);
        if (error == null) {
          published = normalized;
        }
      }
      (LayerStackSnapshot snapshot, string snapshotError) =
        layerStackService.CreateSnapshot(published);
      if (snapshotError != null) {
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
    private int[] _domeCableMapping = null;
    private ImmutableArray<int> _domeCableMappingView =
      ImmutableArray<int>.Empty;
    public ImmutableArray<int> domeCableMapping =>
      this._domeCableMappingView;

    public void ReplaceDomeCableMapping(IReadOnlyList<int> value) {
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
    private DomePortMapping[] _domePortMappings = null;
    private ImmutableArray<ImmutableArray<int>> _domePortMappingsView =
      ImmutableArray<ImmutableArray<int>>.Empty;
    public ImmutableArray<ImmutableArray<int>> domePortMappings =>
      this._domePortMappingsView;

    public void ReplaceDomePortMappings(
      IReadOnlyList<DomePortMapping> value
    ) {
      DomePortMapping[] detached =
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
      IReadOnlyList<DomePortMapping> mappings
    ) {
      if (mappings == null || mappings.Count == 0) {
        return ImmutableArray<ImmutableArray<int>>.Empty;
      }
      var result = ImmutableArray.CreateBuilder<ImmutableArray<int>>(
        mappings.Count);
      foreach (DomePortMapping mapping in mappings) {
        result.Add(mapping?.ports == null
          ? ImmutableArray<int>.Empty
          : ImmutableArray.CreateRange(mapping.ports));
      }
      return result.MoveToImmutable();
    }

    // Cross-layer visual state; per-visualizer tuning lives in each layer's
    // renderer parameter bag instead (see Configuration).
    private double _domeGlobalFadeSpeed = 0;
    public double domeGlobalFadeSpeed {
      get => _domeGlobalFadeSpeed;
      set {
        if (this.DispatchMutationIfRequired(
            nameof(this.domeGlobalFadeSpeed), value)) {
          return;
        }
        if (this._domeGlobalFadeSpeed == value) {
          return;
        }
        this._domeGlobalFadeSpeed = value;
        this.PublishDomeShowStateSnapshot();
        this.RaisePropertyChanged(nameof(this.domeGlobalFadeSpeed));
        this.RaisePropertyChanged(
          DomeShowStateSnapshot.NotificationPropertyName);
      }
    }
    private double _domeGlobalHueSpeed = 1;
    public double domeGlobalHueSpeed {
      get => _domeGlobalHueSpeed;
      set {
        if (this.DispatchMutationIfRequired(
            nameof(this.domeGlobalHueSpeed), value)) {
          return;
        }
        if (this._domeGlobalHueSpeed == value) {
          return;
        }
        this._domeGlobalHueSpeed = value;
        this.PublishDomeShowStateSnapshot();
        this.RaisePropertyChanged(nameof(this.domeGlobalHueSpeed));
        this.RaisePropertyChanged(
          DomeShowStateSnapshot.NotificationPropertyName);
      }
    }
    // Null private storage is projected as an empty immutable view.
    private List<DomeScene> _domeScenes = null;
    private ImmutableArray<DomeSceneView> _domeScenesView =
      ImmutableArray<DomeSceneView>.Empty;
    public ImmutableArray<DomeSceneView> domeScenes => this._domeScenesView;

    public void ReplaceDomeScenes(IReadOnlyList<DomeScene> value) {
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
    private List<DomePalette> _domePalettes = null;
    public ImmutableArray<DomePaletteSnapshot> domePalettes =>
      this.compiledDomePalettes;

    public void ReplaceDomePalettes(IReadOnlyList<DomePalette> value) {
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
      ApplicationStateDispatcher dispatcher =
        Volatile.Read(ref this.mutationDispatcher);
      if (dispatcher != null && !dispatcher.CheckAccess()) {
        dispatcher.Post(() => this.ApplyDomeShowState(detached));
        return;
      }

      (List<DomeLayerSettings> layers, LayerStackSnapshot layerSnapshot) =
        this.PrepareLayerStack(detached.Layers);
      bool fadeChanged = this._domeGlobalFadeSpeed != detached.GlobalFadeSpeed;
      bool hueChanged = this._domeGlobalHueSpeed != detached.GlobalHueSpeed;

      // Assign every persisted field and compile the deep immutable generation
      // before the first notification. Subscribers can read any combination of
      // these properties without observing the transaction halfway through.
      this._domeLayerStack = layers;
      this._domeLayerStackView = DomeLayerView.Compile(layers);
      if (detached.PalettesChanged) {
        this._domePalettes = detached.Palettes;
        this.CompileDomePalettes();
      }
      this._domeGlobalFadeSpeed = detached.GlobalFadeSpeed;
      this._domeGlobalHueSpeed = detached.GlobalHueSpeed;
      if (detached.ScenesChanged) {
        this._domeScenes = detached.Scenes;
        this._domeScenesView = DomeSceneView.Compile(detached.Scenes);
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
        Volatile.Read(ref this._domeLayerStackSnapshot) ??
          LayerStackSnapshot.Empty,
        this.compiledDomePalettes,
        this._domeGlobalFadeSpeed,
        this._domeGlobalHueSpeed);
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
    public ImmutableDictionary<string, int> domeLayerFireCounters =>
      this._domeLayerFireCountersView;

    public void ReplaceDomeLayerFireCounters(
      IReadOnlyDictionary<string, int> value
    ) {
      Dictionary<string, int> detached =
        ConfigurationGraphCopy.Dictionary(value);
      if (this.DispatchMutationIfRequired(
          () => this.ReplaceDomeLayerFireCounters(detached))) {
        return;
      }
      this._domeLayerFireCounters = detached;
      this._domeLayerFireCountersView = detached == null
        ? ImmutableDictionary<string, int>.Empty
        : detached.ToImmutableDictionary();
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
    public ImmutableDictionary<string, int> domeLayerClearCounters =>
      this._domeLayerClearCountersView;

    public void ReplaceDomeLayerClearCounters(
      IReadOnlyDictionary<string, int> value
    ) {
      Dictionary<string, int> detached =
        ConfigurationGraphCopy.Dictionary(value);
      if (this.DispatchMutationIfRequired(
          () => this.ReplaceDomeLayerClearCounters(detached))) {
        return;
      }
      this._domeLayerClearCounters = detached;
      this._domeLayerClearCountersView = detached == null
        ? ImmutableDictionary<string, int>.Empty
        : detached.ToImmutableDictionary();
      this.PublishDomeRuntimeFrameSettings();
      this.RaisePropertyChanged(nameof(this.domeLayerClearCounters));
    }

    // maps from device ID to preset ID
    private bool _vjHUDEnabled = false;
    public bool vjHUDEnabled {
      get => _vjHUDEnabled;
      set => SetField(ref _vjHUDEnabled, value);
    }
    private Dictionary<int, int> _midiDevices = new Dictionary<int, int>();
    private ImmutableDictionary<int, int> _midiDevicesView =
      ImmutableDictionary<int, int>.Empty;
    public ImmutableDictionary<int, int> midiDevices => this._midiDevicesView;

    public void ReplaceMidiDevices(IReadOnlyDictionary<int, int> value) {
      Dictionary<int, int> detached = ConfigurationGraphCopy.Dictionary(value);
      if (this.DispatchMutationIfRequired(
          () => this.ReplaceMidiDevices(detached))) {
        return;
      }
      this._midiDevices = detached;
      this._midiDevicesView = detached == null
        ? ImmutableDictionary<int, int>.Empty
        : detached.ToImmutableDictionary();
      this.PublishMidiDeviceSettings();
      this.RaisePropertyChanged(nameof(this.midiDevices));
    }
    private Dictionary<int, MidiPreset> _midiPresets = new Dictionary<int, MidiPreset>();
    private ImmutableDictionary<int, MidiPresetView> _midiPresetsView =
      ImmutableDictionary<int, MidiPresetView>.Empty;
    public ImmutableDictionary<int, MidiPresetView> midiPresets =>
      this._midiPresetsView;

    public void ReplaceMidiPresets(
      IReadOnlyDictionary<int, MidiPreset> value
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
      MidiPreset detached = ConfigurationGraphCopy.MidiPreset(value);
      if (this.DispatchMutationIfRequired(
          () => this.UpsertMidiPreset(id, detached))) {
        return;
      }
      var updated = this._midiPresets == null
        ? new Dictionary<int, MidiPreset>()
        : new Dictionary<int, MidiPreset>(this._midiPresets);
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
      if (this._midiPresets == null ||
          !this._midiPresets.ContainsKey(id)) {
        return;
      }
      var updated = new Dictionary<int, MidiPreset>(this._midiPresets);
      updated.Remove(id);
      this._midiPresets = updated;
      this._midiPresetsView = this._midiPresetsView.Remove(id);
      this.PublishMidiBindingSettings();
      this.RaisePropertyChanged(nameof(this.midiPresets));
    }
    private Dictionary<int, MidiLevelDriverPreset> _midiLevelDriverChannels =
      new Dictionary<int, MidiLevelDriverPreset>();

    private double _flashSpeed = 0.0;
    public double flashSpeed {
      get => _flashSpeed;
      set => SetField(ref _flashSpeed, value, this.PublishBeatSettings);
    }

    // 0 = human, 1 = Madmom, 2 = Pro DJ Link
    private int _beatInput = 0;
    public int beatInput {
      get => _beatInput;
      set => SetField(ref _beatInput, value, this.PublishAudioSettings);
    }

    private int _orientationDeviceSpotlight = 0;
    public int orientationDeviceSpotlight {
      get => _orientationDeviceSpotlight;
      set => SetField(
        ref _orientationDeviceSpotlight, value,
        this.PublishOrientationAndFrameSettings);
    }
    private bool _orientationCalibrate = false;
    public bool orientationCalibrate {
      get => _orientationCalibrate;
      set => SetField(
        ref _orientationCalibrate, value,
        this.PublishOrientationSettings);
    }
    // Initialized to "" (not left null) so both the receiver's IsNullOrEmpty
    // check and StringParameter.Coerce/Get have a real string, and a
    // spectrum_config.xml predating this property (which leaves the field
    // untouched on deserialize) still reads as "no serial input".
    private string _wandSerialPort = "";
    public string wandSerialPort {
      get => _wandSerialPort;
      set => SetField(
        ref _wandSerialPort, value, this.PublishOrientationSettings);
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
          this._domeTestPattern,
          this._domeMaxBrightness,
          this._domeBrightness,
          this._orientationDeviceSpotlight,
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
          this._audioDeviceID,
          this._beatInput));
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
      if (this._midiPresets != null) {
        foreach (KeyValuePair<int, MidiPreset> pair in this._midiPresets) {
          presets[pair.Key] = pair.Value == null
            ? null
            : (MidiPreset)pair.Value.Clone();
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
          this._midiInputEnabled,
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
          this._orientationDeviceSpotlight,
          this._orientationCalibrate,
          this._wandSerialPort ?? ""));
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
      ImmutableArray<int> cables = this._domeCableMapping == null
        ? ImmutableArray<int>.Empty
        : ImmutableArray.Create(this._domeCableMapping);
      var ports = ImmutableArray.CreateBuilder<ImmutableArray<int>>(
        this._domePortMappings?.Length ?? 0);
      if (this._domePortMappings != null) {
        foreach (DomePortMapping mapping in this._domePortMappings) {
          ports.Add(mapping?.ports == null
            ? ImmutableArray<int>.Empty
            : ImmutableArray.CreateRange(mapping.ports));
        }
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
          this._domeEnabled,
          this._domeSimulationEnabled,
          this._domeBeagleboneOPCAddress ?? "",
          this._domeOutputInSeparateThread,
          cables,
          ports.MoveToImmutable()));
    }

    private void PublishBeatSettings() {
      var channels = ImmutableDictionary.CreateBuilder<
        int, MidiLevelDriverSettingsSnapshot>();
      if (this._midiLevelDriverChannels != null) {
        foreach (KeyValuePair<int, MidiLevelDriverPreset> pair in
            this._midiLevelDriverChannels) {
          MidiLevelDriverPreset preset = pair.Value;
          if (preset != null) {
            channels[pair.Key] = new MidiLevelDriverSettingsSnapshot(
              preset.AttackTime,
              preset.PeakLevel,
              preset.DecayTime,
              preset.SustainLevel,
              preset.ReleaseTime);
          }
        }
      }
      Volatile.Write(
        ref this._beatSettingsSnapshot,
        new BeatSettingsSnapshot(
          Interlocked.Increment(ref this.beatSettingsGeneration),
          this._flashSpeed,
          channels.ToImmutable()));
    }

    private void PublishSceneRetentionSettings() {
      var retained = ImmutableHashSet.CreateBuilder<string>();
      if (this._domeScenes != null) {
        foreach (DomeScene scene in this._domeScenes) {
          if (scene?.Layers == null) {
            continue;
          }
          foreach (DomeLayerSettings layer in scene.Layers) {
            if (layer != null &&
                !string.IsNullOrWhiteSpace(layer.InstanceId)) {
              retained.Add(layer.InstanceId);
            }
          }
        }
      }
      Volatile.Write(
        ref this._sceneRetentionSnapshot,
        new SceneRetentionSnapshot(
          Interlocked.Increment(ref this.sceneRetentionGeneration),
          retained.ToImmutable()));
    }

    internal void ReplaceMidiLevelDriverChannels(
      IReadOnlyDictionary<int, MidiLevelDriverPreset> channels
    ) {
      this._midiLevelDriverChannels =
        ConfigurationGraphCopy.MidiLevelDriverChannels(channels);
      this.PublishBeatSettings();
    }

    internal SpectrumConfigurationDocument CreateDocument() =>
      new SpectrumConfigurationDocument {
        audioDeviceID = this._audioDeviceID,
        domeEnabled = this._domeEnabled,
        midiInputEnabled = this._midiInputEnabled,
        domeOutputInSeparateThread = this._domeOutputInSeparateThread,
        domeBeagleboneOPCAddress = this._domeBeagleboneOPCAddress,
        domeSimulationEnabled = this._domeSimulationEnabled,
        webDomeSimulatorEnabled = this._webDomeSimulatorEnabled,
        domeMaxBrightness = this._domeMaxBrightness,
        domeBrightness = this._domeBrightness,
        domeTestPattern = this._domeTestPattern,
        domeLayerStack = ConfigurationGraphCopy.Layers(
          this._domeLayerStack),
        domeCableMapping = ConfigurationGraphCopy.Array(
          this._domeCableMapping),
        domePortMappings = ConfigurationGraphCopy.PortMappings(
          this._domePortMappings),
        domeGlobalFadeSpeed = this._domeGlobalFadeSpeed,
        domeGlobalHueSpeed = this._domeGlobalHueSpeed,
        domeLayerFireCounters = ConfigurationGraphCopy.Dictionary(
          this._domeLayerFireCounters),
        domeLayerClearCounters = ConfigurationGraphCopy.Dictionary(
          this._domeLayerClearCounters),
        domeScenes = ConfigurationGraphCopy.Scenes(this._domeScenes),
        domePalettes = ConfigurationGraphCopy.Palettes(this._domePalettes),
        vjHUDEnabled = this._vjHUDEnabled,
        midiDevices = ConfigurationGraphCopy.Dictionary(this._midiDevices),
        midiPresets = ConfigurationGraphCopy.MidiPresets(this._midiPresets),
        midiLevelDriverChannels =
          ConfigurationGraphCopy.MidiLevelDriverChannels(
            this._midiLevelDriverChannels),
        flashSpeed = this._flashSpeed,
        beatInput = this._beatInput,
        orientationDeviceSpotlight = this._orientationDeviceSpotlight,
        orientationCalibrate = this._orientationCalibrate,
        wandSerialPort = this._wandSerialPort,
      };
  }

}
