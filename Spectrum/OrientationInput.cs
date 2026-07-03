using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Spectrum.Base;
using System.Linq;

namespace Spectrum {

  public class OrientationInput : Input {
    private readonly Configuration config;
    private Dictionary<int, OrientationDevice> devices;
    private long lastCheckedDevices;
    private Dictionary<int, long> lastSeen;
    private long[] lastEvent;
    // Per-device connection-quality stats (arrival rate, jitter, packet count),
    // accumulated on the receive thread under mLock alongside the device map.
    private Dictionary<int, DeviceStats> stats;

    // High-resolution monotonic clock for arrival timing. DateTime.Now (used for
    // the 1s device timeout) is too coarse to measure jitter on a stream that
    // arrives every few ms, so the stats use Stopwatch ticks instead.
    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;
    private static double NowMillis() => Stopwatch.GetTimestamp() * TicksToMs;
    private readonly object mLock = new object();
    // Guards the listener lifecycle (mUdpClient/active) independently of mLock,
    // which protects the device state read on the receive thread.
    private readonly object lifecycleLock = new object();
    private UdpClient mUdpClient;
    private readonly static int DEVICE_LISTEN_PORT = 5005;
    private readonly static long DEVICE_TIMEOUT_MS = 1000;
    private readonly static long DEVICE_EVENT_TIMEOUT = 5;

    public OrientationInput(Configuration config) {
      this.config = config;
      devices = new Dictionary<int, OrientationDevice>();
      lastCheckedDevices = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      lastSeen = new Dictionary<int, long>();
      lastEvent = new long[256];
      stats = new Dictionary<int, DeviceStats>();
    }

    private bool active;
    public bool Active {
      get {
        lock (lifecycleLock) {
          return active;
        }
      }
      // The UDP listener now follows Active (driven by the operator) instead of
      // running for the whole process lifetime: it starts when activated and the
      // UdpClient is disposed when deactivated.
      set {
        lock (lifecycleLock) {
          if (active == value) {
            return;
          }
          active = value;
          if (value) {
            StartListening();
          } else {
            StopListening();
          }
        }
      }
    }

    // Must be called while holding lifecycleLock.
    private void StartListening() {
      mUdpClient = new UdpClient(new IPEndPoint(IPAddress.Any, DEVICE_LISTEN_PORT));
      mUdpClient.BeginReceive(ReceiveCallback, mUdpClient);
    }

    // Must be called while holding lifecycleLock. Closing disposes the client; a
    // pending ReceiveCallback then completes with ObjectDisposedException, which
    // it swallows, so the receive loop stops cleanly.
    private void StopListening() {
      UdpClient client = mUdpClient;
      mUdpClient = null;
      client?.Close();
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
      UdpClient client = (UdpClient)ar.AsyncState;
      byte[] buffer;
      var remote = new IPEndPoint(IPAddress.Any, 0);
      try {
        buffer = client.EndReceive(ar, ref remote);
      } catch (ObjectDisposedException) {
        // The client was closed in StopListening(); stop the receive loop.
        return;
      } catch (SocketException) {
        // A prior send was rejected (e.g. ICMP port-unreachable). Re-arm and
        // keep listening.
        RearmReceive(client);
        return;
      }

      ProcessDatagram(buffer);
      RearmReceive(client);
    }

    private void RearmReceive(UdpClient client) {
      try {
        client.BeginReceive(ReceiveCallback, client);
      } catch (ObjectDisposedException) {
        // Deactivated between datagrams; nothing more to do.
      }
    }

    private void ProcessDatagram(byte[] buffer) {
      // This is an unauthenticated UDP listener on 0.0.0.0, so any short or
      // spoofed datagram must be ignored rather than indexed blindly.
      if (buffer.Length < DatagramHandler.MinDatagramLength) {
        return;
      }
      var deviceId = buffer[0];
      var timestamp = BitConverter.ToInt32(buffer, 1);
      int deviceType = buffer[5];
      if (buffer.Length < DatagramHandler.RequiredLength(deviceType)) {
        return;
      }

      var currentTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      var arrivalMs = NowMillis();

      // Datagram unpacking
      var datagramOut = DatagramHandler.parseDatagram(buffer);
      int actionFlag = datagramOut.actionFlag;

      // All access to the shared device state must be under mLock, since the
      // operator thread reads/removes from these collections concurrently.
      lock (mLock) {
        lastSeen[deviceId] = currentTime;

        // Connection-quality accounting. Uses the high-res arrival time and the
        // device's own send timestamp (buffer[1..4], ms) as the reference clock
        // for jitter; both are valid even for an as-yet-unknown deviceType.
        if (!stats.TryGetValue(deviceId, out var deviceStats)) {
          deviceStats = new DeviceStats();
          stats[deviceId] = deviceStats;
        }
        deviceStats.RecordArrival(arrivalMs, timestamp, buffer.Length);

        // Device state update
        if (devices.TryGetValue(deviceId, out var device)) {
          if (actionFlag != 0) {
            // debounce (per device!)
            if (currentTime - lastEvent[deviceId] > DEVICE_EVENT_TIMEOUT) {
              lastEvent[deviceId] = currentTime;
              // A button press means someone is holding the wand, even if it
              // isn't rotating.
              device.NoteActivity(currentTime);
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
            device.RecordMotion(
              datagramOut.device.currentOrientation,
              timestamp - device.timestamp,
              currentTime);
            device.timestamp = timestamp;
            device.currentOrientation = datagramOut.device.currentOrientation;
            // This took me a while to track down. We must set the avgDistanceShort from the datagram
            device.avgDistanceShort = datagramOut.device.avgDistanceShort;
          } else {
            // Duplicate/stale timestamp: no orientation to score, but the
            // moving flag still has to decay.
            device.RefreshMoving(currentTime);
          }
        } else {
          datagramOut.device.NoteActivity(currentTime);
          devices.Add(deviceId, datagramOut.device);
        }
      }
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
              devices.Remove(kvp.Key);
              stats.Remove(kvp.Key);
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

    // Returns a thread-safe deep copy of the current device map, taken under
    // mLock. Callers (e.g. visualizers on the operator thread) get fully
    // independent device objects, so they can read device fields off-thread
    // without racing the receive thread's field writes or enumeration.
    public Dictionary<int, OrientationDevice> DevicesSnapshot() {
      lock (mLock) {
        var snapshot = new Dictionary<int, OrientationDevice>(devices.Count);
        foreach (var kvp in devices) {
          snapshot[kvp.Key] = kvp.Value.Clone();
        }
        return snapshot;
      }
    }

    // Thread-safe snapshot of per-device connection-quality stats, taken under
    // mLock. Keyed by the same device ids as DevicesSnapshot. MillisSinceLast is
    // computed against the current time so the "staleness" is fresh at call.
    public Dictionary<int, OrientationDeviceStats> ConnectionStatsSnapshot() {
      var nowMs = NowMillis();
      lock (mLock) {
        var snapshot = new Dictionary<int, OrientationDeviceStats>(stats.Count);
        foreach (var kvp in stats) {
          snapshot[kvp.Key] = kvp.Value.Snapshot(nowMs);
        }
        return snapshot;
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

    // Mutable per-device accumulator, only ever touched on the receive thread
    // under mLock. Tracks a smoothed inter-arrival interval (→ update rate), an
    // RFC 3550-style interarrival jitter estimate, a smoothed payload byte rate
    // (→ data rate), and a packet-loss estimate derived from the device's own
    // send timestamps.
    private class DeviceStats {
      // RFC 3550 jitter smoothing gain (J += (|D| - J)/16).
      private const double JitterGain = 1.0 / 16.0;
      // EWMA gain for the mean inter-arrival interval.
      private const double IntervalGain = 0.1;
      // EWMA gain for the mean payload size (→ data rate).
      private const double PacketBytesGain = 0.1;

      private bool primed;
      private double lastArrivalMs;
      private int lastDeviceTimestamp;
      private double meanIntervalMs;
      private double jitterMs;
      private long packetCount;
      private double meanPacketBytes;

      // Packet-loss accounting in the device's own timestamp domain (ms since
      // the wand powered on). minDeviceIntervalMs is the smallest gap seen
      // between two consecutive received packets, taken as the wand's true send
      // period; receivedInWindow/missingInWindow accumulate the received and
      // (inferred) dropped packet counts since priming or the last clock reset.
      private double minDeviceIntervalMs = double.MaxValue;
      private long receivedInWindow;
      private long missingInWindow;

      public void RecordArrival(
        double arrivalMs, int deviceTimestamp, int byteCount) {
        packetCount++;
        if (!primed) {
          // First packet only establishes the baseline; no interval yet.
          primed = true;
          lastArrivalMs = arrivalMs;
          lastDeviceTimestamp = deviceTimestamp;
          receivedInWindow = 1;
          meanPacketBytes = byteCount;
          return;
        }

        double hostInterval = arrivalMs - lastArrivalMs;
        int deviceInterval = deviceTimestamp - lastDeviceTimestamp;
        lastArrivalMs = arrivalMs;
        lastDeviceTimestamp = deviceTimestamp;

        meanIntervalMs = meanIntervalMs == 0
          ? hostInterval
          : meanIntervalMs + (hostInterval - meanIntervalMs) * IntervalGain;
        meanPacketBytes += (byteCount - meanPacketBytes) * PacketBytesGain;

        if (deviceInterval < 0) {
          // The device clock ran backwards: the wand was power-cycled (its
          // timestamp is ms since power-on) or a datagram arrived badly out of
          // order. Restart the loss window so a clock reset isn't scored as a
          // huge burst of dropped packets.
          receivedInWindow = 1;
          missingInWindow = 0;
          return;
        }
        receivedInWindow++;

        // The wand sends on a fixed cadence, so the smallest device-timestamp
        // gap between consecutive packets is that send period. Track it, then
        // score any larger gap as dropped packets: a gap rounding to k send
        // periods means k-1 packets went missing between the two we received.
        // Rounding makes this robust to small send-clock jitter — a near-period
        // gap snaps to k=1 (no loss). Skip until a period is known.
        if (deviceInterval > 0 && deviceInterval < minDeviceIntervalMs) {
          minDeviceIntervalMs = deviceInterval;
        }
        if (minDeviceIntervalMs != double.MaxValue) {
          long slots = (long)Math.Round(deviceInterval / minDeviceIntervalMs);
          if (slots > 1) {
            missingInWindow += slots - 1;
          }
        }

        // Interarrival jitter: the variation in network transit time, taking
        // the device's own send timestamps as the reference clock. Skip
        // degenerate deltas (>1000ms, already handled as a reset above) so a
        // clock jump doesn't spike the estimate.
        if (deviceInterval > 0 && deviceInterval < 1000) {
          double transitDelta = hostInterval - deviceInterval;
          jitterMs += (Math.Abs(transitDelta) - jitterMs) * JitterGain;
        }
      }

      public OrientationDeviceStats Snapshot(double nowMs) {
        double rateHz = meanIntervalMs > 0 ? 1000.0 / meanIntervalMs : 0.0;
        long sent = receivedInWindow + missingInWindow;
        double lossFraction = sent > 0 ? (double)missingInWindow / sent : 0.0;
        return new OrientationDeviceStats(
          rateHz,
          meanIntervalMs,
          jitterMs,
          packetCount,
          primed ? nowMs - lastArrivalMs : 0.0,
          lossFraction,
          rateHz * meanPacketBytes
        );
      }
    }
  }

  // Immutable snapshot of one device's connection-quality stats. Value type so
  // callers read a consistent set of numbers with no shared mutable state.
  public readonly struct OrientationDeviceStats {
    public double UpdateRateHz { get; }
    public double MeanIntervalMs { get; }
    public double JitterMs { get; }
    public long PacketCount { get; }
    public double MillisSinceLastPacket { get; }
    // Estimated fraction (0..1) of the device's sent packets that never
    // arrived, derived from its own send-timestamp cadence.
    public double PacketLossFraction { get; }
    // Smoothed received payload throughput, in bytes per second.
    public double DataRateBytesPerSec { get; }

    public OrientationDeviceStats(
      double updateRateHz,
      double meanIntervalMs,
      double jitterMs,
      long packetCount,
      double millisSinceLastPacket,
      double packetLossFraction,
      double dataRateBytesPerSec
    ) {
      this.UpdateRateHz = updateRateHz;
      this.MeanIntervalMs = meanIntervalMs;
      this.JitterMs = jitterMs;
      this.PacketCount = packetCount;
      this.MillisSinceLastPacket = millisSinceLastPacket;
      this.PacketLossFraction = packetLossFraction;
      this.DataRateBytesPerSec = dataRateBytesPerSec;
    }
  }
}
