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
    private readonly FrameClock clock = new FrameClock();
    private RenderPlan plan = RenderPlan.Empty;
    private DomeFrame destination;
    private DomeFrame scratch;
    private double seconds;

    public DomeCompositor(
      Func<DomeFrame> createFrame,
      OrientationAngleProvider orientation = null,
      Func<double> elapsedSeconds = null
    ) {
      this.createFrame = createFrame ??
        throw new ArgumentNullException(nameof(createFrame));
      this.orientation = orientation;
      this.elapsedSeconds = elapsedSeconds ??
        (() => this.clock.Tick() / FrameClock.NominalFps);
    }

    public RenderPlan Plan => Volatile.Read(ref this.plan);

    public void Publish(RenderPlan next) =>
      Volatile.Write(ref this.plan, next ?? RenderPlan.Empty);

    // Returns null when no scheduled renderer contributed, preserving the old
    // "hold last frame / leave diagnostic output alone" contract.
    public DomeFrame Compose() {
      this.seconds += Math.Max(0, this.elapsedSeconds());
      RenderPlan current = this.Plan;
      bool hasAvailableLayer = false;
      for (int i = 0; i < current.Layers.Length; i++) {
        CompiledLayer layer = current.Layers[i];
        if (!layer.Renderer.IsAvailable) {
          continue;
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
        layer.Operation.Execute(new DomeBlendContext(
          this.destination, source, needsSnapshot ? this.scratch : null,
          layer.OperationOptions, layer.Snapshot.Opacity, this.seconds,
          this.orientation));
      }
      return hasAvailableLayer ? this.destination : null;
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
  }
}
