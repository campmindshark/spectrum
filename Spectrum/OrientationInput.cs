using System;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Spectrum.Base;
namespace Spectrum {

  public class OrientationInput : Input {
    private readonly Configuration config;
    private Int32 timestamp = 0;
    private Thread listenThread;
    private UdpClient listenClient;
    private readonly object mLock = new object();
    private Quaternion sensorState;
    private Quaternion calibratedOrigin = new Quaternion(0, 0, 0, 1);
    private Quaternion mRotation;

    public OrientationInput(Configuration config) {
      this.config = config;

      listenThread = new Thread(new ThreadStart(Run));
      listenThread.Start();
    }

    public bool Active {
      get {
        return true;
      }
      set { }
    }

    public bool AlwaysActive {
      get {
        return true;
      }
    }

    public bool Enabled {
      get {
        return true;
      }
    }

    private void Run() {
      var endpoint = new IPEndPoint(IPAddress.Any, 5005);
      listenClient = new UdpClient(endpoint);
      while (true) {
        var buffer = listenClient.Receive(ref endpoint);
        var new_timestamp = BitConverter.ToInt32(buffer, 0);
        if (new_timestamp > timestamp || new_timestamp < timestamp - 1000) {
          timestamp = new_timestamp;
          short W = BitConverter.ToInt16(buffer, 4);
          short X = BitConverter.ToInt16(buffer, 6);
          short Y = BitConverter.ToInt16(buffer, 8);
          short Z = BitConverter.ToInt16(buffer, 10);
          lock (mLock) {
            sensorState = new Quaternion(X / 16384.0f, Y / 16384.0f, Z / 16384.0f, W / 16384.0f);
            mRotation = Quaternion.Multiply(calibratedOrigin, Quaternion.Inverse(sensorState));
          }
        }
      }
    }

    public void OperatorUpdate() {
      if (config.orientationCalibrate) {
        // calibration always happens when facing directly 'forwards' in the y-direction
        lock(mLock) {
          calibratedOrigin = sensorState;
        }
        config.orientationCalibrate = false;
      }
    }

    public Quaternion rotation {
      get { lock(mLock) { return mRotation;} }
      private set {}
    }
    public Quaternion calibration {
      get { lock(mLock) { return calibratedOrigin; } }
      private set {}
    }
  }
}
