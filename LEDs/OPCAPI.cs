using System.IO.Ports;
using Spectrum.Base;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System;
using System.Net.Sockets;

namespace Spectrum.LEDs {

  /**
   * Open Pixel Control API
   */
  class OPCAPI {

    private string host;
    private int port;
    private Socket socket;
    // (channel ID, pixel ID) => RGB value
    // these values have been realized (ie. a flush has happened)
    private ConcurrentDictionary<Tuple<byte, int>, int> currentPixelColors;
    // these values haven't been realized (ie. flush hasn't happened yet)
    private ConcurrentDictionary<Tuple<byte, int>, int> nextPixelColors;
    // channel ID => pixel ID
    // these values have been realized (ie. a flush has happened)
    private ConcurrentDictionary<byte, int> currentFirstPixelNotSet;
    // these values haven't been realized (ie. flush hasn't happened yet)
    private ConcurrentDictionary<byte, int> nextFirstPixelNotSet;
    private bool separateThread;
    private Action<int> setFPS;
    private Stopwatch frameRateStopwatch;
    private int framesThisSecond;
    private byte defaultChannel;
    private bool flushHappened;

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
      }
      this.socket = new Socket(
        AddressFamily.InterNetwork,
        SocketType.Stream,
        ProtocolType.Tcp
      );
      this.currentPixelColors = new ConcurrentDictionary<Tuple<byte, int>, int>();
      this.nextPixelColors = new ConcurrentDictionary<Tuple<byte, int>, int>();
      this.currentFirstPixelNotSet = new ConcurrentDictionary<byte, int>();
      this.nextFirstPixelNotSet = new ConcurrentDictionary<byte, int>();
      this.flushHappened = false;
      this.separateThread = separateThread;
      this.setFPS = setFPS;

      this.frameRateStopwatch = new Stopwatch();
      this.frameRateStopwatch.Start();
      this.framesThisSecond = 0;
    }

    private bool active;
    private Thread outputThread;
    private object lockObject = new object();
    public bool Active {
      get {
        lock (this.lockObject) {
          return this.active;
        }
      }
      set {
        lock (this.lockObject) {
          if (this.active == value) {
            return;
          }
          if (value) {
            if (this.separateThread) {
              this.outputThread = new Thread(this.OutputThread);
              this.outputThread.Start();
            } else {
              this.ConnectSocket();
            }
          } else {
            if (this.outputThread != null) {
              this.outputThread.Abort();
              this.outputThread.Join();
              this.outputThread = null;
            } else {
              this.DisconnectSocket();
            }
          }
          this.active = value;
        }
      }
    }

    private void ConnectSocket() {
      this.socket.Connect(this.host, this.port);
    }

    private void DisconnectSocket() {
      this.socket.Disconnect(true);
      this.currentPixelColors = new ConcurrentDictionary<Tuple<byte, int>, int>();
      this.nextPixelColors = new ConcurrentDictionary<Tuple<byte, int>, int>();
      this.currentFirstPixelNotSet = new ConcurrentDictionary<byte, int>();
      this.nextFirstPixelNotSet = new ConcurrentDictionary<byte, int>();
    }

    private void Update() {
      if (!this.flushHappened) {
        return;
      }
      lock (this.lockObject) {
        // channel ID => array of bits, one per color
        Dictionary<byte, int[]> colorsPerChannel = new Dictionary<byte, int[]>();
        foreach (var pixelPair in this.currentFirstPixelNotSet) {
          colorsPerChannel[pixelPair.Key] = new int[pixelPair.Value];
        }
        foreach (var pixelColorPair in this.currentPixelColors) {
          var channelIndex = pixelColorPair.Key.Item1;
          var pixelIndex = pixelColorPair.Key.Item2;
          if (pixelIndex < colorsPerChannel[channelIndex].Length) {
            colorsPerChannel[channelIndex][pixelIndex] = pixelColorPair.Value;
          }
        }
        byte[][] messages = new byte[colorsPerChannel.Count][];
        int i = 0;
        foreach (var channelPixelsPair in colorsPerChannel) {
          List<byte> message = new List<byte>();
          message.Add(channelPixelsPair.Key);
          message.Add(0);
          var length = channelPixelsPair.Value.Length * 3;
          message.Add((byte)(length >> 8));
          message.Add((byte)length);
          foreach (int color in channelPixelsPair.Value) {
            message.Add((byte)(color >> 16));
            message.Add((byte)(color >> 8));
            message.Add((byte)color);
          }
          messages[i++] = message.ToArray();
        }
        byte[] bytes = messages.SelectMany(a => a).ToArray();
        try {
          this.socket.Send(bytes);
        } catch (Exception) { }
        this.flushHappened = false;
      }
    }

    public void OperatorUpdate() {
      if (!this.separateThread) {
        this.Update();
      }
    }

    private void OutputThread() {
      this.ConnectSocket();
      try {
        while (true) {
          if (this.frameRateStopwatch.ElapsedMilliseconds >= 1000) {
            this.frameRateStopwatch.Restart();
            this.setFPS(this.framesThisSecond);
            this.framesThisSecond = 0;
          }
          this.framesThisSecond++;
          this.Update();
        }
      } catch (ThreadAbortException) {
        this.DisconnectSocket();
      }
    }

    public void Flush() {
      lock (this.lockObject) {
        this.currentFirstPixelNotSet = this.nextFirstPixelNotSet;
        this.nextFirstPixelNotSet =
          new ConcurrentDictionary<byte, int>(this.nextFirstPixelNotSet);
        this.currentPixelColors = this.nextPixelColors;
        this.nextPixelColors =
          new ConcurrentDictionary<Tuple<byte, int>, int>(this.nextPixelColors);
        this.flushHappened = true;
      }
    }

    public void SetPixel(byte channelIndex, int pixelIndex, int color) {
      lock (this.lockObject) {
        this.nextFirstPixelNotSet[channelIndex] =
          this.nextFirstPixelNotSet.ContainsKey(channelIndex)
            ? Math.Max(this.nextFirstPixelNotSet[channelIndex], pixelIndex + 1)
            : pixelIndex + 1;
        var pixelTuple = new Tuple<byte, int>(channelIndex, pixelIndex);
        this.nextPixelColors[pixelTuple] = color;
      }
    }

    /**
     * If in your first param to OPCAPI you specified a default channel, you can
     * use this method. Otherwise it will crash.
     */
    public void SetPixel(int pixelIndex, int color) {
      Debug.Assert(this.defaultChannel >= 0, "defaultChannel should be set");
      this.SetPixel(this.defaultChannel, pixelIndex, color);
    }

  }

}