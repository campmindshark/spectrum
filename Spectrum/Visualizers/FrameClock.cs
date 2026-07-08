using System;
using System.Diagnostics;

namespace Spectrum.Visualizers {

  // Wall-clock frame timing shared by the orientation- and twinkle-driven dome
  // layers. Owns the stopwatch and the nominal-frame constants that used to be
  // copy-pasted into each of those visualizers (Metaball, Ripple, Stamp,
  // Quaternion Paintbrush, Twinkle).
  //
  // Every per-frame state advance in those layers is multiplied by the value
  // Tick() returns — frameScale = (real elapsed seconds) / NOMINAL_FRAME_SECONDS
  // — so animation evolves at a consistent wall-clock speed regardless of how
  // fast the Operator loop happens to tick. The tuning constants/sliders were
  // dialed in at NOMINAL_FPS, so frameScale == 1 there reproduces the original
  // behavior exactly. MAX_FRAME_SCALE caps the catch-up after a stall (GC pause,
  // or the very first frame) so one long gap can't jolt the animation forward.
  class FrameClock {
    private const double NOMINAL_FPS = 120;
    private const double NOMINAL_FRAME_SECONDS = 1 / NOMINAL_FPS;
    private const double MAX_FRAME_SCALE = 5;
    private readonly Stopwatch timer = new Stopwatch();

    // Real time since the previous Tick, in nominal-frame units, clamped so a
    // long stall (GC pause, first frame) can't fast-forward animations.
    public double Tick() {
      if (!this.timer.IsRunning) {
        this.timer.Restart();
        return 1;
      }
      double elapsedSeconds = this.timer.Elapsed.TotalSeconds;
      this.timer.Restart();
      return Math.Clamp(elapsedSeconds / NOMINAL_FRAME_SECONDS, 0, MAX_FRAME_SCALE);
    }
  }
}
