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

    // Cap on how often we actually push a frame to the controller, independent
    // of the operator loop's own rate cap. This matters most when the output
    // runs on its own thread (separateThread): OutputThread spins free of the
    // operator loop, so this is the only thing bounding its send rate. Even
    // inline, it keeps the wire rate decoupled from engine compute — the frame
    // is ~25 KB and the BeagleBone/LEDs gain nothing from being driven faster
    // than this, it just burns CPU and network. 200 Hz is far above anything
    // visible while leaving generous headroom.
    private const int MaxRefreshRateHz = 200;
    private static readonly double MinSendIntervalMs = 1000.0 / MaxRefreshRateHz;

    private readonly string host;
    private readonly int port;
    private Socket socket;
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
    private int framesThisSecond;
    // Measures time since the last frame actually sent, to enforce
    // MaxRefreshRateHz. Only ever touched inside Update(), which runs on a
    // single thread (the output thread when separateThread, else the operator
    // thread), so it needs no synchronization.
    private readonly Stopwatch sendThrottleStopwatch;
    private readonly byte defaultChannel;
    // Whether a default channel was actually specified in the host string.
    // defaultChannel is a byte, so a ">= 0" check can never detect "unset".
    private readonly bool defaultChannelSet;
    // Read in Update() outside lockObject, written under it in Flush()/Update().
    private volatile bool flushHappened;

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
    ) {
      string[] parts = hostAndPort.Split(':');
      this.host = parts[0];
      this.port = Convert.ToInt32(parts[1]);
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
      this.flushHappened = false;
      this.separateThread = separateThread;
      this.setFPS = setFPS;

      this.frameRateStopwatch = new Stopwatch();
      this.frameRateStopwatch.Start();
      this.framesThisSecond = 0;
      this.sendThrottleStopwatch = Stopwatch.StartNew();
    }

    private bool active;
    private Thread outputThread;
    // Cooperative stop flag for OutputThread, replacing Thread.Abort().
    private volatile bool outputThreadStop;
    private readonly object lockObject = new object();
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
          // Set active before (re)starting/stopping so ConnectSocket's
          // this.Active check sees the new state.
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

    private void ConnectSocket() {
      while (true) {
        if (!this.Active || this.outputThreadStop) {
          return;
        }
        var asyncResult = this.socket.BeginConnect(this.host, this.port, null, null);
        bool result = asyncResult.AsyncWaitHandle.WaitOne(2000, true);
        if (result && this.socket.Connected) {
          return;
        }
        try {
          this.socket.Close();
        } catch (Exception e) {
          Debug.WriteLine("OPCAPI: error closing socket during reconnect: " + e);
        }
        this.InitializeSocket();
      }
    }

    private void DisconnectSocket() {
      try {
        this.socket.Disconnect(true);
      } catch (Exception e) {
        Debug.WriteLine("OPCAPI: error disconnecting socket: " + e);
      }
      try {
        this.socket.Close();
      } catch (Exception e) {
        Debug.WriteLine("OPCAPI: error closing socket: " + e);
      }
      this.InitializeSocket();
      // Reset the realized pixel set on (re)connect. Built off-lock then swapped
      // in under lockObject so a lock-free SetPixel reader never observes a
      // half-built dictionary (channels is volatile, so the new reference is
      // visible immediately). DisconnectSocket is now reachable from Update()'s
      // catch outside the lock; lock is reentrant, so callers already holding it
      // (the Active setter) are fine.
      var fresh = new Dictionary<byte, ChannelBuffer>();
      if (this.defaultChannelSet) {
        fresh[this.defaultChannel] = new ChannelBuffer(InitialChannelCapacity);
      }
      lock (this.lockObject) {
        this.channels = fresh;
      }
    }

    // Returns true if a frame was actually pushed to the controller this call.
    private bool Update() {
      if (!this.flushHappened || this.socket == null || !this.socket.Connected) {
        return false;
      }
      // Refresh-rate cap: if we pushed a frame too recently, leave flushHappened
      // set and try again on a later pass. This throttles both the threaded
      // output loop (which spins) and the inline OperatorUpdate() path.
      if (this.sendThrottleStopwatch.Elapsed.TotalMilliseconds < MinSendIntervalMs) {
        return false;
      }
      int totalLength;
      lock (this.lockObject) {
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
          this.flushHappened = false;
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
      // socket are only ever touched on this single Update() thread, so they
      // need no lock here.
      try {
        this.socket.Send(this.sendBuffer, 0, totalLength, SocketFlags.None);
        this.sendThrottleStopwatch.Restart();
        this.flushHappened = false;
        return true;
      } catch (Exception e) {
        Debug.WriteLine("OPCAPI: socket send failed, reconnecting: " + e);
        this.DisconnectSocket();
        this.ConnectSocket();
        return false;
      }
    }

    public void OperatorUpdate() {
      if (!this.separateThread) {
        this.Update();
      }
    }

    private void OutputThread() {
      this.ConnectSocket();
      while (!this.outputThreadStop) {
        if (this.frameRateStopwatch.ElapsedMilliseconds >= 1000) {
          this.frameRateStopwatch.Restart();
          this.setFPS(this.framesThisSecond);
          this.framesThisSecond = 0;
        }
        // Count only frames that actually went out so the reported FPS reflects
        // the real (throttled) refresh rate rather than the spin-loop rate.
        if (this.Update()) {
          this.framesThisSecond++;
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
        this.flushHappened = true;
      }
    }

    public void SetPixel(byte channelIndex, int pixelIndex, int color) {
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
