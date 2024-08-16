﻿using System;
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
    private readonly static long DEVICE_TIMEOUT_MS = 5000;
    private readonly static long DEVICE_EVENT_TIMEOUT = 50;
    private int n_poi = 0;

    public OrientationInput(Configuration config) {
      this.config = config;
      devices = new Dictionary<int, OrientationDevice>();
      lastCheckedDevices = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      lastSeen = new Dictionary<int, long>();
      lastEvent = new long[255];
      listenThread = new Thread(new ThreadStart(Run));
      listenThread.Start();
    }
    public struct UdpState {
      public UdpClient u;
      public IPEndPoint e;
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
      UdpClient u = ((UdpState)(ar.AsyncState)).u;
      IPEndPoint e = ((UdpState)(ar.AsyncState)).e;
      UdpState s = new UdpState();
      s.e = e;
      s.u = u;
      byte[] buffer = u.EndReceive(ar, ref e);
      var deviceId = buffer[0];
      var timestamp = BitConverter.ToInt32(buffer, 1);

      var currentTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      lastSeen[deviceId] = currentTime;

      // Datagram unpacking
      var datagramOut = DatagramHandler.parseDatagram(buffer);
      int actionFlag = datagramOut.actionFlag;

      // Device state update
      if (!devices.ContainsKey(deviceId)) {
        devices.Add(deviceId, datagramOut.device);
        if (devices[deviceId].deviceType == 2) {
          n_poi++;
        } else {
          n_poi--;
        }
      } else {
        if (actionFlag != 0) {
          // debounce (per device!)
          if (currentTime - lastEvent[deviceId] > DEVICE_EVENT_TIMEOUT) {
            lastEvent[deviceId] = currentTime;
            if (actionFlag == 4) {
              devices[deviceId].calibrate();
            } else if (actionFlag == 1 || actionFlag == 2 || actionFlag == 3) {
              devices[deviceId].actionFlag = actionFlag;
            }
          }
        } else {
          devices[deviceId].actionFlag = 0;
        }
        if (timestamp > devices[deviceId].timestamp || timestamp < (devices[deviceId].timestamp - 1000)) {
          // the second conditional is just to catch a case where the device was power cycled;
          //   assuming it was off for more than a second
          devices[deviceId].timestamp = timestamp;
          devices[deviceId].currentOrientation = datagramOut.device.currentOrientation;
          // This took me a while to track down. We must set the avgDistanceShort from the datagram
          devices[deviceId].avgDistanceShort = datagramOut.device.avgDistanceShort;
        }
      }
      u.BeginReceive(new AsyncCallback(ReceiveCallback), s);
    }
    private void Run() {
      IPEndPoint e = new IPEndPoint(IPAddress.Any, 5005);
      UdpClient u = new UdpClient(e);

      UdpState s = new UdpState();
      s.e = e;
      s.u = u;
      u.BeginReceive(new AsyncCallback(ReceiveCallback), s);
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

      // Disabled device removal - can we run this less often?
      var currentTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      if ((currentTime - lastCheckedDevices) > DEVICE_TIMEOUT_MS) {
        List<int> removedDevices = new List<int>();
        foreach (KeyValuePair<int, long> kvp in lastSeen) {
          if ((currentTime - kvp.Value) > DEVICE_TIMEOUT_MS) {
            if (devices[kvp.Key].deviceType == 2) {
              n_poi--;
            } else {
              n_poi++;
            }
            devices.Remove(kvp.Key);
            removedDevices.Add(kvp.Key);
          }
        }
        foreach (int deviceId in removedDevices) {
          lastSeen.Remove(deviceId);
        }
        lastCheckedDevices = currentTime;
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
