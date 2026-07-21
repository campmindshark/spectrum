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
      IRuntimeSettingsConfiguration {

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

    private void CheckSerializerCollectionReadAccess(
      [CallerMemberName] string name = null
    ) {
      ApplicationStateDispatcher dispatcher =
        Volatile.Read(ref this.mutationDispatcher);
      if (dispatcher != null && !dispatcher.CheckAccess()) {
        throw new InvalidOperationException(
          "Serializer-facing configuration collection must be read on the " +
          "application-state owner thread: " + name);
      }
    }

    private void DomePalettePropertyChanged(
      object sender, PropertyChangedEventArgs e
    ) {
      ApplicationStateDispatcher dispatcher =
        Volatile.Read(ref this.mutationDispatcher);
      if (dispatcher != null && !dispatcher.CheckAccess()) {
        dispatcher.Post(() => this.DomePalettePropertyChanged(sender, e));
        return;
      }
      this.CompileDomePalettes();
      this.PublishDomeShowStateSnapshot();
      this.RaisePropertyChanged("domePalettes." + e.PropertyName);
      this.RaisePropertyChanged(
        DomeShowStateSnapshot.NotificationPropertyName);
    }

    private void SubscribePalettes(List<DomePalette> palettes) {
      if (palettes == null) {
        return;
      }
      foreach (DomePalette palette in palettes) {
        if (palette != null) {
          palette.PropertyChanged += this.DomePalettePropertyChanged;
        }
      }
    }

    private void UnsubscribePalettes(List<DomePalette> palettes) {
      if (palettes == null) {
        return;
      }
      foreach (DomePalette palette in palettes) {
        if (palette != null) {
          palette.PropertyChanged -= this.DomePalettePropertyChanged;
        }
      }
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

    private bool _midiInputInSeparateThread = false;
    public bool midiInputInSeparateThread {
      get => _midiInputInSeparateThread;
      set => SetField(ref _midiInputInSeparateThread, value);
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
    // Left null by default (not a pre-filled list): XSerializer deserializes a
    // collection property by calling IList.Add on the *existing* instance, so a
    // non-null default would double up the persisted entries on load.
    private List<DomeLayerSettings> _domeLayerStack = null;
    private static readonly LayerStackService layerStackService =
      new LayerStackService(DomeLayerCatalog.Metadata);
    private LayerStackSnapshot _domeLayerStackSnapshot =
      LayerStackSnapshot.Empty;
    public List<DomeLayerSettings> domeLayerStack {
      get {
        this.CheckSerializerCollectionReadAccess();
        return _domeLayerStack;
      }
      set {
        if (this.DispatchMutationIfRequired(
            nameof(this.domeLayerStack), value)) {
          return;
        }
        (List<DomeLayerSettings> published, LayerStackSnapshot snapshot) =
          this.PrepareLayerStack(value);
        if (EqualityComparer<List<DomeLayerSettings>>.Default.Equals(
              this._domeLayerStack, published)) {
          return;
        }
        this._domeLayerStack = published;
        Volatile.Write(ref this._domeLayerStackSnapshot, snapshot);
        this.PublishDomeShowStateSnapshot();
        this.RaisePropertyChanged(nameof(this.domeLayerStack));
        this.RaisePropertyChanged(
          DomeShowStateSnapshot.NotificationPropertyName);
      }
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
    // Left null by default (rather than a pre-filled identity array): XSerializer
    // deserializes an array property by calling IList.Add on the *existing*
    // instance, and a non-null array is fixed-size, so a pre-initialized default
    // throws NotSupportedException ("Collection was of a fixed size") on load.
    // LEDDomeOutput.RebuildOutputMapping already treats null as the identity
    // mapping, so a null default is equivalent to the legacy hard-coded wiring.
    private int[] _domeCableMapping = null;
    public int[] domeCableMapping {
      get => CloneArray(_domeCableMapping);
      set => SetField(
        ref _domeCableMapping, CloneArray(value),
        this.PublishDomeMappingSettings);
    }
    // Five independently owned mappings, one for each dome-side box. This is an
    // array of DTOs rather than a preinitialized jagged array so XSerializer can
    // reliably construct old and new configuration files. Both boundaries are
    // deep-cloned so callers cannot mutate live configuration in place.
    private DomePortMapping[] _domePortMappings = null;
    public DomePortMapping[] domePortMappings {
      get => ClonePortMappings(_domePortMappings);
      set => SetField(
        ref _domePortMappings, ClonePortMappings(value),
        this.PublishDomeMappingSettings);
    }

    private static int[] CloneArray(int[] values) =>
      values == null ? null : (int[])values.Clone();

    private static DomePortMapping[] ClonePortMappings(
      IReadOnlyList<DomePortMapping> mappings
    ) {
      if (mappings == null) {
        return null;
      }
      var clone = new DomePortMapping[mappings.Count];
      for (int box = 0; box < mappings.Count; box++) {
        clone[box] = new DomePortMapping(mappings[box]?.ports);
      }
      return clone;
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
    // Left null by default (not a pre-filled list) for the same reason as
    // domeLayerStack: XSerializer deserializes a collection property by calling
    // IList.Add on the *existing* instance, so a non-null default would double up
    // the persisted scenes on load. Null reads as "no saved scenes."
    private List<DomeScene> _domeScenes = null;
    public List<DomeScene> domeScenes {
      get {
        this.CheckSerializerCollectionReadAccess();
        return _domeScenes;
      }
      set => SetField(
        ref _domeScenes, value, this.PublishSceneRetentionSettings);
    }
    // Left null by default for the same reason as domeScenes: XSerializer
    // deserializes a collection property by calling IList.Add on the *existing*
    // instance, so a non-null default would double up the persisted palettes on
    // load. Null reads as "no saved palettes."
    private List<DomePalette> _domePalettes = null;
    public List<DomePalette> domePalettes {
      get {
        this.CheckSerializerCollectionReadAccess();
        return _domePalettes;
      }
      set {
        if (this.DispatchMutationIfRequired(
            nameof(this.domePalettes), value)) {
          return;
        }
        if (ReferenceEquals(this._domePalettes, value)) {
          return;
        }
        this.UnsubscribePalettes(this._domePalettes);
        this._domePalettes = value;
        this.SubscribePalettes(this._domePalettes);
        this.CompileDomePalettes();
        this.PublishDomeShowStateSnapshot();
        this.RaisePropertyChanged(nameof(this.domePalettes));
        this.RaisePropertyChanged(
          DomeShowStateSnapshot.NotificationPropertyName);
      }
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
      ApplicationStateDispatcher dispatcher =
        Volatile.Read(ref this.mutationDispatcher);
      if (dispatcher != null && !dispatcher.CheckAccess()) {
        dispatcher.Post(() => this.ApplyDomeShowState(update));
        return;
      }

      (List<DomeLayerSettings> layers, LayerStackSnapshot layerSnapshot) =
        this.PrepareLayerStack(update.Layers);
      bool layersChanged = !ReferenceEquals(this._domeLayerStack, layers);
      bool palettesChanged = !ReferenceEquals(
        this._domePalettes, update.Palettes);
      bool fadeChanged = this._domeGlobalFadeSpeed != update.GlobalFadeSpeed;
      bool hueChanged = this._domeGlobalHueSpeed != update.GlobalHueSpeed;
      bool scenesChanged = !ReferenceEquals(this._domeScenes, update.Scenes);
      if (!layersChanged && !palettesChanged && !fadeChanged &&
          !hueChanged && !scenesChanged) {
        return;
      }

      // Assign every persisted field and compile the deep immutable generation
      // before the first notification. Subscribers can read any combination of
      // these properties without observing the transaction halfway through.
      if (palettesChanged) {
        this.UnsubscribePalettes(this._domePalettes);
      }
      this._domeLayerStack = layers;
      this._domePalettes = update.Palettes;
      this._domeGlobalFadeSpeed = update.GlobalFadeSpeed;
      this._domeGlobalHueSpeed = update.GlobalHueSpeed;
      this._domeScenes = update.Scenes;
      if (palettesChanged) {
        this.SubscribePalettes(this._domePalettes);
        this.CompileDomePalettes();
      }
      Volatile.Write(ref this._domeLayerStackSnapshot, layerSnapshot);
      if (layersChanged || palettesChanged || fadeChanged || hueChanged) {
        this.PublishDomeShowStateSnapshot();
      }

      if (layersChanged) {
        this.RaisePropertyChanged(nameof(this.domeLayerStack));
      }
      if (palettesChanged) {
        this.RaisePropertyChanged(nameof(this.domePalettes));
      }
      if (fadeChanged) {
        this.RaisePropertyChanged(nameof(this.domeGlobalFadeSpeed));
      }
      if (hueChanged) {
        this.RaisePropertyChanged(nameof(this.domeGlobalHueSpeed));
      }
      if (scenesChanged) {
        this.PublishSceneRetentionSettings();
        this.RaisePropertyChanged(nameof(this.domeScenes));
      }
      if (layersChanged || palettesChanged || fadeChanged || hueChanged) {
        this.RaisePropertyChanged(
          DomeShowStateSnapshot.NotificationPropertyName);
      }
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
    // Non-null default, matching midiDevices/channelToMidiLevelDriverPreset
    // (Dictionary properties round-trip fine through XSerializer, unlike
    // List/array properties).
    private Dictionary<string, int> _domeLayerFireCounters =
      new Dictionary<string, int>();
    public Dictionary<string, int> domeLayerFireCounters {
      get {
        this.CheckSerializerCollectionReadAccess();
        return _domeLayerFireCounters;
      }
      set => SetField(
        ref _domeLayerFireCounters, value,
        this.PublishDomeRuntimeFrameSettings);
    }

    // Parallel to _domeLayerFireCounters (see the Configuration interface): the
    // Clear button bumps these, a layer edge-detects the bump and drops its live
    // state. Non-null default for the same XSerializer round-trip reason.
    private Dictionary<string, int> _domeLayerClearCounters =
      new Dictionary<string, int>();
    public Dictionary<string, int> domeLayerClearCounters {
      get {
        this.CheckSerializerCollectionReadAccess();
        return _domeLayerClearCounters;
      }
      set => SetField(
        ref _domeLayerClearCounters, value,
        this.PublishDomeRuntimeFrameSettings);
    }

    // maps from device ID to preset ID
    private bool _vjHUDEnabled = false;
    public bool vjHUDEnabled {
      get => _vjHUDEnabled;
      set => SetField(ref _vjHUDEnabled, value);
    }
    private Dictionary<int, int> _midiDevices = new Dictionary<int, int>();
    public Dictionary<int, int> midiDevices {
      get {
        this.CheckSerializerCollectionReadAccess();
        return _midiDevices;
      }
      set => SetField(
        ref _midiDevices, value, this.PublishMidiDeviceSettings);
    }
    private Dictionary<int, MidiPreset> _midiPresets = new Dictionary<int, MidiPreset>();
    public Dictionary<int, MidiPreset> midiPresets {
      get {
        this.CheckSerializerCollectionReadAccess();
        return _midiPresets;
      }
      set => SetField(
        ref _midiPresets, value, this.PublishMidiBindingSettings);
    }
    private Dictionary<string, ILevelDriverPreset> _levelDriverPresets = new Dictionary<string, ILevelDriverPreset>();
    public Dictionary<string, ILevelDriverPreset> levelDriverPresets {
      get {
        this.CheckSerializerCollectionReadAccess();
        return _levelDriverPresets;
      }
      set => SetField(
        ref _levelDriverPresets, value, this.PublishBeatSettings);
    }
    private Dictionary<int, string> _channelToAudioLevelDriverPreset = new Dictionary<int, string>();
    public Dictionary<int, string> channelToAudioLevelDriverPreset {
      get {
        this.CheckSerializerCollectionReadAccess();
        return _channelToAudioLevelDriverPreset;
      }
      set => SetField(ref _channelToAudioLevelDriverPreset, value);
    }
    private Dictionary<int, string> _channelToMidiLevelDriverPreset = new Dictionary<int, string>();
    public Dictionary<int, string> channelToMidiLevelDriverPreset {
      get {
        this.CheckSerializerCollectionReadAccess();
        return _channelToMidiLevelDriverPreset;
      }
      set => SetField(
        ref _channelToMidiLevelDriverPreset, value,
        this.PublishBeatSettings);
    }

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
      var presets = ImmutableDictionary.CreateBuilder<
        string, MidiLevelDriverSettingsSnapshot>();
      if (this._levelDriverPresets != null) {
        foreach (KeyValuePair<string, ILevelDriverPreset> pair in
            this._levelDriverPresets) {
          if (pair.Key != null && pair.Value is MidiLevelDriverPreset preset) {
            presets[pair.Key] = new MidiLevelDriverSettingsSnapshot(
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
          this._channelToMidiLevelDriverPreset == null
            ? ImmutableDictionary<int, string>.Empty
            : this._channelToMidiLevelDriverPreset.ToImmutableDictionary(),
          presets.ToImmutable()));
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
  }

}
