using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Spectrum.Base;

namespace Spectrum {

  // Drives a dome layers panel: binds an ItemsControl (one row per layer) and an
  // "Add layer" button to config.domeLayerStack, translating every UI mutation
  // into a whole-stack snapshot swap and rebuilding the rows when the stack
  // changes underneath us (a web PUT or the other window). Shared by
  // MainWindow and the VJ HUD.
  //
  // Row order is front-to-back: the top row is the front (last stack entry) and
  // the bottom row is the background (stack index 0), matching common layer-panel
  // conventions.
  public class DomeLayersController {
    private readonly Configuration config;
    private readonly Dispatcher dispatcher;
    public ObservableCollection<DomeLayerRowViewModel> Rows { get; } =
      new ObservableCollection<DomeLayerRowViewModel>();

    // True while RebuildRows is repopulating Rows from config, so the row-change
    // handlers don't publish back mid-rebuild.
    private bool rebuilding;
    // The exact list instance this controller last wrote to config, used to
    // ignore the PropertyChanged echo of our own write.
    private List<DomeLayerSettings> lastPublished;

    public DomeLayersController(
      Configuration config, ItemsControl itemsControl, ButtonBase addButton
    ) {
      this.config = config;
      this.dispatcher = itemsControl.Dispatcher;
      itemsControl.ItemsSource = this.Rows;
      addButton.Click += (s, e) => this.AddLayer();
      this.config.PropertyChanged += this.OnConfigChanged;
      this.RebuildRows();
    }

    private void OnConfigChanged(object sender, PropertyChangedEventArgs e) {
      if (e.PropertyName != "domeLayerStack") {
        return;
      }
      // The write may originate off the UI thread (the operator-thread alias);
      // marshal the rebuild onto the dispatcher. Ignore our own echo.
      this.dispatcher.BeginInvoke(new Action(() => {
        if (ReferenceEquals(this.config.domeLayerStack, this.lastPublished)) {
          return;
        }
        this.RebuildRows();
      }));
    }

    private void RebuildRows() {
      this.rebuilding = true;
      this.Rows.Clear();
      List<DomeLayerSettings> stack = this.config.domeLayerStack;
      if (stack != null) {
        // Stack index 0 = background => bottom row, so add in reverse.
        for (int i = stack.Count - 1; i >= 0; i--) {
          this.Rows.Add(this.MakeRow(stack[i]));
        }
      }
      this.rebuilding = false;
    }

    private DomeLayerRowViewModel MakeRow(DomeLayerSettings settings) {
      var vm = new DomeLayerRowViewModel {
        InstanceId = settings.InstanceId ?? LayerInstanceId.NewId().Value,
        VisualizerKey = settings.VisualizerKey,
        BlendMode = settings.BlendMode,
        Opacity = settings.Opacity,
        LayerEnabled = settings.Enabled,
        Notes = settings.Notes,
      };
      // Seed param values from the saved bag (the setters above installed schema
      // defaults); do this before wiring Changed so it doesn't republish.
      vm.LoadParams(settings.Params);
      vm.Changed += this.Publish;
      vm.RemoveRequested += this.RemoveRow;
      vm.MoveUpRequested += this.MoveRowUp;
      vm.MoveDownRequested += this.MoveRowDown;
      vm.FireRequested += this.FireRow;
      vm.ClearRequested += this.ClearRow;
      return vm;
    }

    private void AddLayer() {
      var vm = new DomeLayerRowViewModel {
        VisualizerKey = DomeLayerSettings.LegacyVisKeys[0],
        BlendMode = DomeBlend.Default.Name,
        Opacity = 1.0,
        LayerEnabled = true,
      };
      vm.Changed += this.Publish;
      vm.RemoveRequested += this.RemoveRow;
      vm.MoveUpRequested += this.MoveRowUp;
      vm.MoveDownRequested += this.MoveRowDown;
      vm.FireRequested += this.FireRow;
      vm.ClearRequested += this.ClearRow;
      // New layer goes on the bottom (background) = the last row, since rows
      // run front-to-back (Rows[0] is the front, the last row is stack index 0).
      this.Rows.Add(vm);
      this.Publish();
    }

    private void RemoveRow(DomeLayerRowViewModel row) {
      if (this.Rows.Remove(row)) {
        this.Publish();
      }
    }

    private void MoveRowUp(DomeLayerRowViewModel row) {
      int i = this.Rows.IndexOf(row);
      if (i > 0) {
        this.Rows.Move(i, i - 1);
        this.Publish();
      }
    }

    private void MoveRowDown(DomeLayerRowViewModel row) {
      int i = this.Rows.IndexOf(row);
      if (i >= 0 && i < this.Rows.Count - 1) {
        this.Rows.Move(i, i + 1);
        this.Publish();
      }
    }

    // Bump this row's manual-fire counter. A whole-dictionary copy-and-swap
    // (like Publish's whole-stack swap), keyed by the row's own layer key
    // rather than routed through Params/Publish — firing is not a stack edit.
    private void FireRow(DomeLayerRowViewModel row) {
      string instanceId = row.InstanceId;
      if (instanceId == null) {
        return;
      }
      var counters = new Dictionary<string, int>(
        this.config.domeLayerFireCounters ?? new Dictionary<string, int>());
      counters.TryGetValue(instanceId, out int count);
      counters[instanceId] = count + 1;
      this.config.domeLayerFireCounters = counters;
    }

    // Bump this row's manual-clear counter, exactly like FireRow. A layer that
    // holds accumulated live state (Shooting Star) edge-detects the bump and
    // drops it; layers with no such state ignore it (harmless no-op).
    private void ClearRow(DomeLayerRowViewModel row) {
      string instanceId = row.InstanceId;
      if (instanceId == null) {
        return;
      }
      var counters = new Dictionary<string, int>(
        this.config.domeLayerClearCounters ?? new Dictionary<string, int>());
      counters.TryGetValue(instanceId, out int count);
      counters[instanceId] = count + 1;
      this.config.domeLayerClearCounters = counters;
    }

    // Rebuild config.domeLayerStack from the current rows (bottom row = index 0)
    // and swap it in as a fresh immutable list.
    private void Publish() {
      if (this.rebuilding) {
        return;
      }
      var stack = new List<DomeLayerSettings>();
      for (int i = this.Rows.Count - 1; i >= 0; i--) {
        DomeLayerRowViewModel vm = this.Rows[i];
        // Snapshot the row's param VMs into a fresh bag; null when the layer has
        // no params (an absent bag reads as all-defaults everywhere).
        Dictionary<string, double> paramBag = null;
        if (vm.Params.Count > 0) {
          paramBag = new Dictionary<string, double>();
          foreach (LayerParamViewModel p in vm.Params) {
            paramBag[p.Key] = p.Value;
          }
        }
        stack.Add(new DomeLayerSettings {
          InstanceId = vm.InstanceId,
          VisualizerKey = vm.VisualizerKey,
          BlendMode = vm.BlendMode,
          Opacity = vm.Opacity,
          Enabled = vm.LayerEnabled,
          Notes = vm.Notes,
          Params = paramBag,
        });
      }
      this.lastPublished = stack;
      this.config.domeLayerStack = stack;
    }
  }

}
