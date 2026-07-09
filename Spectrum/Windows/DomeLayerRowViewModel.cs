using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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

  // View-model wrapping one DomeLayerParam descriptor plus its current value for
  // the generic per-layer param editors. The row DataTemplate binds the control
  // matching Type (Slider / CheckBox / ComboBox / ColorPicker) via the
  // IsDouble/IsBool/IsEnum/IsColor flags; editing any of the value facets raises
  // Changed so the owning row republishes. Values are always stored as double
  // (Bool 0/1, Enum index, Color a packed 0xRRGGBB int).
  public class LayerParamViewModel : INotifyPropertyChanged {
    public event PropertyChangedEventHandler PropertyChanged;
    public event Action Changed;

    private readonly DomeLayerParam descriptor;

    public LayerParamViewModel(DomeLayerParam descriptor, double value) {
      this.descriptor = descriptor;
      this.value = value;
    }

    public string Key => this.descriptor.Key;
    public string Label => this.descriptor.Label;
    public double Min => this.descriptor.Min;
    public double Max => this.descriptor.Max;
    public double Step => this.descriptor.Step;
    public IReadOnlyList<string> Options => this.descriptor.Options;

    public bool IsDouble => this.descriptor.Type == DomeLayerParamType.Double;
    public bool IsBool => this.descriptor.Type == DomeLayerParamType.Bool;
    public bool IsEnum => this.descriptor.Type == DomeLayerParamType.Enum;
    public bool IsColor => this.descriptor.Type == DomeLayerParamType.Color;

    // The canonical stored value. The typed facets below are views onto it.
    private double value;
    public double Value {
      get => this.value;
      set {
        if (this.value == value) {
          return;
        }
        this.value = value;
        this.Raise(nameof(Value));
        this.Raise(nameof(BoolValue));
        this.Raise(nameof(IntValue));
        this.Raise(nameof(ColorValue));
        this.Changed?.Invoke();
      }
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

    public DomeLayerRowViewModel() {
      this.RemoveCommand = new RelayCommand(
        () => this.RemoveRequested?.Invoke(this));
      this.MoveUpCommand = new RelayCommand(
        () => this.MoveUpRequested?.Invoke(this));
      this.MoveDownCommand = new RelayCommand(
        () => this.MoveDownRequested?.Invoke(this));
    }

    // Shared, static option lists so every row's ComboBox binds the same source.
    private static readonly List<DomeLayerVisualizerOption> visualizerOptions =
      BuildVisualizerOptions();
    private static List<DomeLayerVisualizerOption> BuildVisualizerOptions() {
      var options = new List<DomeLayerVisualizerOption>();
      for (int i = 0; i < DomeLayerSettings.LayerKeys.Length; i++) {
        options.Add(new DomeLayerVisualizerOption {
          Key = DomeLayerSettings.LayerKeys[i],
          Label = DomeLayerSettings.LayerLabels[i],
        });
      }
      return options;
    }
    public IReadOnlyList<DomeLayerVisualizerOption> VisualizerOptions =>
      visualizerOptions;
    public IReadOnlyList<DomeBlendMode> BlendModes => blendModes;
    private static readonly DomeBlendMode[] blendModes = new DomeBlendMode[] {
      DomeBlendMode.Over, DomeBlendMode.Add, DomeBlendMode.Screen,
      DomeBlendMode.Lighten, DomeBlendMode.Multiply, DomeBlendMode.Desaturate,
      DomeBlendMode.Hue,
    };

    // Generic per-layer param editors, rebuilt from the schema whenever the
    // visualizer key or blend mode changes: the visualizer-consumed set
    // (ParamsFor) followed by the compositor-consumed set of the current blend
    // (ParamsForBlend). Each entry raises the row's Changed on edit.
    public ObservableCollection<LayerParamViewModel> Params { get; } =
      new ObservableCollection<LayerParamViewModel>();

    public ICommand RemoveCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    private void Set<T>(ref T field, T value, string name) {
      if (EqualityComparer<T>.Default.Equals(field, value)) {
        return;
      }
      field = value;
      this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
      this.Changed?.Invoke();
    }

    private string visualizerKey;
    public string VisualizerKey {
      get => this.visualizerKey;
      set {
        if (this.visualizerKey == value) {
          return;
        }
        this.visualizerKey = value;
        this.PropertyChanged?.Invoke(
          this, new PropertyChangedEventArgs(nameof(VisualizerKey)));
        // The visualizer's param schema changed: rebuild to defaults, dropping
        // keys not in the new schema.
        this.RebuildParams(null);
        this.Changed?.Invoke();
      }
    }

    private DomeBlendMode blendMode = DomeBlendMode.Add;
    public DomeBlendMode BlendMode {
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
        this.RebuildParams(this.CurrentParamValues());
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
    private void RebuildParams(IDictionary<string, double> seed) {
      foreach (LayerParamViewModel existing in this.Params) {
        existing.Changed -= this.OnParamChanged;
      }
      this.Params.Clear();
      AddParams(DomeLayerSettings.ParamsFor(this.visualizerKey), seed);
      AddParams(DomeLayerSettings.ParamsForBlend(this.blendMode), seed);
    }

    private void AddParams(
      IReadOnlyList<DomeLayerParam> schema, IDictionary<string, double> seed
    ) {
      foreach (DomeLayerParam descriptor in schema) {
        double value = descriptor.Default;
        if (seed != null && seed.TryGetValue(descriptor.Key, out double v)) {
          value = v;
        }
        var vm = new LayerParamViewModel(descriptor, value);
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
    private IDictionary<string, double> CurrentParamValues() {
      var values = new Dictionary<string, double>();
      foreach (LayerParamViewModel vm in this.Params) {
        values[vm.Key] = vm.Value;
      }
      return values;
    }

    // Seed the param values from a saved config bag, keeping the current schema.
    // Called by the controller after constructing the row so loaded values
    // replace the defaults the setters installed.
    public void LoadParams(IDictionary<string, double> saved) {
      this.RebuildParams(saved);
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
  }

}
