using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Spectrum.Base;
using Spectrum.Visualizers;

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
    // UI-only disclosure state, retained when a web edit or scene load rebuilds
    // the row view models. Stable layer IDs make the preference reorder-safe.
    private readonly Dictionary<string, bool> expandedByInstanceId = new();
    private readonly Dictionary<string, AstronomyPlaybackDisplay>
      astronomyPlaybackByInstanceId = new();
    private readonly Dictionary<string, AstronomyStoppedDisplay>
      astronomyStoppedByInstanceId = new();
    private Dictionary<string, int> observedFireGenerations;
    private Dictionary<string, int> observedClearGenerations;
    private readonly DispatcherTimer astronomyPlaybackTimer;

    private sealed class AstronomyPlaybackDisplay {
      public double StartOffset;
      public double ConfiguredTimeOffset;
      public double Speed;
      public long StartedAt;
    }

    private sealed class AstronomyStoppedDisplay {
      public double Offset;
      public double ConfiguredTimeOffset;
    }

    public DomeLayersController(
      Configuration config, ItemsControl itemsControl, ButtonBase addButton,
      ButtonBase collapseAllButton, ButtonBase expandAllButton
    ) {
      this.config = config;
      this.dispatcher = itemsControl.Dispatcher;
      this.observedFireGenerations = new Dictionary<string, int>(
        this.config.domeLayerFireCounters ?? new Dictionary<string, int>());
      this.observedClearGenerations = new Dictionary<string, int>(
        this.config.domeLayerClearCounters ?? new Dictionary<string, int>());
      this.astronomyPlaybackTimer = new DispatcherTimer {
        Interval = TimeSpan.FromMilliseconds(100),
      };
      this.astronomyPlaybackTimer.Tick += this.AdvanceAstronomyPlayback;
      itemsControl.ItemsSource = this.Rows;
      addButton.Click += (s, e) => this.AddLayer();
      collapseAllButton.Click += (s, e) => this.SetAllExpanded(false);
      expandAllButton.Click += (s, e) => this.SetAllExpanded(true);
      this.config.PropertyChanged += this.OnConfigChanged;
      this.RebuildRows();
    }

    private void SetAllExpanded(bool expanded) {
      foreach (DomeLayerRowViewModel row in this.Rows) {
        row.IsExpanded = expanded;
        if (!string.IsNullOrWhiteSpace(row.InstanceId)) {
          this.expandedByInstanceId[row.InstanceId] = expanded;
        }
      }
    }

    private void OnConfigChanged(object sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == "domeLayerFireCounters") {
        this.dispatcher.BeginInvoke(
          new Action(this.StartNewAstronomyPlaybackDisplays));
        return;
      }
      if (e.PropertyName == "domeLayerClearCounters") {
        this.dispatcher.BeginInvoke(
          new Action(this.StopNewAstronomyPlaybackDisplays));
        return;
      }
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
      foreach (DomeLayerRowViewModel row in this.Rows) {
        if (!string.IsNullOrWhiteSpace(row.InstanceId)) {
          this.expandedByInstanceId[row.InstanceId] = row.IsExpanded;
        }
      }
      this.Rows.Clear();
      List<DomeLayerSettings> stack = this.config.domeLayerStack;
      if (stack != null) {
        // Stack index 0 = background => bottom row, so add in reverse.
        for (int i = stack.Count - 1; i >= 0; i--) {
          this.Rows.Add(this.MakeRow(stack[i]));
        }
      }
      this.rebuilding = false;
      this.RestoreStoppedAstronomyDisplays();
      if (this.astronomyPlaybackByInstanceId.Count > 0) {
        this.AdvanceAstronomyPlayback(null, EventArgs.Empty);
      }
    }

    private DomeLayerRowViewModel MakeRow(DomeLayerSettings settings) {
      string instanceId = settings.InstanceId ?? LayerInstanceId.NewId().Value;
      var vm = new DomeLayerRowViewModel {
        InstanceId = instanceId,
        VisualizerKey = settings.VisualizerKey,
        BlendMode = settings.BlendMode,
        Opacity = settings.Opacity,
        LayerEnabled = settings.Enabled,
        Notes = settings.Notes,
        IsExpanded = !this.expandedByInstanceId.TryGetValue(
          instanceId, out bool isExpanded) || isExpanded,
      };
      // Seed param values from the saved bag (the setters above installed schema
      // defaults); do this before wiring Changed so it doesn't republish.
      vm.LoadParams(settings.RendererParams, settings.OperationParams);
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
        VisualizerKey = LayerCatalog.Default.Definitions[0].Id,
        BlendMode = DomeBlend.Default.Id,
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

    // A fire-generation change may originate from either native window or the
    // web console. Every open native layer panel mirrors an Astronomy Play edge
    // into its own UI without persisting each timer tick.
    private void StartNewAstronomyPlaybackDisplays() {
      Dictionary<string, int> current = new Dictionary<string, int>(
        this.config.domeLayerFireCounters ?? new Dictionary<string, int>());
      foreach (DomeLayerRowViewModel row in this.Rows) {
        if (row.VisualizerKey != "astronomy" || row.InstanceId == null) {
          continue;
        }
        current.TryGetValue(row.InstanceId, out int generation);
        this.observedFireGenerations.TryGetValue(
          row.InstanceId, out int observedGeneration);
        if (generation > observedGeneration) {
          this.StartAstronomyPlaybackDisplay(row);
        }
      }
      this.observedFireGenerations = current;
    }

    private void StopNewAstronomyPlaybackDisplays() {
      Dictionary<string, int> current = new Dictionary<string, int>(
        this.config.domeLayerClearCounters ?? new Dictionary<string, int>());
      foreach (DomeLayerRowViewModel row in this.Rows) {
        if (row.VisualizerKey != "astronomy" || row.InstanceId == null) {
          continue;
        }
        current.TryGetValue(row.InstanceId, out int generation);
        this.observedClearGenerations.TryGetValue(
          row.InstanceId, out int observedGeneration);
        if (generation > observedGeneration) {
          this.StopAstronomyPlaybackDisplay(row.InstanceId);
        }
      }
      this.observedClearGenerations = current;
    }

    private void StartAstronomyPlaybackDisplay(
      DomeLayerRowViewModel row
    ) {
      LayerParamViewModel time = row.FindParam("timeOffsetHours");
      LayerParamViewModel speed = row.FindParam("playbackSpeed");
      if (time == null || speed == null) {
        return;
      }
      double startOffset = time.StoredValue;
      if (this.astronomyStoppedByInstanceId.TryGetValue(
            row.InstanceId, out AstronomyStoppedDisplay stopped) &&
          Math.Abs(
            stopped.ConfiguredTimeOffset - time.StoredValue) <= 1e-6) {
        startOffset = stopped.Offset;
      }
      this.astronomyStoppedByInstanceId.Remove(row.InstanceId);
      this.astronomyPlaybackByInstanceId[row.InstanceId] =
        new AstronomyPlaybackDisplay {
          StartOffset = startOffset,
          ConfiguredTimeOffset = time.StoredValue,
          Speed = speed.Value,
          StartedAt = Stopwatch.GetTimestamp(),
        };
      time.SetDisplayedValue(startOffset);
      if (!this.astronomyPlaybackTimer.IsEnabled) {
        this.astronomyPlaybackTimer.Start();
      }
    }

    private void AdvanceAstronomyPlayback(object sender, EventArgs e) {
      long now = Stopwatch.GetTimestamp();
      var completedIds = new List<string>();
      foreach (
        KeyValuePair<string, AstronomyPlaybackDisplay> item
        in this.astronomyPlaybackByInstanceId
      ) {
        DomeLayerRowViewModel row = this.FindRow(item.Key);
        LayerParamViewModel time = row?.FindParam("timeOffsetHours");
        LayerParamViewModel speed = row?.FindParam("playbackSpeed");
        LayerParamViewModel loop = row?.FindParam("loop");
        if (row?.VisualizerKey != "astronomy" || time == null ||
            speed == null || loop == null) {
          completedIds.Add(item.Key);
          continue;
        }

        AstronomyPlaybackDisplay playback = item.Value;
        bool timeWasEdited = Math.Abs(
          time.StoredValue - playback.ConfiguredTimeOffset) > 1e-6;
        if (timeWasEdited) {
          playback.StartOffset = time.StoredValue;
          playback.ConfiguredTimeOffset = time.StoredValue;
          playback.StartedAt = now;
        } else if (speed.Value != playback.Speed) {
          double oldElapsed = ElapsedSeconds(playback.StartedAt, now);
          playback.StartOffset =
            LEDDomeAstronomyVisualizer.PlaybackOffset(
              playback.StartOffset, oldElapsed, playback.Speed,
              loop.BoolValue, out bool completedAtOldSpeed);
          playback.StartedAt = now;
          if (completedAtOldSpeed) {
            time.SetDisplayedValue(playback.StartOffset);
            completedIds.Add(item.Key);
            continue;
          }
        }
        playback.Speed = speed.Value;

        double offset = LEDDomeAstronomyVisualizer.PlaybackOffset(
          playback.StartOffset,
          ElapsedSeconds(playback.StartedAt, now),
          playback.Speed,
          loop.BoolValue,
          out bool completed);
        time.SetDisplayedValue(offset);
        if (completed) {
          completedIds.Add(item.Key);
        }
      }

      foreach (string instanceId in completedIds) {
        this.astronomyPlaybackByInstanceId.Remove(instanceId);
      }
      if (this.astronomyPlaybackByInstanceId.Count == 0) {
        this.astronomyPlaybackTimer.Stop();
      }
    }

    private void StopAstronomyPlaybackDisplay(string instanceId) {
      if (!this.astronomyPlaybackByInstanceId.ContainsKey(instanceId)) {
        return;
      }
      this.AdvanceAstronomyPlayback(null, EventArgs.Empty);
      DomeLayerRowViewModel row = this.FindRow(instanceId);
      LayerParamViewModel time = row?.FindParam("timeOffsetHours");
      if (this.astronomyPlaybackByInstanceId.ContainsKey(instanceId) &&
          time != null) {
        this.astronomyStoppedByInstanceId[instanceId] =
          new AstronomyStoppedDisplay {
            Offset = time.Value,
            ConfiguredTimeOffset = time.StoredValue,
          };
      }
      this.astronomyPlaybackByInstanceId.Remove(instanceId);
      if (this.astronomyPlaybackByInstanceId.Count == 0) {
        this.astronomyPlaybackTimer.Stop();
      }
    }

    private void RestoreStoppedAstronomyDisplays() {
      var invalidIds = new List<string>();
      foreach (
        KeyValuePair<string, AstronomyStoppedDisplay> item
        in this.astronomyStoppedByInstanceId
      ) {
        DomeLayerRowViewModel row = this.FindRow(item.Key);
        LayerParamViewModel time = row?.FindParam("timeOffsetHours");
        if (row?.VisualizerKey != "astronomy" || time == null ||
            Math.Abs(
              time.StoredValue - item.Value.ConfiguredTimeOffset) > 1e-6) {
          invalidIds.Add(item.Key);
          continue;
        }
        time.SetDisplayedValue(item.Value.Offset);
      }
      foreach (string instanceId in invalidIds) {
        this.astronomyStoppedByInstanceId.Remove(instanceId);
      }
    }

    private DomeLayerRowViewModel FindRow(string instanceId) {
      foreach (DomeLayerRowViewModel row in this.Rows) {
        if (row.InstanceId == instanceId) {
          return row;
        }
      }
      return null;
    }

    private static double ElapsedSeconds(long startedAt, long now) =>
      (now - startedAt) / (double)Stopwatch.Frequency;

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
        Dictionary<string, double> rendererParams = null;
        Dictionary<string, double> operationParams = null;
        if (vm.Params.Count > 0) {
          foreach (LayerParamViewModel p in vm.Params) {
            Dictionary<string, double> target;
            if (p.IsOperationParameter) {
              target = operationParams ??= new Dictionary<string, double>();
            } else {
              target = rendererParams ??= new Dictionary<string, double>();
            }
            target[p.Key] = p.StoredValue;
          }
        }
        stack.Add(new DomeLayerSettings {
          InstanceId = vm.InstanceId,
          VisualizerKey = vm.VisualizerKey,
          BlendMode = vm.BlendMode,
          Opacity = vm.Opacity,
          Enabled = vm.LayerEnabled,
          Notes = vm.Notes,
          RendererParams = rendererParams,
          OperationParams = operationParams,
        });
      }
      this.lastPublished = stack;
      this.config.domeLayerStack = stack;
    }
  }

}
