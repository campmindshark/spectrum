using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Spectrum.Base;

namespace Spectrum.LEDs {

  // Owns the native and browser simulator publication channels. Diagnostic
  // pixels retain their ordering in bounded queues, while normal rendered
  // frames use replaceable pooled mailboxes so a slow UI never builds a
  // per-frame backlog.
  internal sealed class DomeSimulatorPublisher {
    internal const int WebFramesPerSecond = 60;
    private const int CommandQueueCap = 20000;
    private static readonly long WebFrameIntervalTicks =
      Stopwatch.Frequency / WebFramesPerSecond;

    private readonly object nativeFrameGate = new object();
    private readonly object webFrameGate = new object();
    private volatile bool nativeHasConsumer;
    private volatile bool webHasConsumer;
    private int[]? latestNativeFrame;
    private int[]? latestWebFrame;
    private bool nativeFramePublishedSinceFlush;
    private bool webFramePublishedSinceFlush;
    private long nextWebFrameTimestamp;

    internal ConcurrentQueue<DomeLEDCommand> NativeCommands { get; } =
      new ConcurrentQueue<DomeLEDCommand>();

    internal ConcurrentQueue<DomeLEDCommand> WebCommands { get; } =
      new ConcurrentQueue<DomeLEDCommand>();

    internal bool NativeHasConsumer {
      get { return this.nativeHasConsumer; }
      set {
        int[]? abandoned = null;
        lock (this.nativeFrameGate) {
          this.nativeHasConsumer = value;
          if (!value) {
            abandoned = this.latestNativeFrame;
            this.latestNativeFrame = null;
          }
        }
        ReturnFrame(abandoned);
      }
    }

    internal bool WebHasConsumer {
      get { return this.webHasConsumer; }
      set {
        int[]? abandoned = null;
        lock (this.webFrameGate) {
          this.webHasConsumer = value;
          if (!value) {
            abandoned = this.latestWebFrame;
            this.latestWebFrame = null;
            this.nextWebFrameTimestamp = 0;
          }
        }
        ReturnFrame(abandoned);
        if (!value) {
          this.WebCommands.Clear();
        }
      }
    }

    internal bool ShouldPublishNative(bool simulationEnabled) =>
      simulationEnabled && this.NativeHasConsumer;

    internal bool ShouldPublishWeb => this.WebHasConsumer;

    internal DomeSimulatorFrameCapture BeginFrame(
      int pixelCount,
      bool simulationEnabled
    ) {
      bool publishNative = this.ShouldPublishNative(simulationEnabled);
      // Beginning a normal frame suppresses diagnostic Flush commands for the
      // browser even when its 60 FPS sampler skips this particular engine tick.
      if (this.ShouldPublishWeb) {
        this.webFramePublishedSinceFlush = true;
      }
      bool publishWeb = this.ShouldCaptureWebFrame();
      return new DomeSimulatorFrameCapture(
        publishNative ? ArrayPool<int>.Shared.Rent(pixelCount) : null,
        publishWeb ? ArrayPool<int>.Shared.Rent(pixelCount) : null);
    }

    internal void CompleteFrame(
      DomeSimulatorFrameCapture capture,
      bool simulationEnabled
    ) {
      if (capture.NativeFrame != null) {
        if (simulationEnabled &&
            this.PublishNativeFrame(capture.NativeFrame)) {
          this.nativeFramePublishedSinceFlush = true;
        } else {
          ReturnFrame(capture.NativeFrame);
        }
      }
      if (capture.WebFrame != null) {
        if (this.PublishWebFrame(capture.WebFrame)) {
          this.webFramePublishedSinceFlush = true;
        } else {
          ReturnFrame(capture.WebFrame);
        }
      }
    }

    internal void PublishPixel(
      int strutIndex,
      int ledIndex,
      int color,
      bool simulationEnabled
    ) {
      if (this.ShouldPublishNative(simulationEnabled)) {
        EnqueueCommand(this.NativeCommands, new DomeLEDCommand {
          strutIndex = strutIndex,
          ledIndex = ledIndex,
          color = color,
        });
      }
      if (this.ShouldPublishWeb) {
        EnqueueCommand(this.WebCommands, new DomeLEDCommand {
          strutIndex = strutIndex,
          ledIndex = ledIndex,
          color = color,
        });
      }
    }

    internal void FlushFrame(bool simulationEnabled) {
      if (this.ShouldPublishNative(simulationEnabled) &&
          !this.nativeFramePublishedSinceFlush) {
        EnqueueFlush(this.NativeCommands);
      }
      if (this.ShouldPublishWeb &&
          !this.webFramePublishedSinceFlush) {
        EnqueueFlush(this.WebCommands);
      }
      this.nativeFramePublishedSinceFlush = false;
      this.webFramePublishedSinceFlush = false;
    }

    internal void FlushDiagnostics(bool simulationEnabled) {
      if (this.ShouldPublishNative(simulationEnabled)) {
        EnqueueFlush(this.NativeCommands);
      }
      if (this.ShouldPublishWeb) {
        EnqueueFlush(this.WebCommands);
      }
    }

    internal bool TryTakeNativeFrame(
      [NotNullWhen(true)] out int[]? frame
    ) {
      lock (this.nativeFrameGate) {
        frame = this.latestNativeFrame;
        this.latestNativeFrame = null;
        return frame != null;
      }
    }

    internal bool TryTakeWebFrame(out int[]? frame) {
      lock (this.webFrameGate) {
        frame = this.latestWebFrame;
        this.latestWebFrame = null;
        return frame != null;
      }
    }

    internal static void ReturnFrame(int[]? frame) {
      if (frame != null) {
        ArrayPool<int>.Shared.Return(frame);
      }
    }

    private bool ShouldCaptureWebFrame() {
      if (!this.ShouldPublishWeb) {
        return false;
      }
      long now = Stopwatch.GetTimestamp();
      lock (this.webFrameGate) {
        if (!this.webHasConsumer ||
            now < this.nextWebFrameTimestamp) {
          return false;
        }
        // Advance from the previous target so engine-tick quantization does not
        // steadily undershoot 60 FPS. Reset after a missed whole interval to
        // avoid catch-up bursts.
        if (this.nextWebFrameTimestamp == 0 ||
            now - this.nextWebFrameTimestamp >= WebFrameIntervalTicks) {
          this.nextWebFrameTimestamp = now + WebFrameIntervalTicks;
        } else {
          this.nextWebFrameTimestamp += WebFrameIntervalTicks;
        }
        return true;
      }
    }

    private bool PublishNativeFrame(int[] frame) {
      int[]? superseded = null;
      lock (this.nativeFrameGate) {
        if (!this.nativeHasConsumer) {
          return false;
        }
        superseded = this.latestNativeFrame;
        this.latestNativeFrame = frame;
      }
      ReturnFrame(superseded);
      // A complete normal frame supersedes diagnostics from an older mode.
      if (!this.NativeCommands.IsEmpty) {
        this.NativeCommands.Clear();
      }
      return true;
    }

    private bool PublishWebFrame(int[] frame) {
      int[]? superseded = null;
      lock (this.webFrameGate) {
        if (!this.webHasConsumer) {
          return false;
        }
        superseded = this.latestWebFrame;
        this.latestWebFrame = frame;
      }
      ReturnFrame(superseded);
      this.WebCommands.Clear();
      return true;
    }

    private static void EnqueueFlush(
      ConcurrentQueue<DomeLEDCommand> queue
    ) {
      EnqueueCommand(queue, new DomeLEDCommand { isFlush = true });
    }

    private static void EnqueueCommand(
      ConcurrentQueue<DomeLEDCommand> queue,
      DomeLEDCommand command
    ) {
      queue.Enqueue(command);
      while (queue.Count > CommandQueueCap && queue.TryDequeue(out _)) {
      }
    }
  }

  internal readonly struct DomeSimulatorFrameCapture {
    internal DomeSimulatorFrameCapture(
      int[]? nativeFrame,
      int[]? webFrame
    ) {
      this.NativeFrame = nativeFrame;
      this.WebFrame = webFrame;
    }

    internal int[]? NativeFrame { get; }
    internal int[]? WebFrame { get; }
    internal bool Enabled =>
      this.NativeFrame != null || this.WebFrame != null;

    internal void SetColor(int index, int color) {
      if (this.NativeFrame != null) {
        this.NativeFrame[index] = color;
      }
      if (this.WebFrame != null) {
        this.WebFrame[index] = color;
      }
    }
  }
}
