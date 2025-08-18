using System;
using System.Collections.Generic;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Spectrum.Base;
using System.Linq;

namespace Spectrum {

  public class OrientationInput : Input {
    private readonly Configuration config;
    public Dictionary<int, OrientationDevice> devices;
    private long lastCheckedDevices;
    private Dictionary<int, long> lastSeen;
    private long[] lastEvent;
    private Thread listenThread;
    private readonly object mLock = new object();
    private IPEndPoint mEndpoint;
    private readonly UdpClient mUdpClient;
    private readonly static int DEVICE_LISTEN_PORT = 5005;
    private readonly static long DEVICE_TIMEOUT_MS = 1000;
    private readonly static long DEVICE_EVENT_TIMEOUT = 5;
    private int n_poi = 0;

    public OrientationInput(Configuration config) {
      this.config = config;
      devices = new Dictionary<int, OrientationDevice>();
      lastCheckedDevices = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      lastSeen = new Dictionary<int, long>();
      lastEvent = new long[255];
      mEndpoint = new IPEndPoint(IPAddress.Any, DEVICE_LISTEN_PORT);
      mUdpClient = new UdpClient(mEndpoint);
      mUdpClient.BeginReceive(ReceiveCallback, null);
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

    private void ReceiveCallback(IAsyncResult ar) {
      byte[] buffer = mUdpClient.EndReceive(ar, ref mEndpoint);
      var deviceId = buffer[0];
      var timestamp = BitConverter.ToInt32(buffer, 1);

      var currentTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      lastSeen[deviceId] = currentTime;

      // Datagram unpacking
      var datagramOut = DatagramHandler.parseDatagram(buffer);
      int actionFlag = datagramOut.actionFlag;

      // Device state update
      if (devices.TryGetValue(deviceId, out var device)) {
        if (actionFlag != 0) {
          // debounce (per device!)
          if (currentTime - lastEvent[deviceId] > DEVICE_EVENT_TIMEOUT) {
            lastEvent[deviceId] = currentTime;
            if (actionFlag == 4) {
              device.calibrate();
            } else if (actionFlag == 1 || actionFlag == 2 || actionFlag == 3) {
              device.actionFlag = actionFlag;
            }
          }
        } else {
          device.actionFlag = 0;
        }
        if (timestamp > device.timestamp || timestamp < (device.timestamp - 1000)) {
          // the second conditional is just to catch a case where the device was power cycled;
          //   assuming it was off for more than a second
          device.timestamp = timestamp;
          device.currentOrientation = datagramOut.device.currentOrientation;
          // This took me a while to track down. We must set the avgDistanceShort from the datagram
          device.avgDistanceShort = datagramOut.device.avgDistanceShort;
        }
      } else {
        lock (mLock) {
          devices.Add(deviceId, datagramOut.device);
          if (devices[deviceId].deviceType == 2) {
            n_poi++;
          }
        }
      }
      mUdpClient.BeginReceive(new AsyncCallback(ReceiveCallback), null);
    }
    public void OperatorUpdate() {
      if (config.orientationCalibrate) {
        // This is when the "Calibrate" button in the UI window is hit
        // Calibrates all devices at once
        // Calibration target is 'forwards' in the y-direction
        lock(mLock) {
          foreach (var device in devices.Values) {
            device.calibrate();
          }
        }
        config.orientationCalibrate = false;
      }

      // Disabled device removal - can we run this less often?
      var currentTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      if ((currentTime - lastCheckedDevices) > DEVICE_TIMEOUT_MS) {
        lock (mLock) {
          List<int> removedDevices = new List<int>();
          foreach (KeyValuePair<int, long> kvp in lastSeen) {
            if ((currentTime - kvp.Value) > DEVICE_TIMEOUT_MS) {
              if (devices[kvp.Key].deviceType == 2) {
                n_poi--;
              }
              devices.Remove(kvp.Key);
              removedDevices.Add(kvp.Key);
           }
          }
          foreach (int deviceId in removedDevices) {
            lastSeen.Remove(deviceId);
          }
        }
        lastCheckedDevices = currentTime;
      }
    }

    public Quaternion deviceRotation(int deviceId) {
      lock (mLock) {
        return devices.TryGetValue(deviceId, out var device) ? device.currentRotation() : new Quaternion(0, 0, 0, 1);
      }
    }

    public Quaternion deviceCalibration(int deviceId) {
      lock (mLock) {
        return devices.TryGetValue(deviceId, out var device) ? device.calibrationOrigin : new Quaternion(0, 0, 0, 1);
      }
    }

    // onlyPoi is used to change visualization to accentuate poi
    public bool onlyPoi() {
      // All devices are not poi if there are no devices.
      if (devices.Count == 0) {
        return false;
      }
      return n_poi == devices.Count;
    }
  }
}
