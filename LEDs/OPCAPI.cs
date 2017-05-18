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
    private ConcurrentDictionary<Tuple<byte, int>, int> currentPixelColors;
    // channel ID => pixel ID
    private ConcurrentDictionary<byte, int> firstPixelNotSet;
    private bool separateThread;
    private Action<int> setFPS;
    private Stopwatch frameRateStopwatch;
    private int framesThisSecond;

    public OPCAPI(
      string hostAndPort,
      bool separateThread,
      Action<int> setFPS
    ) {
      string[] parts = hostAndPort.Split(':');
      this.host = parts[0];
      this.port = Convert.ToInt32(parts[1]);
      this.socket = new Socket(
        AddressFamily.InterNetwork,
        SocketType.Stream,
        ProtocolType.Tcp
      );
      this.currentPixelColors = new ConcurrentDictionary<Tuple<byte, int>, int>();
      this.firstPixelNotSet = new ConcurrentDictionary<byte, int>();
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
      this.firstPixelNotSet = new ConcurrentDictionary<byte, int>();
    }

    private void Update() {
      lock (this.lockObject) {
        // channel ID => array of bits, one per color
        Dictionary<byte, int[]> colorsPerChannel = new Dictionary<byte, int[]>();
        foreach (var pixelPair in this.firstPixelNotSet) {
          colorsPerChannel[pixelPair.Key] = new int[pixelPair.Value];
        }
        foreach (var pixelColorPair in this.currentPixelColors) {
          colorsPerChannel[pixelColorPair.Key.Item1][pixelColorPair.Key.Item2] = pixelColorPair.Value;
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

    public void Flush() { }

    public void SetPixel(byte channelIndex, int pixelIndex, int color) {
      lock (this.lockObject) {
        this.firstPixelNotSet[channelIndex] = this.firstPixelNotSet.ContainsKey(channelIndex)
          ? Math.Max(this.firstPixelNotSet[channelIndex], pixelIndex + 1)
          : pixelIndex + 1;
        var pixelTuple = new Tuple<byte, int>(channelIndex, pixelIndex);
        this.currentPixelColors[pixelTuple] = color;
      }
    }

  }

}