using System;
using System.Collections.Generic;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Spectrum.Base;
namespace Spectrum {

  public class OrientationInput : Input {
    private readonly Configuration config;
    public Dictionary<int, OrientationDevice> devices;
    private Dictionary<int, long> lastSeen;
    private Thread listenThread;
    private UdpClient listenClient;
    private readonly object mLock = new object();
    private readonly static long DEVICE_TIMEOUT_MS = 1000;

    public OrientationInput(Configuration config) {
      this.config = config;
      devices = new Dictionary<int, OrientationDevice>();
      lastSeen = new Dictionary<int, long>();
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
        // move the device dropping code out of this loop to something that only runs once in a while
        //var currentTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        // Check and drop any devices that haven't been seen for a while
        //foreach (KeyValuePair<int, long> kvp in lastSeen) {
        //  if ((currentTime - kvp.Value) > DEVICE_TIMEOUT_MS) {
        //    lock (mLock) {
        //      devices.Remove(kvp.Key);
        //    }
        //  }
        //}
        var buffer = listenClient.Receive(ref endpoint);
        var deviceId = buffer[0];
        //lastSeen[deviceId] = currentTime;
        var timestamp = BitConverter.ToInt32(buffer, 1);
        short W = BitConverter.ToInt16(buffer, 5);
        short X = BitConverter.ToInt16(buffer, 7);
        short Y = BitConverter.ToInt16(buffer, 9);
        short Z = BitConverter.ToInt16(buffer, 11);
        var actionFlag = buffer[13]; // what the buttons do
        Quaternion sensorState = new Quaternion(X / 16384.0f, Y / 16384.0f, Z / 16384.0f, W / 16384.0f);
        if (devices.ContainsKey(deviceId)) {
          lock (mLock) {
            // action flags take priority:
            if (actionFlag != 0) {
              Console.WriteLine(deviceId);
              // handle them here
            }
            if (timestamp > devices[deviceId].timestamp || timestamp < (devices[deviceId].timestamp - 1000)) {
              // the second conditional is just to catch a case where the device was power cycled; assuming it was off for more than a second
              devices[deviceId].timestamp = timestamp;
              devices[deviceId].currentOrientation = sensorState;
            }
          }
        } else {
          devices.Add(deviceId, new OrientationDevice(timestamp, new Quaternion(0, 0, 0, 1), sensorState));
        }
      }
    }
    public void OperatorUpdate() {
      if (config.orientationCalibrate) {
        // This is when the "Calibrate" button in the UI window is hit
        // Calibrates all devices at once
        // Calibration target is 'forwards' in the y-direction
        lock(mLock) {
          foreach (int id in devices.Keys) {
            devices[id].calibrate();
          }
        }
        config.orientationCalibrate = false;
      }
    }

    public Quaternion deviceRotation(int deviceId) {
      lock (mLock) {
        if (devices.ContainsKey(deviceId)) {
          return devices[deviceId].currentRotation();
        } else {
          return new Quaternion(0, 0, 0, 1);
        }
      }
    }

    public Quaternion deviceCalibration(int deviceId) {
      lock (mLock) {
        if (devices.ContainsKey(deviceId)) {
          return devices[deviceId].calibrationOrigin;
        } else {
          return new Quaternion(0, 0, 0, 1);
        }
      }
    }
  }
}
