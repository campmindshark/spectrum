using Spectrum.Base;
using System.Threading;
using System.Collections.Generic;
using System.Diagnostics;
using System;
using System.Net.Sockets;

namespace Spectrum.LEDs {

  /**
   * Open Pixel Control API
   */
  class OPCAPI {

    /**
     * Double-buffered, preallocated pixel storage for a single OPC channel.
     *
     * The pixel set on a channel is dense and (after warmup) fixed-size, so we
     * back it with plain int[] RGB buffers instead of a tuple-keyed dictionary.
     * The visualize/operator thread writes into "next"; Flush() swaps "next"
     * into "current" (the realized frame) and copies it back into "next" so the
     * next frame inherits the previous frame's pixels (the persistence
     * semantics the old per-flush dictionary copy provided). The output thread
     * reads only "current".
     */
    private class ChannelBuffer {
      public int[] next;
      public int[] current;
      // (max pixel index that has been set) + 1, i.e. the realized pixel count.
      public int nextCount;
      public int currentCount;

      public ChannelBuffer(int initialCapacity) {
        this.next = new int[initialCapacity];
        this.current = new int[initialCapacity];
      }
    }

    // Smallest channel buffer we allocate; grows on demand to the max pixel
    // index actually written.
    private const int InitialChannelCapacity = 64;

    // The OPC payload length is an unsigned 16-bit byte count. RGB pixels use
    // three bytes each, so this is the largest dense channel that can be
    // represented without wrapping the header length on the wire.
    internal const int MaxPixelsPerChannel = ushort.MaxValue / 3;

    // Cap on how often we actually push a frame to the controller, independent
    // of the operator loop's own rate cap. This matters most when the output
    // runs on its own thread (separateThread): OutputThread spins free of the
    // operator loop, so this is the only thing bounding its send rate. Even
    // inline, it keeps the wire rate decoupled from engine compute — the frame
    // is ~25 KB and the BeagleBone/LEDs gain nothing from being driven faster
    // than this, it just burns CPU and network. 200 Hz is far above anything
    // visible while leaving generous headroom.
    private const int MaxRefreshRateHz = 200;
    private static readonly TimeSpan DefaultMinSendInterval =
      TimeSpan.FromSeconds(1.0 / MaxRefreshRateHz);

    // A missing controller must not stall the operator. Connection attempts are
    // started asynchronously and polled by the normal update loop; after a
    // failure, wait briefly before creating another socket so an offline show
    // network does not turn into a tight connect loop.
    private const int ReconnectDelayMs = 250;
    private const int ConnectTimeoutMs = 2000;
    private static readonly long ReconnectDelayTicks =
      Stopwatch.Frequency * ReconnectDelayMs / 1000;
    private static readonly long ConnectTimeoutTicks =
      Stopwatch.Frequency * ConnectTimeoutMs / 1000;

    private readonly string host;
    private readonly int port;
    private readonly double minSendIntervalMs;
    private Socket socket;
    private IAsyncResult connectAttempt;
    private long connectAttemptStartedTimestamp;
    private long nextConnectAttemptTimestamp;
    private readonly object connectionLock = new object();
    // channel ID => its double-buffered pixel storage. Structural additions
    // happen only under lockObject; after warmup the set of channels is fixed,
    // so the hot SetPixel path reads it lock-free. volatile so a lock-free
    // reader always sees the latest reference after a copy-on-write swap or a
    // reconnect reset.
    private volatile Dictionary<byte, ChannelBuffer> channels;
    // Reusable OPC wire buffer; grown (never shrunk) to fit the largest frame.
    private byte[] sendBuffer;
    private readonly bool separateThread;
    private readonly Action<int> setFPS;
    private readonly Stopwatch frameRateStopwatch;
    private int sentFramesInWindow;
    // Measures time since the last frame actually sent, to enforce
    // MaxRefreshRateHz. Touched only on a single thread — the output thread when
    // separateThread (Update() plus OutputThread's inter-send sleep), else the
    // operator thread (Update()) — so it needs no synchronization.
    private readonly Stopwatch sendThrottleStopwatch;
    private readonly byte defaultChannel;
    // Whether a default channel was actually specified in the host string.
    // defaultChannel is a byte, so a ">= 0" check can never detect "unset".
    private readonly bool defaultChannelSet;
    // Flush and send generations avoid losing a new frame when Flush() races a
    // send on the separate output thread. A failed send leaves the generation
    // pending, so the newest realized frame is sent after reconnect.
    private long flushGeneration;
    private long sentGeneration;

    /**
     * hostAndPort looks like:
     *   192.168.1.3:5678
     * you can optionally specify a default OPC channel at the end, like:
     *   192.168.1.3:5678:0
     */
    public OPCAPI(
      string hostAndPort,
      bool separateThread,
      Action<int> setFPS
    ) : this(
      hostAndPort, separateThread, setFPS,
      DefaultMinSendInterval
    ) {
    }

    internal OPCAPI(
      string hostAndPort,
      bool separateThread,
      Action<int> setFPS,
      TimeSpan minSendInterval
    ) {
      if (minSendInterval < TimeSpan.Zero) {
        throw new ArgumentOutOfRangeException(nameof(minSendInterval));
      }
      string[] parts = hostAndPort.Split(':');
      this.host = parts[0];
      this.port = Convert.ToInt32(parts[1]);
      this.minSendIntervalMs = minSendInterval.TotalMilliseconds;
      if (parts.Length >= 3) {
        this.defaultChannel = Convert.ToByte(parts[2]);
        this.defaultChannelSet = true;
      }
      this.InitializeSocket();
      this.channels = new Dictionary<byte, ChannelBuffer>();
      // Pre-create the default channel so the common single-channel path never
      // has to structurally modify the dictionary at runtime.
      if (this.defaultChannelSet) {
        this.channels[this.defaultChannel] =
          new ChannelBuffer(InitialChannelCapacity);
      }
      this.flushGeneration = 0;
      this.sentGeneration = 0;
      this.separateThread = separateThread;
      this.setFPS = setFPS;

      this.frameRateStopwatch = new Stopwatch();
      this.frameRateStopwatch.Start();
      this.sentFramesInWindow = 0;
      this.sendThrottleStopwatch = Stopwatch.StartNew();
    }

    private volatile bool active;
    private Thread outputThread;
    // Cooperative stop flag for OutputThread, replacing Thread.Abort().
    private volatile bool outputThreadStop;
    private readonly object lockObject = new object();

    internal WaitHandle PendingConnectWaitHandle {
      get {
        lock (this.connectionLock) {
          return this.connectAttempt?.AsyncWaitHandle;
        }
      }
    }

    public bool Active {
      get {
        lock (this.lockObject) {
          return this.active;
        }
      }
      set {
        Thread threadToJoin = null;
        lock (this.lockObject) {
          if (this.active == value) {
            return;
          }
          // Set active before (re)starting/stopping so the asynchronous
          // connector sees the new state.
          this.active = value;
          if (value) {
            if (this.separateThread) {
              this.outputThreadStop = false;
              this.outputThread = new Thread(this.OutputThread);
              this.outputThread.Start();
            } else {
              this.ConnectSocket();
            }
          } else {
            if (this.outputThread != null) {
              // Signal the loop to exit; it disconnects the socket itself.
              this.outputThreadStop = true;
              threadToJoin = this.outputThread;
              this.outputThread = null;
            } else {
              this.DisconnectSocket();
            }
          }
        }
        // Join outside lockObject: OutputThread takes lockObject in Update(), so
        // joining while holding it could deadlock.
        threadToJoin?.Join();
      }
    }

    private void InitializeSocket() {
      this.socket = new Socket(
        AddressFamily.InterNetwork,
        SocketType.Stream,
        ProtocolType.Tcp
      );
      // Disable Nagle's algorithm (M4): without this, the ~25 KB frame can be
      // coalesced/delayed by the TCP stack, adding latency between "frame ready"
      // and "frame on the wire" and making the OPC output rate uneven. The frame
      // is already a single Send(), so coalescing buys us nothing.
      this.socket.NoDelay = true;
    }

    // Starts or polls one asynchronous connection attempt. This method never
    // waits for the network, so both inline and threaded output remain
    // responsive while the controller is absent or becoming reachable.
    private bool ConnectSocket() {
      lock (this.connectionLock) {
        if (!this.active || this.outputThreadStop) {
          return false;
        }

        if (this.connectAttempt != null) {
          if (!this.connectAttempt.IsCompleted) {
            if (Stopwatch.GetTimestamp() -
                this.connectAttemptStartedTimestamp >= ConnectTimeoutTicks) {
              Debug.WriteLine("OPCAPI: connection attempt timed out");
              this.ReplaceSocketForRetry();
            }
            return false;
          }
          IAsyncResult completed = this.connectAttempt;
          this.connectAttempt = null;
          this.connectAttemptStartedTimestamp = 0;
          try {
            this.socket.EndConnect(completed);
            if (this.socket.Connected) {
              this.nextConnectAttemptTimestamp = 0;
              return true;
            }
          } catch (Exception e) {
            Debug.WriteLine("OPCAPI: connection attempt failed: " + e);
          }
          this.ReplaceSocketForRetry();
          return false;
        }

        if (this.socket.Connected) {
          return true;
        }
        if (Stopwatch.GetTimestamp() < this.nextConnectAttemptTimestamp) {
          return false;
        }
        try {
          this.connectAttempt = this.socket.BeginConnect(
            this.host, this.port, null, null);
          this.connectAttemptStartedTimestamp = Stopwatch.GetTimestamp();
        } catch (Exception e) {
          Debug.WriteLine("OPCAPI: could not start connection attempt: " + e);
          this.ReplaceSocketForRetry();
        }
        return false;
      }
    }

    private void DisconnectSocket() {
      lock (this.connectionLock) {
        this.CloseSocket();
        this.InitializeSocket();
        this.connectAttempt = null;
        this.connectAttemptStartedTimestamp = 0;
        this.nextConnectAttemptTimestamp = 0;
      }
      // An explicit deactivation starts with a clean pixel set next time. A
      // transport failure uses ReplaceSocketForRetry instead and deliberately
      // preserves the newest pending frame.
      var fresh = new Dictionary<byte, ChannelBuffer>();
      if (this.defaultChannelSet) {
        fresh[this.defaultChannel] = new ChannelBuffer(InitialChannelCapacity);
      }
      lock (this.lockObject) {
        this.channels = fresh;
        Volatile.Write(
          ref this.sentGeneration,
          Volatile.Read(ref this.flushGeneration));
      }
    }

    private void ReplaceSocketForRetry() {
      this.CloseSocket();
      this.InitializeSocket();
      this.connectAttempt = null;
      this.connectAttemptStartedTimestamp = 0;
      this.nextConnectAttemptTimestamp =
        Stopwatch.GetTimestamp() + ReconnectDelayTicks;
    }

    private void CloseSocket() {
      try {
        this.socket?.Close();
      } catch (Exception e) {
        Debug.WriteLine("OPCAPI: error closing socket: " + e);
      }
    }

    // Returns true if a frame was actually pushed to the controller this call.
    private bool Update() {
      if (!this.ConnectSocket()) {
        return false;
      }
      long pendingGeneration = Volatile.Read(ref this.flushGeneration);
      if (pendingGeneration == Volatile.Read(ref this.sentGeneration)) {
        return false;
      }
      // Refresh-rate cap: if we pushed a frame too recently, leave its
      // generation pending and try again on a later pass. This throttles both
      // the threaded output loop and the inline OperatorUpdate() path.
      if (this.sendThrottleStopwatch.Elapsed.TotalMilliseconds <
          this.minSendIntervalMs) {
        return false;
      }
      int totalLength;
      long sendingGeneration;
      lock (this.lockObject) {
        sendingGeneration = Volatile.Read(ref this.flushGeneration);
        // Each non-empty channel contributes a 4-byte OPC header plus 3 bytes
        // per pixel. Channels with no pixels set yet are skipped, matching the
        // original (which only emitted channels present in the realized set).
        totalLength = 0;
        foreach (var channelPair in this.channels) {
          if (channelPair.Value.currentCount > 0) {
            totalLength += 4 + channelPair.Value.currentCount * 3;
          }
        }
        if (totalLength == 0) {
          Volatile.Write(ref this.sentGeneration, sendingGeneration);
          return false;
        }
        if (this.sendBuffer == null || this.sendBuffer.Length < totalLength) {
          this.sendBuffer = new byte[totalLength];
        }
        byte[] bytes = this.sendBuffer;
        int offset = 0;
        foreach (var channelPair in this.channels) {
          ChannelBuffer channel = channelPair.Value;
          int count = channel.currentCount;
          if (count == 0) {
            continue;
          }
          int length = count * 3;
          bytes[offset++] = channelPair.Key;
          bytes[offset++] = 0;
          bytes[offset++] = (byte)(length >> 8);
          bytes[offset++] = (byte)length;
          int[] colors = channel.current;
          for (int p = 0; p < count; p++) {
            int color = colors[p];
            bytes[offset++] = (byte)(color >> 16);
            bytes[offset++] = (byte)(color >> 8);
            bytes[offset++] = (byte)color;
          }
        }
      }
      // The send (and any resulting reconnect) runs OUTSIDE lockObject. A
      // reconnect can block for seconds; holding lockObject across it would
      // stall Flush()/SetPixel on the operator thread for the whole outage,
      // defeating the point of the separate output thread. sendBuffer and the
      // sendBuffer is touched only on this Update() thread. Capture the socket
      // so a concurrent explicit deactivation can close it safely; SendAll then
      // throws and the frame generation stays pending.
      Socket sendingSocket = this.socket;
      try {
        SendAll(
          this.sendBuffer, totalLength,
          new SocketByteSender(sendingSocket));
        this.sendThrottleStopwatch.Restart();
        Volatile.Write(ref this.sentGeneration, sendingGeneration);
        return true;
      } catch (Exception e) {
        Debug.WriteLine("OPCAPI: socket send failed, reconnecting: " + e);
        lock (this.connectionLock) {
          if (ReferenceEquals(this.socket, sendingSocket)) {
            this.ReplaceSocketForRetry();
          }
        }
        return false;
      }
    }

    internal interface IOpcByteSender {
      int Send(byte[] buffer, int offset, int count);
    }

    private readonly struct SocketByteSender : IOpcByteSender {
      private readonly Socket socket;

      public SocketByteSender(Socket socket) {
        this.socket = socket;
      }

      public int Send(byte[] buffer, int offset, int count) =>
        this.socket.Send(buffer, offset, count, SocketFlags.None);
    }

    // Socket.Send may legally accept fewer bytes than requested. Keep this
    // loop independently testable so a platform/socket implementation that
    // returns partial writes cannot truncate an OPC message.
    internal static void SendAll<TSender>(
      byte[] buffer, int length, TSender sender
    ) where TSender : IOpcByteSender {
      int sent = 0;
      while (sent < length) {
        int count = sender.Send(buffer, sent, length - sent);
        if (count <= 0 || count > length - sent) {
          throw new SocketException((int)SocketError.ConnectionReset);
        }
        sent += count;
      }
    }

    public void OperatorUpdate() {
      if (!this.separateThread) {
        this.Update();
      }
    }

    private void OutputThread() {
      this.ConnectSocket();
      // Do not include time spent inactive or establishing the initial
      // connection in the first send-rate reading.
      this.frameRateStopwatch.Restart();
      this.sentFramesInWindow = 0;
      while (!this.outputThreadStop) {
        // Count only frames that actually went out so the reported FPS reflects
        // the real (throttled) refresh rate rather than the spin-loop rate.
        if (this.Update()) {
          this.sentFramesInWindow++;
        } else {
          // Nothing was sent this pass: either we're inside the MaxRefreshRateHz
          // window, there's no new frame, or we're disconnected. Sleep instead
          // of busy-spinning — without this the loop pins a whole CPU core just
          // re-reading the throttle Stopwatch between sends. Sleep off the
          // whole-millisecond remainder until the next send is due (the same
          // Sleep-vs-spin tradeoff the operator loop makes in ThrottleFrame),
          // with a 1ms floor so a no-new-frame/disconnected pass still yields.
          double remainingMs = this.minSendIntervalMs -
            this.sendThrottleStopwatch.Elapsed.TotalMilliseconds;
          int sleepMs = remainingMs > 1 ? (int)remainingMs : 1;
          Thread.Sleep(sleepMs);
        }
        if (this.frameRateStopwatch.ElapsedMilliseconds >= 1000) {
          double elapsedSeconds =
            this.frameRateStopwatch.Elapsed.TotalSeconds;
          this.setFPS((int)Math.Round(
            this.sentFramesInWindow / elapsedSeconds,
            MidpointRounding.AwayFromZero
          ));
          this.sentFramesInWindow = 0;
          this.frameRateStopwatch.Restart();
        }
      }
      this.DisconnectSocket();
    }

    public void Flush() {
      lock (this.lockObject) {
        foreach (var channelPair in this.channels) {
          ChannelBuffer channel = channelPair.Value;
          // Realize "next" into "current" via a reference swap (no per-frame
          // dictionary rebuild or deep copy).
          int[] realized = channel.next;
          channel.next = channel.current;
          channel.current = realized;
          channel.currentCount = channel.nextCount;
          // Carry the realized frame back into "next" so visualizers that only
          // overwrite some pixels inherit the rest (matches the old semantics
          // where the next dictionary started as a copy of the flushed one).
          if (channel.next.Length < channel.current.Length) {
            channel.next = new int[channel.current.Length];
          }
          Array.Copy(channel.current, channel.next, channel.currentCount);
          channel.nextCount = channel.currentCount;
        }
        Interlocked.Increment(ref this.flushGeneration);
      }
    }

    // Clears the pending dense pixel set for one channel. The dome uses this
    // before writing the first complete frame after a cable-map change; without
    // it, persistent pixels at addresses used only by the old mapping would be
    // retransmitted as stale colors.
    public void ClearPixels(byte channelIndex) {
      ChannelBuffer channel = this.GetOrCreateChannel(channelIndex);
      Array.Clear(channel.next, 0, channel.nextCount);
      channel.nextCount = 0;
    }

    public void ClearPixels() {
      Debug.Assert(this.defaultChannelSet, "defaultChannel should be set");
      this.ClearPixels(this.defaultChannel);
    }

    public void SetPixel(byte channelIndex, int pixelIndex, int color) {
      if ((uint)pixelIndex >= (uint)MaxPixelsPerChannel) {
        throw new ArgumentOutOfRangeException(
          nameof(pixelIndex), pixelIndex,
          "An OPC channel can contain at most " +
          MaxPixelsPerChannel + " RGB pixels."
        );
      }
      ChannelBuffer channel = this.GetOrCreateChannel(channelIndex);
      if (pixelIndex >= channel.next.Length) {
        int newCapacity = channel.next.Length;
        while (newCapacity <= pixelIndex) {
          newCapacity *= 2;
        }
        Array.Resize(ref channel.next, newCapacity);
      }
      channel.next[pixelIndex] = color;
      if (pixelIndex + 1 > channel.nextCount) {
        channel.nextCount = pixelIndex + 1;
      }
    }

    // Looks up a channel's buffer, creating it on first use. The lock is taken
    // only on the (rare) creation path so it doesn't race the output thread's
    // enumeration of the channels dictionary in Flush()/Update(); the common
    // case (channel already exists) reads the dictionary lock-free.
    private ChannelBuffer GetOrCreateChannel(byte channelIndex) {
      ChannelBuffer channel;
      if (this.channels.TryGetValue(channelIndex, out channel)) {
        return channel;
      }
      lock (this.lockObject) {
        if (!this.channels.TryGetValue(channelIndex, out channel)) {
          channel = new ChannelBuffer(InitialChannelCapacity);
          // Copy-on-write the dictionary so a concurrent lock-free reader in
          // SetPixel never observes a half-mutated Dictionary.
          var updated = new Dictionary<byte, ChannelBuffer>(this.channels);
          updated[channelIndex] = channel;
          this.channels = updated;
        }
        return channel;
      }
    }

    /**
     * If in your first param to OPCAPI you specified a default channel, you can
     * use this method. Otherwise it will crash.
     */
    public void SetPixel(int pixelIndex, int color) {
      Debug.Assert(this.defaultChannelSet, "defaultChannel should be set");
      this.SetPixel(this.defaultChannel, pixelIndex, color);
    }

  }

}
