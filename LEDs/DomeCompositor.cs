using System;
using System.Collections.Generic;
using System.Threading;
using Spectrum.Base;

namespace Spectrum.LEDs {

  // Hardware-independent execution of a compiled render plan. It owns only
  // reusable render frames, pass ordering, and effect time; OPC, mapping,
  // configuration, palettes, and simulator publication remain in the output.
  public sealed class DomeCompositor {
    private readonly Func<DomeFrame> createFrame;
    private readonly OrientationAngleProvider orientation;
    private readonly Func<double> elapsedSeconds;
    private readonly Func<int, double, int> paletteColor;
    private readonly FrameClock clock = new FrameClock();
    private readonly Dictionary<CompositeHistoryKey, CompositeFrameHistory>
      histories = new();
    private readonly HashSet<CompositeHistoryKey> activeHistories = new();
    private readonly List<CompositeHistoryKey> staleHistories = new();
    private RenderPlan plan = RenderPlan.Empty;
    private DomeFrame destination;
    private DomeFrame scratch;
    private double seconds;

    public DomeCompositor(
      Func<DomeFrame> createFrame,
      OrientationAngleProvider orientation = null,
      Func<double> elapsedSeconds = null,
      Func<int, double, int> paletteColor = null
    ) {
      this.createFrame = createFrame ??
        throw new ArgumentNullException(nameof(createFrame));
      this.orientation = orientation;
      this.elapsedSeconds = elapsedSeconds ??
        (() => this.clock.Tick() / FrameClock.NominalFps);
      this.paletteColor = paletteColor;
    }

    public RenderPlan Plan => Volatile.Read(ref this.plan);

    internal int HistoryStateCount => this.histories.Count;
    internal int RetainedHistoryFrameCount {
      get {
        int count = 0;
        foreach (CompositeFrameHistory history in this.histories.Values) {
          count += history.Count;
        }
        return count;
      }
    }

    public void Publish(RenderPlan next) =>
      Volatile.Write(ref this.plan, next ?? RenderPlan.Empty);

    // Returns null when no scheduled renderer contributed, preserving the old
    // "hold last frame / leave diagnostic output alone" contract.
    public DomeFrame Compose() {
      this.seconds += Math.Max(0, this.elapsedSeconds());
      RenderPlan current = this.Plan;
      bool hasAvailableLayer = false;
      bool orientationUpdated = false;
      this.activeHistories.Clear();
      for (int i = 0; i < current.Layers.Length; i++) {
        CompiledLayer layer = current.Layers[i];
        if (!layer.Renderer.IsAvailable) {
          continue;
        }
        if (!orientationUpdated && this.orientation != null &&
            (layer.Operation.Requirements &
              CompositeRequirements.ReadsOrientation) != 0) {
          this.orientation.Update();
          orientationUpdated = true;
        }
        if (!hasAvailableLayer) {
          this.destination ??= this.createFrame();
          this.destination.ResetComposite();
          hasAvailableLayer = true;
        }
        DomeFrame source = layer.Renderer.Frame;
        bool needsSnapshot = (layer.Operation.Requirements &
          CompositeRequirements.ReadsDestinationNeighbors) != 0;
        if (needsSnapshot) {
          this.scratch ??= this.createFrame();
          this.scratch.CopyFrom(this.destination);
        }
        CompositeFrameHistory history = null;
        if ((layer.Operation.Requirements &
            CompositeRequirements.ReadsHistory) != 0) {
          var key = new CompositeHistoryKey(
            layer.Snapshot.Id, layer.Operation.Id);
          this.activeHistories.Add(key);
          if (!this.histories.TryGetValue(key, out history)) {
            history = new CompositeFrameHistory();
            this.histories.Add(key, history);
          }
        }
        layer.Operation.Execute(new DomeBlendContext(
          this.destination, source, needsSnapshot ? this.scratch : null,
          layer.OperationOptions, layer.Snapshot.Opacity, this.seconds,
          this.orientation, history, this.paletteColor));
      }
      this.RemoveStaleHistories();
      return hasAvailableLayer ? this.destination : null;
    }

    private void RemoveStaleHistories() {
      if (this.histories.Count == this.activeHistories.Count) {
        return;
      }
      this.staleHistories.Clear();
      foreach (CompositeHistoryKey key in this.histories.Keys) {
        if (!this.activeHistories.Contains(key)) {
          this.staleHistories.Add(key);
        }
      }
      foreach (CompositeHistoryKey key in this.staleHistories) {
        this.histories.Remove(key);
      }
    }

    // Explicit phase 5: mutate persistent renderer trails only after the
    // completed destination has been written. Shared renderer frames rotate
    // at most once.
    public void AdvancePostFrameHue(double delta) {
      if (delta == 0) {
        return;
      }
      var seen = new HashSet<ILayerRenderer>();
      RenderPlan current = this.Plan;
      foreach (CompiledLayer layer in current.Layers) {
        if (layer.Renderer.IsAvailable && seen.Add(layer.Renderer)) {
          layer.Renderer.Frame.HueRotate(delta);
        }
      }
    }

    private readonly record struct CompositeHistoryKey(
      LayerInstanceId LayerId, string OperationId);
  }
}
