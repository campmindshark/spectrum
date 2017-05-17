using System.IO.Ports;
using Spectrum.Base;
using System.Threading;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System;

namespace Spectrum.LEDs {

  /**
   * TeensyAPI is an API that can handle a single Teensy. It has no conception
   * of how many LEDs the Teensy is addressing - it just communicates a given
   * index and color to the Teensy.
   */
  class TeensyAPI {

    private SerialPort port;
    private ConcurrentQueue<byte[]> buffer;
    private bool separateThread;
    private Action<int> setFPS;
    private Stopwatch frameRateStopwatch;
    private int framesThisSecond;

    public TeensyAPI(
      string portName,
      bool separateThread,
      Action<int> setFPS
    ) {
      this.port = new SerialPort(portName);
      this.buffer = new ConcurrentQueue<byte[]>();
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
              this.outputThread = new Thread(OutputThread);
              this.outputThread.Start();
            } else {
              this.OpenPort();
            }
          } else {
            if (this.outputThread != null) {
              this.outputThread.Abort();
              this.outputThread.Join();
              this.outputThread = null;
            } else {
              this.ClosePort();
            }
          }
          this.active = value;
        }
      }
    }

    private void OpenPort() {
      this.port.Open();
    }

    private void ClosePort() {
      this.port.Close();
      this.buffer = new ConcurrentQueue<byte[]>();
    }

    private void Update() {
      lock (this.lockObject) {
        int numMessages = this.buffer.Count;
        if (numMessages == 0) {
          return;
        }
        byte[][] messages = new byte[numMessages][];
        for (int i = 0; i < numMessages; i++) {
          bool result = this.buffer.TryDequeue(out messages[i]);
          if (!result) {
            throw new System.Exception("Someone else is dequeueing!");
          }
        }
        byte[] bytes = messages.SelectMany(a => a).ToArray();
        int num_bytes = messages.Sum(a => a.Length);

        try {
          this.port.Write(bytes, 0, num_bytes);
        } catch (Exception) { }
      }
    }

    public void OperatorUpdate() {
      if (!this.separateThread) {
        this.Update();
      }
    }

    private void OutputThread() {
      this.OpenPort();
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
        this.ClosePort();
      }
    }

    public void Flush() {
      this.buffer.Enqueue(new byte[] { 0, 0, 0, 0, 0 });
    }

    public void SetPixel(int pixelIndex, int color) {
      int message = pixelIndex + 1;
      this.buffer.Enqueue(new byte[] {
        (byte)message,
        (byte)(message >> 8),
        (byte)color,
        (byte)(color >> 8),
        (byte)(color >> 16),
      });
    }

  }

}