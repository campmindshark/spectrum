using System;
using System.Collections.Generic;

namespace Spectrum.Base {

  // Bounded RGB-only snapshots of the composite below one stateful operation.
  // Geometry stays in the shared topology and adjustment blends never consume
  // historical coverage or published hue, so retaining packed colors keeps a
  // long delay line substantially smaller than cloning DomeFrame instances.
  public sealed class CompositeFrameHistory {
    private sealed record Entry(double Seconds, int[] Colors);

    private readonly List<Entry> entries = new();
    private readonly Stack<int[]> recycled = new();
    private object operationState;
    private int first;
    private int pixelCount = -1;

    public int Count => this.entries.Count - this.first;

    internal void Capture(
      DomeFrame frame, double seconds, double retentionSeconds
    ) {
      if (frame == null) {
        throw new ArgumentNullException(nameof(frame));
      }
      if (!double.IsFinite(seconds)) {
        seconds = 0;
      }
      retentionSeconds = double.IsFinite(retentionSeconds)
        ? Math.Max(0, retentionSeconds) : 0;
      if (this.pixelCount != frame.pixels.Length ||
          (this.Count != 0 &&
            seconds < this.entries[^1].Seconds)) {
        this.Reset(frame.pixels.Length);
      } else if (this.pixelCount < 0) {
        this.pixelCount = frame.pixels.Length;
      }

      this.Trim(seconds - retentionSeconds);
      if (this.Count != 0 && this.entries[^1].Seconds == seconds) {
        CopyColors(frame, this.entries[^1].Colors);
        return;
      }

      int[] colors = this.recycled.Count != 0
        ? this.recycled.Pop() : new int[this.pixelCount];
      CopyColors(frame, colors);
      this.entries.Add(new Entry(seconds, colors));
    }

    internal bool TryGetAtOrBefore(double seconds, out int[] colors) {
      colors = null;
      if (this.Count == 0 || seconds < this.entries[this.first].Seconds) {
        return false;
      }
      int low = this.first;
      int high = this.entries.Count - 1;
      while (low < high) {
        int middle = low + (high - low + 1) / 2;
        if (this.entries[middle].Seconds <= seconds) {
          low = middle;
        } else {
          high = middle - 1;
        }
      }
      colors = this.entries[low].Colors;
      return true;
    }

    // Stateful composite operations are registered as shared singletons, so
    // any mutable per-layer data must live with the compositor-owned history
    // entry. Temporal operations that do not need a frame delay line can use
    // this slot without allocating and retaining RGB snapshots every frame.
    internal T GetOrCreateState<T>(Func<T> create) where T : class {
      if (this.operationState is T state) {
        return state;
      }
      state = (create ?? throw new ArgumentNullException(nameof(create)))();
      this.operationState = state ?? throw new InvalidOperationException(
        "A composite operation state factory returned null.");
      return state;
    }

    private void Trim(double cutoff) {
      // Keep one sample at or before the cutoff so the oldest requested delay
      // still resolves when frame times do not land exactly on its timestamp.
      while (this.first + 1 < this.entries.Count &&
          this.entries[this.first + 1].Seconds <= cutoff) {
        this.Recycle(this.entries[this.first].Colors);
        this.first++;
      }
      if (this.first >= 128 && this.first * 2 >= this.entries.Count) {
        this.entries.RemoveRange(0, this.first);
        this.first = 0;
      }
    }

    private void Reset(int nextPixelCount) {
      this.entries.Clear();
      this.recycled.Clear();
      this.operationState = null;
      this.first = 0;
      this.pixelCount = nextPixelCount;
    }

    private void Recycle(int[] colors) {
      // Steady-state capture normally reuses the array on the very next frame.
      // The small cap avoids retaining a former long-delay configuration after
      // its horizon is shortened abruptly.
      if (this.recycled.Count < 4) {
        this.recycled.Push(colors);
      }
    }

    private static void CopyColors(DomeFrame frame, int[] colors) {
      for (int i = 0; i < colors.Length; i++) {
        colors[i] = frame.pixels[i].color;
      }
    }
  }
}
