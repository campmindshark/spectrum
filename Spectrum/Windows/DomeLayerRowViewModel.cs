using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Spectrum.Base;
using MediaColor = System.Windows.Media.Color;

namespace Spectrum {

  // A minimal ICommand that always executes and forwards to a delegate. Used by
  // the per-row layer buttons (remove / up / down).
  public class RelayCommand : ICommand {
    private readonly Action execute;
    public RelayCommand(Action execute) {
      this.execute = execute;
    }
    // Always executable; no state changes, so the standard add/remove-nop form
    // satisfies ICommand without an unused backing field.
    public event EventHandler CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object parameter) => true;
    public void Execute(object parameter) => this.execute();
  }

  // One selectable visualizer in a layer row's picker.
  public class DomeLayerVisualizerOption {
    public string Key { get; set; }
    public string Label { get; set; }
  }

  public class DomeLayerOperationOption {
    public string Id { get; set; }
    public string Label { get; set; }
  }

  // View-model wrapping one DomeLayerParam descriptor plus its current value for
  // the generic per-layer param editors. The row DataTemplate binds the control
  // matching Type (Slider / CheckBox / ComboBox / ColorPicker / date TextBox)
  // via the Is* flags; editing any of the value facets raises Changed so the
  // owning row republishes. Values are always stored as double (Bool 0/1, Enum
  // index, Color a packed 0xRRGGBB int, Date as yyyyMMdd).
  public class LayerParamViewModel : INotifyPropertyChanged {
    public event PropertyChangedEventHandler PropertyChanged;
    public event Action Changed;

    private readonly DomeLayerParam descriptor;

    public LayerParamViewModel(
      DomeLayerParam descriptor, double value, bool isOperationParameter
    ) {
      this.descriptor = descriptor;
      this.value = value;
      this.storedValue = value;
      this.IsOperationParameter = isOperationParameter;
    }

    public string Key => this.descriptor.Key;
    public string Label => this.descriptor.Label;
    public double Min => this.descriptor.Min;
    public double Max => this.descriptor.Max;
    public double Step => this.descriptor.Step;
    public IReadOnlyList<string> Options => this.descriptor.Options;
    public bool IsOperationParameter { get; }

    public bool IsDouble => this.descriptor.Type == DomeLayerParamType.Double;
    public bool IsBool => this.descriptor.Type == DomeLayerParamType.Bool;
    public bool IsEnum => this.descriptor.Type == DomeLayerParamType.Enum;
    public bool IsColor => this.descriptor.Type == DomeLayerParamType.Color;
    public bool IsDate => this.descriptor.Type == DomeLayerParamType.Date;

    // Value is the live value shown by the editor. StoredValue remains the
    // persisted setting while a runtime playback display advances Value.
    private double value;
    private double storedValue;
    internal double StoredValue => this.storedValue;
    public double Value {
      get => this.value;
      set {
        if (this.value == value && this.storedValue == value) {
          return;
        }
        this.value = value;
        this.storedValue = value;
        this.Raise(nameof(Value));
        this.Raise(nameof(BoolValue));
        this.Raise(nameof(IntValue));
        this.Raise(nameof(ColorValue));
        this.Raise(nameof(DateText));
        this.Changed?.Invoke();
      }
    }

    // Playback can move a displayed timeline without turning every animation
    // tick into a persisted layer-stack edit. User edits still go through the
    // Value setter above and raise Changed as usual.
    internal void SetDisplayedValue(double value) {
      if (this.value == value) {
        return;
      }
      this.value = value;
      this.Raise(nameof(Value));
      this.Raise(nameof(BoolValue));
      this.Raise(nameof(IntValue));
      this.Raise(nameof(ColorValue));
      this.Raise(nameof(DateText));
    }

    // CheckBox facet for Bool params.
    public bool BoolValue {
      get => this.value != 0;
      set => this.Value = value ? 1 : 0;
    }

    // ComboBox SelectedIndex facet for Enum params.
    public int IntValue {
      get => (int)this.value;
      set => this.Value = value;
    }

    // ColorPicker.SelectedColor facet for Color params: the packed 0xRRGGBB
    // value viewed as an opaque WPF Color. Alpha is always full since the bag
    // only stores RGB.
    public MediaColor? ColorValue {
      get {
        int packed = (int)this.value;
        return MediaColor.FromRgb(
          (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed
        );
      }
      set {
        MediaColor c = value ?? default;
        this.Value = (c.R << 16) | (c.G << 8) | c.B;
      }
    }

    // TextBox facet for Date params. Invalid input leaves the stored value
    // untouched and surfaces through WPF's exception validation.
    public string DateText {
      get => DomeLayerDate.Format(this.value);
      set {
        if (!DomeLayerDate.TryParse(value, out double encoded)) {
          throw new FormatException("Use YYYY-MM-DD.");
        }
        this.Value = encoded;
      }
    }

    private void Raise(string name) {
      this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
  }

  // View-model backing one row of the dome layers panel (native GUI). Editing any
  // user-facing field raises Changed so the owning DomeLayersController
  // republishes the whole stack to config; the reorder/remove buttons raise the
  // corresponding *Requested events the controller wires up.
  public class DomeLayerRowViewModel : INotifyPropertyChanged {
    public event PropertyChangedEventHandler PropertyChanged;
    // Raised when an editable field (visualizer / blend / opacity / mute) changes.
    public event Action Changed;
    public event Action<DomeLayerRowViewModel> RemoveRequested;
    public event Action<DomeLayerRowViewModel> MoveUpRequested;
    public event Action<DomeLayerRowViewModel> MoveDownRequested;
    // Manual trigger (docs/triggers.md): the controller bumps this layer's
    // domeLayerFireCounters entry rather than mutating Params, so firing
    // never interleaves with the whole-stack Publish() snapshot swap.
    public event Action<DomeLayerRowViewModel> FireRequested;
    // Manual clear, parallel to FireRequested: the controller bumps this layer's
    // domeLayerClearCounters entry so a layer can drop its live state.
    public event Action<DomeLayerRowViewModel> ClearRequested;

    public DomeLayerRowViewModel() {
      this.RemoveCommand = new RelayCommand(
        () => this.RemoveRequested?.Invoke(this));
      this.MoveUpCommand = new RelayCommand(
        () => this.MoveUpRequested?.Invoke(this));
      this.MoveDownCommand = new RelayCommand(
        () => this.MoveDownRequested?.Invoke(this));
      this.FireCommand = new RelayCommand(
        () => this.FireRequested?.Invoke(this));
      this.ClearCommand = new RelayCommand(
        () => this.ClearRequested?.Invoke(this));
    }

    // Shared, static option lists so every row's ComboBox binds the same source.
    private static readonly List<DomeLayerVisualizerOption> visualizerOptions =
      BuildVisualizerOptions();
    private static List<DomeLayerVisualizerOption> BuildVisualizerOptions() {
      var options = new List<DomeLayerVisualizerOption>();
      foreach (LayerDefinition definition in LayerCatalog.Default.Definitions) {
        options.Add(new DomeLayerVisualizerOption {
          Key = definition.Id,
          Label = definition.DisplayName,
        });
      }
      return options;
    }
    public IReadOnlyList<DomeLayerVisualizerOption> VisualizerOptions =>
      visualizerOptions;
    public IReadOnlyList<DomeLayerOperationOption> BlendModes => blendModes;
    private static readonly List<DomeLayerOperationOption> blendModes =
      DomeBlend.All.Select(b => new DomeLayerOperationOption {
        Id = b.Id,
        Label = b.DisplayName,
      }).ToList();

    // Generic per-layer param editors, rebuilt from the schema whenever the
    // visualizer key or blend mode changes: the visualizer-consumed set
    // (from LayerCatalog) followed by the compositor-consumed set of the
    // current blend. Each entry raises the row's Changed on edit.
    public ObservableCollection<LayerParamViewModel> Params { get; } =
      new ObservableCollection<LayerParamViewModel>();

    public ICommand RemoveCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand FireCommand { get; }
    public ICommand ClearCommand { get; }

    private void Set<T>(ref T field, T value, string name) {
      if (EqualityComparer<T>.Default.Equals(field, value)) {
        return;
      }
      field = value;
      this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
      this.Changed?.Invoke();
    }

    private string visualizerKey;
    public string InstanceId { get; set; } = LayerInstanceId.NewId().Value;
    public string VisualizerKey {
      get => this.visualizerKey;
      set {
        if (this.visualizerKey == value) {
          return;
        }
        this.visualizerKey = value;
        this.PropertyChanged?.Invoke(
          this, new PropertyChangedEventArgs(nameof(VisualizerKey)));
        this.PropertyChanged?.Invoke(
          this, new PropertyChangedEventArgs(nameof(FireLabel)));
        this.PropertyChanged?.Invoke(
          this, new PropertyChangedEventArgs(nameof(FireToolTip)));
        this.PropertyChanged?.Invoke(
          this, new PropertyChangedEventArgs(nameof(ClearLabel)));
        this.PropertyChanged?.Invoke(
          this, new PropertyChangedEventArgs(nameof(ClearToolTip)));
        // The visualizer's param schema changed: rebuild to defaults, dropping
        // keys not in the new schema.
        this.RebuildParams(null, this.CurrentParamValues(true));
        this.Changed?.Invoke();
      }
    }

    // Astronomy uses the existing per-instance fire command as its dedicated
    // playback start edge. The command transport stays generic; only the row's
    // user-facing action changes from Fire to Play.
    public string FireLabel =>
      this.visualizerKey == "astronomy" ? "Play" : "Fire";
    public string FireToolTip => this.visualizerKey == "astronomy"
      ? "Play the one-week astronomy timeline"
      : "Fire manual trigger";
    public string ClearLabel =>
      this.visualizerKey == "astronomy" ? "Stop" : "Clear";
    public string ClearToolTip => this.visualizerKey == "astronomy"
      ? "Stop astronomy playback at the current time"
      : "Clear this layer's live state";

    private string blendMode = DomeBlend.Default.Id;
    public string BlendMode {
      get => this.blendMode;
      set {
        if (this.blendMode == value) {
          return;
        }
        this.blendMode = value;
        this.PropertyChanged?.Invoke(
          this, new PropertyChangedEventArgs(nameof(BlendMode)));
        // The blend's compositor-consumed param schema changed: rebuild, but
        // seed from the current values so unrelated (visualizer) params
        // survive and only newly-introduced blend keys fall back to default.
        this.RebuildParams(
          this.CurrentParamValues(false), this.CurrentParamValues(true));
        this.Changed?.Invoke();
      }
    }

    // (Re)build the Params collection from the current visualizer + blend schema.
    // Each descriptor's value is taken from `seed` when present, else its
    // default; keys absent from the schema are dropped. `seed` is the saved
    // config bag when loading a row, or null to reset to defaults on a
    // key/blend change. Does not itself raise Changed — callers do (a schema
    // rebuild always accompanies a key/blend edit that already publishes, and
    // seeding happens before the row is wired up).
    private void RebuildParams(
      IDictionary<string, double> rendererSeed,
      IDictionary<string, double> operationSeed
    ) {
      foreach (LayerParamViewModel existing in this.Params) {
        existing.Changed -= this.OnParamChanged;
      }
      this.Params.Clear();
      AddParams(
        LayerCatalog.Default.ParametersFor(this.visualizerKey),
        rendererSeed, false);
      DomeBlend blend = DomeBlend.FromId(this.blendMode);
      if (blend != null) {
        AddParams(blend.Params, operationSeed, true);
      }
    }

    private void AddParams(
      IReadOnlyList<DomeLayerParam> schema, IDictionary<string, double> seed,
      bool isOperationParameter
    ) {
      foreach (DomeLayerParam descriptor in schema) {
        double value = DomeLayerDate.ResolveDefault(descriptor);
        if (seed != null && seed.TryGetValue(descriptor.Key, out double v)) {
          value = v;
        }
        var vm = new LayerParamViewModel(
          descriptor, value, isOperationParameter);
        vm.Changed += this.OnParamChanged;
        this.Params.Add(vm);
      }
    }

    private void OnParamChanged() {
      this.Changed?.Invoke();
    }

    // Snapshot of the current Params collection, used to seed a rebuild so
    // values already set by the user survive a schema change that keeps the
    // same keys.
    private IDictionary<string, double> CurrentParamValues(
      bool operationParameters
    ) {
      var values = new Dictionary<string, double>();
      foreach (LayerParamViewModel vm in this.Params) {
        if (vm.IsOperationParameter == operationParameters) {
          values[vm.Key] = vm.StoredValue;
        }
      }
      return values;
    }

    // Seed the param values from a saved config bag, keeping the current schema.
    // Called by the controller after constructing the row so loaded values
    // replace the defaults the setters installed.
    public void LoadParams(
      IDictionary<string, double> rendererParams,
      IDictionary<string, double> operationParams
    ) {
      this.RebuildParams(rendererParams, operationParams);
    }

    internal LayerParamViewModel FindParam(string key) {
      foreach (LayerParamViewModel param in this.Params) {
        if (!param.IsOperationParameter && param.Key == key) {
          return param;
        }
      }
      return null;
    }

    private double opacity = 1.0;
    public double Opacity {
      get => this.opacity;
      set => this.Set(ref this.opacity, value, nameof(Opacity));
    }

    // Whether this layer contributes (mute without removing).
    private bool layerEnabled = true;
    public bool LayerEnabled {
      get => this.layerEnabled;
      set => this.Set(ref this.layerEnabled, value, nameof(LayerEnabled));
    }

    // Free-text note the user leaves for themselves about this layer.
    private string notes;
    public string Notes {
      get => this.notes;
      set => this.Set(ref this.notes, value, nameof(Notes));
    }

    // Native-editor disclosure state. This is deliberately not persisted in
    // DomeLayerSettings and does not raise Changed: collapsing a card is only a
    // local UI preference, not a scene edit.
    private bool isExpanded = true;
    public bool IsExpanded {
      get => this.isExpanded;
      set {
        if (this.isExpanded == value) {
          return;
        }
        this.isExpanded = value;
        this.PropertyChanged?.Invoke(
          this, new PropertyChangedEventArgs(nameof(IsExpanded)));
      }
    }
  }

}
