using System;
using System.Threading;
using Spectrum.Base;

namespace Spectrum.LEDs {

  // Owns the accepted render generation and the operator-thread frame capture.
  // Publication can happen concurrently, but a frame retains one generation
  // and one pair of runtime/output snapshots until EndFrame.
  internal sealed class DomeRenderState {
    private readonly Func<DomeRuntimeFrameSnapshot> runtimeSource;
    private readonly Func<DomeOutputSettingsSnapshot> outputSource;
    private readonly DomePaletteSampler paletteSampler;
    private DomeRenderGeneration generation;
    private DomeRenderGeneration? frameGeneration;
    private DomeRuntimeFrameSnapshot frameRuntime =
      DomeRuntimeFrameSnapshot.Empty;
    private DomeOutputSettingsSnapshot frameOutput =
      DomeOutputSettingsSnapshot.Empty;
    private bool frameActive;

    public DomeRenderState(
      BeatBroadcaster beat,
      DomeRenderGeneration initialGeneration,
      Func<DomeRuntimeFrameSnapshot> runtimeSource,
      Func<DomeOutputSettingsSnapshot> outputSource
    ) {
      this.generation = initialGeneration ??
        throw new ArgumentNullException(nameof(initialGeneration));
      this.runtimeSource = runtimeSource ??
        throw new ArgumentNullException(nameof(runtimeSource));
      this.outputSource = outputSource ??
        throw new ArgumentNullException(nameof(outputSource));
      this.paletteSampler = new DomePaletteSampler(
        beat ?? throw new ArgumentNullException(nameof(beat)),
        () => this.CurrentGeneration,
        this.runtimeSource);
    }

    public DomePaletteSampler PaletteSampler => this.paletteSampler;

    public DomeRenderGeneration CurrentGeneration =>
      Volatile.Read(ref this.generation) ?? DomeRenderGeneration.Empty;

    public DomeRenderGeneration FrameGeneration =>
      this.frameGeneration ?? this.CurrentGeneration;

    public RenderPlan Plan => this.CurrentGeneration.Plan;

    public DomeShowStateSnapshot ShowState =>
      this.CurrentGeneration.ShowState;

    public DomeRuntimeFrameSnapshot RuntimeSettings =>
      this.frameActive ? this.frameRuntime : this.runtimeSource();

    public DomeOutputSettingsSnapshot OutputSettings =>
      this.frameActive ? this.frameOutput : this.outputSource();

    public void Publish(DomeRenderGeneration generation) {
      if (generation == null) {
        throw new ArgumentNullException(nameof(generation));
      }
      Volatile.Write(ref this.generation, generation);
    }

    public void PublishShowState(DomeShowStateSnapshot showState) {
      if (showState == null) {
        throw new ArgumentNullException(nameof(showState));
      }
      while (true) {
        DomeRenderGeneration current = this.CurrentGeneration;
        var updated = new DomeRenderGeneration(current.Plan, showState);
        if (ReferenceEquals(
            Interlocked.CompareExchange(
              ref this.generation, updated, current), current)) {
          return;
        }
      }
    }

    public DomeShowStateSnapshot BeginFrame(
      DomeRuntimeFrameSnapshot? runtimeSettings = null
    ) {
      DomeRenderGeneration current = this.CurrentGeneration;
      this.frameGeneration = current;
      this.frameRuntime = runtimeSettings ?? this.runtimeSource();
      this.frameOutput = this.outputSource();
      this.frameActive = true;
      this.paletteSampler.BeginFrame(current, this.frameRuntime);
      return current.ShowState;
    }

    public void EndFrame() {
      this.paletteSampler.EndFrame();
      this.frameGeneration = null;
      this.frameActive = false;
    }
  }
}
