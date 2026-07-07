using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;
using Spectrum.Base;

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
      for (int i = 0; i < DomeLayerSettings.LegacyVisKeys.Length; i++) {
        options.Add(new DomeLayerVisualizerOption {
          Key = DomeLayerSettings.LegacyVisKeys[i],
          Label = DomeLayerSettings.LegacyVisLabels[i],
        });
      }
      return options;
    }
    public IReadOnlyList<DomeLayerVisualizerOption> VisualizerOptions =>
      visualizerOptions;
    public IReadOnlyList<DomeBlendMode> BlendModes => blendModes;
    private static readonly DomeBlendMode[] blendModes = new DomeBlendMode[] {
      DomeBlendMode.Over, DomeBlendMode.Add, DomeBlendMode.Screen,
      DomeBlendMode.Lighten, DomeBlendMode.Multiply,
    };

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
      set => this.Set(ref this.visualizerKey, value, nameof(VisualizerKey));
    }

    private DomeBlendMode blendMode = DomeBlendMode.Add;
    public DomeBlendMode BlendMode {
      get => this.blendMode;
      set => this.Set(ref this.blendMode, value, nameof(BlendMode));
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
  }

}
