using System;
using System.Collections.Generic;
using System.Linq;
using Spectrum.Base;

namespace Spectrum.LEDs {

  // Owns the output's visualizer registry and the complete compositor phase:
  // compose one accepted plan, publish the completed frame, then mutate
  // persistent renderer trails for the next frame. Transport and frame-capture
  // lifecycles remain with LEDDomeOutput.
  internal sealed class DomeRenderPipeline {
    private readonly List<Visualizer> visualizers = new();
    private readonly DomeCompositor compositor;
    private readonly Action<DomeFrame> publishFrame;
    private readonly Func<double> beatProgress;
    private readonly Func<double>? hueFrameScale;
    private readonly FrameClock hueClock = new();
    private Visualizer[]? visualizerSnapshot;

    internal DomeRenderPipeline(
      Func<DomeFrame> createFrame,
      Action<DomeFrame> publishFrame,
      OrientationAngleProvider? orientation,
      Func<int, double, int>? paletteColor,
      Func<double> beatProgress,
      Func<double>? hueFrameScale = null
    ) {
      this.publishFrame = publishFrame ??
        throw new ArgumentNullException(nameof(publishFrame));
      this.beatProgress = beatProgress ??
        throw new ArgumentNullException(nameof(beatProgress));
      this.hueFrameScale = hueFrameScale;
      this.compositor = new DomeCompositor(
        createFrame ?? throw new ArgumentNullException(nameof(createFrame)),
        orientation,
        paletteColor: paletteColor);
    }

    internal void RegisterVisualizer(Visualizer visualizer) {
      this.visualizers.Add(visualizer);
      this.visualizerSnapshot = null;
    }

    internal void UnregisterVisualizer(Visualizer visualizer) {
      if (this.visualizers.Remove(visualizer)) {
        this.visualizerSnapshot = null;
      }
    }

    internal Visualizer[] GetVisualizers() =>
      this.visualizerSnapshot ??=
        this.visualizers.ToArray();

    internal void Publish(RenderPlan plan) {
      this.compositor.Publish(plan);
    }

    internal void Render(DomeRenderGeneration generation) {
      DomeFrame? completed = this.compositor.Compose(generation.Plan);
      if (completed == null) {
        return;
      }

      // Publish before advancing persistent layer buffers. Pixels painted this
      // frame reach the wire at their drawn hue and begin rotating next frame.
      this.publishFrame(completed);
      this.ApplyGlobalHueRotation(generation);
    }

    private void ApplyGlobalHueRotation(DomeRenderGeneration generation) {
      // Tick every completed frame, even when rotation is disabled, so
      // re-enabling the effect cannot accumulate a wall-clock jump.
      double frameScale =
        this.hueFrameScale?.Invoke() ?? this.hueClock.Tick();
      double hueSpeed = generation.ShowState.GlobalHueSpeed;
      if (hueSpeed <= 0) {
        return;
      }

      double rate = Math.Pow(10, -hueSpeed);
      double progress = this.beatProgress();
      double modulation =
        3 * progress * progress - 3 * progress + 1;
      this.compositor.AdvancePostFrameHue(
        generation.Plan,
        rate * modulation * frameScale);
    }
  }
}
