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
    private readonly IRuntimeSettingsConfiguration runtimeSettings;
    private readonly ApplicationStateDispatcher stateDispatcher;
    private readonly bool connectHardware;
    private bool calibrationHandled;
    private int spotlightClearPending;
    private Dictionary<int, OrientationDevice> devices;
    private long lastCheckedDevices;
    private Dictionary<int, long> lastSeen;
    private long[] lastEvent;
    // One deep device snapshot published by the operator thread after input
    // updates each frame. Visualizers share this immutable-by-convention view
    // instead of each cloning the device map and contending with the receive
    // thread independently.
    private IReadOnlyDictionary<int, OrientationDevice> operatorFrameDevices =
      new Dictionary<int, OrientationDevice>();
    private long operatorFrameGeneration;
    private DomeRuntimeFrameSnapshot operatorFrameRuntime =
      DomeRuntimeFrameSnapshot.Empty;
    private long operatorSnapshotGeneration = -1;
    // Per-device connection-quality stats (arrival rate, jitter, packet count),
    // accumulated by the receive threads (UDP callback + serial worker),
    // serialized by mLock alongside the device map.
    private Dictionary<int, DeviceStats> stats;

    // Wands also reach us over USB-CDC serial from the ESP-NOW receiver, fed
    // into this same sink via ProcessDatagram. Runs additively with the UDP
    // listener; each remote uses exactly one transport.
    public WandSerialReceiver WandSerial { get; }

    // High-resolution monotonic clock for arrival timing. DateTime.Now (used for
    // the 1s device timeout) is too coarse to measure jitter on a stream that
    // arrives every few ms, so the stats use Stopwatch ticks instead.
    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;
    private static double NowMillis() => Stopwatch.GetTimestamp() * TicksToMs;
    private readonly object mLock = new object();
    // Guards the listener lifecycle (mUdpClient/active) independently of mLock,
    // which protects the device state read on the receive thread.
    private readonly object lifecycleLock = new object();
    private UdpClient? mUdpClient;
    private readonly static int DEVICE_LISTEN_PORT = 5005;
    private readonly static long DEVICE_TIMEOUT_MS = 1000;
    private readonly static long DEVICE_EVENT_TIMEOUT = 5;

    // The wand remotes' radio firmware transmits at a hard ceiling of 200 Hz
    // (one packet every 5 ms); no device can physically send faster. This is
    // the reference "full rate" the connection-quality diagnostics score the
    // measured update rate against, and the physical floor for the inferred
    // send period the packet-loss estimator uses. Kept public so the wand
    // status view (WandRow) reads the same number; the web surface duplicates
    // it in wands.js with a "must agree" note, as it already does for the other
    // quality thresholds.
    public const double WandMaxTransmitRateHz = 200.0;
    private const double WandMinSendIntervalMs = 1000.0 / WandMaxTransmitRateHz;

    public OrientationInput(
      Configuration config,
      ApplicationStateDispatcher stateDispatcher
    ) : this(config, stateDispatcher, true) {
    }

    // Keeps UDP and serial disconnected for the integrated operator test while
    // retaining the normal datagram parser and operator-frame snapshot path.
    internal OrientationInput(
      Configuration config,
      ApplicationStateDispatcher stateDispatcher,
      bool connectHardware
    ) {
      this.config = config;
      this.runtimeSettings = config as IRuntimeSettingsConfiguration ??
        throw new ArgumentException(
          "OrientationInput requires immutable runtime settings.",
          nameof(config));
      this.stateDispatcher = stateDispatcher ??
        throw new ArgumentNullException(nameof(stateDispatcher));
      this.connectHardware = connectHardware;
      devices = new Dictionary<int, OrientationDevice>();
      lastCheckedDevices = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      lastSeen = new Dictionary<int, long>();
      lastEvent = new long[256];
      stats = new Dictionary<int, DeviceStats>();
      WandSerial = new WandSerialReceiver(config, this);
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
          if (this.connectHardware) {
            if (value) {
              StartListening();
            } else {
              StopListening();
            }
            // The serial receiver follows Active too. This is only a flag write
            // + signal (never a blocking port call), so it's safe under
            // lifecycleLock.
            WandSerial.SetActive(value);
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
      UdpClient? client = mUdpClient;
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
      if (ar.AsyncState is not UdpClient client) {
        return;
      }
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

    // Shared sink for both transports: the UDP ReceiveCallback and the serial
    // worker thread call this directly. Everything below runs under mLock, so
    // the two producers are serialized. (The serial receiver filters its own
    // deviceType-5 heartbeats out before calling here — a heartbeat is not a
    // device datagram.)
    public void ProcessDatagram(byte[] buffer) {
      // This is an unauthenticated UDP listener on 0.0.0.0 (and an equally
      // untrusted serial stream), so any short or spoofed datagram must be
      // ignored rather than indexed blindly. TryReadHeader also classifies the
      // two header layouts (legacy vs. the seq-carrying types 5/6), so the
      // timestamp and deviceType below are read from the correct offsets.
      if (!DatagramHandler.TryReadHeader(buffer, out var header)) {
        return;
      }
      var deviceId = header.DeviceId;
      var timestamp = header.Timestamp;
      int deviceType = header.DeviceType;
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
        // for jitter; both are valid even for an as-yet-unknown deviceType. The
        // header's uint8 packet sequence number (header.Sequence, or -1 for the
        // legacy layout that carries none) drives the packet-loss estimate.
        if (!stats.TryGetValue(deviceId, out var deviceStats)) {
          deviceStats = new DeviceStats();
          stats[deviceId] = deviceStats;
        }
        deviceStats.RecordArrival(
          arrivalMs, timestamp, buffer.Length, header.Sequence);

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
      OrientationSettingsSnapshot settings =
        this.runtimeSettings.OrientationSettingsSnapshot;
      if (settings.Calibrate && !this.calibrationHandled) {
        this.calibrationHandled = true;
        // This is when the "Calibrate" button in the UI window is hit
        // Calibrates all devices at once
        // Calibration target is 'forwards' in the y-direction
        lock(mLock) {
          foreach (var device in devices.Values) {
            device.calibrate();
          }
        }
        this.stateDispatcher.Post(
          () => config.orientationCalibrate = false);
      } else if (!settings.Calibrate) {
        this.calibrationHandled = false;
      }

      // Disabled device removal - can we run this less often?
      var currentTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      if ((currentTime - lastCheckedDevices) > DEVICE_TIMEOUT_MS) {
        List<int> removedDevices = new List<int>();
        lock (mLock) {
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
        // If the wand currently chosen as the orientation "spotlight" has
        // disconnected, clear the choice back to -1 so the dome renders every
        // connected wand again rather than a device that is no longer present.
        // Done outside mLock so PropertyChanged isn't raised under the device
        // lock. -1/-2 never match a real device id, so idle/all stay untouched.
        int disconnectedSpotlight = settings.SpotlightDeviceId;
        if (removedDevices.Contains(disconnectedSpotlight) &&
            Interlocked.CompareExchange(
              ref this.spotlightClearPending, 1, 0) == 0) {
          this.stateDispatcher.Post(() => {
            try {
              // Preserve a newer UI/web selection made while this command was
              // waiting on the dispatcher.
              if (this.runtimeSettings.OrientationSettingsSnapshot
                  .SpotlightDeviceId == disconnectedSpotlight) {
                config.orientationDeviceSpotlight = -1;
              }
            } finally {
              Volatile.Write(ref this.spotlightClearPending, 0);
            }
          });
        }
        lastCheckedDevices = currentTime;
      }
    }

    // Called exactly once by Operator after all input updates and before any
    // visualizer runs. The snapshot itself is created lazily so a scene with no
    // orientation consumers pays nothing; all consumers in a generation share
    // the same cloned devices.
    public void BeginOperatorFrame(DomeRuntimeFrameSnapshot? runtime = null) {
      this.operatorFrameRuntime = runtime ??
        this.runtimeSettings.DomeRuntimeFrameSnapshot;
      this.operatorFrameGeneration++;
    }

    public DomeRuntimeFrameSnapshot OperatorFrameRuntime =>
      this.operatorFrameRuntime;

    public IReadOnlyDictionary<int, OrientationDevice> OperatorFrameDevices {
      get {
        if (this.operatorSnapshotGeneration != this.operatorFrameGeneration) {
          this.operatorFrameDevices = this.DevicesSnapshot();
          this.operatorSnapshotGeneration = this.operatorFrameGeneration;
        }
        return this.operatorFrameDevices;
      }
    }

    public long OperatorFrameGeneration => this.operatorFrameGeneration;

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

    // Mutable per-device accumulator, only ever touched on the receive threads
    // (UDP callback + serial worker), serialized by mLock. Tracks a smoothed
    // inter-arrival interval (→ update rate), an
    // RFC 3550-style interarrival jitter estimate, a smoothed payload byte rate
    // (→ data rate), and a packet-loss estimate. Loss is counted directly from
    // the uint8 packet sequence number for devices that carry one (wand v3 /
    // type 6); devices with no sequence number (poi, legacy wands) fall back to
    // inferring loss from the device's own send-timestamp cadence.
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

      // Packet-loss accounting. receivedInWindow/missingInWindow accumulate the
      // received and (counted or inferred) dropped packet counts since priming
      // or the last clock reset; PacketLossFraction is missing/(received+missing).
      private long receivedInWindow;
      private long missingInWindow;

      // Sequence-number path (wand v3 / type 6): the wand stamps every packet
      // with a uint8 counter that increments by one per send and wraps at 256,
      // so the signed 8-bit gap between consecutive received sequence numbers is
      // exactly the number of packets sent in between — no send period to infer.
      // hasSequence latches on the first packet that carries one (>= 0).
      private bool hasSequence;
      private int lastSequence;

      // Timestamp-cadence fallback (poi / legacy wands, no sequence number).
      // minDeviceIntervalMs is the smallest gap seen between two consecutive
      // received packets in the device's own timestamp domain (ms since power-on),
      // taken as the wand's true send period so a larger gap can be scored as
      // dropped packets.
      private double minDeviceIntervalMs = double.MaxValue;

      public void RecordArrival(
        double arrivalMs, int deviceTimestamp, int byteCount, int sequence) {
        packetCount++;
        if (!primed) {
          // First packet only establishes the baseline; no interval yet.
          primed = true;
          lastArrivalMs = arrivalMs;
          lastDeviceTimestamp = deviceTimestamp;
          receivedInWindow = 1;
          meanPacketBytes = byteCount;
          hasSequence = sequence >= 0;
          lastSequence = sequence;
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
          // huge burst of dropped packets, and re-baseline the sequence counter
          // (a power cycle also resets it to 0).
          receivedInWindow = 1;
          missingInWindow = 0;
          lastSequence = sequence;
          return;
        }
        receivedInWindow++;

        if (hasSequence) {
          // The sequence number counts sent packets directly. A signed 8-bit
          // delta absorbs the uint8 wrap: an in-order packet advances by 1 (no
          // loss), a forward gap of d means d-1 packets dropped, and a zero or
          // negative delta is a duplicate or a late-arriving reorder — no new
          // packet to charge as loss. Only advance the baseline on forward
          // progress, so a straggler doesn't rewind it and manufacture a huge
          // phantom gap on the next in-order packet.
          int delta = (sbyte)(sequence - lastSequence);
          if (delta > 1) {
            missingInWindow += delta - 1;
          }
          if (delta > 0) {
            lastSequence = sequence;
          }
        } else {
          // No sequence number: fall back to inferring loss from the send
          // cadence. The wand sends on a fixed cadence, so the smallest
          // device-timestamp gap between consecutive packets is that send
          // period. Track it, then score any larger gap as dropped packets: a
          // gap rounding to k send periods means k-1 packets went missing
          // between the two we received. Rounding makes this robust to small
          // send-clock jitter — a near-period gap snaps to k=1 (no loss). Skip
          // until a period is known.
          if (deviceInterval > 0 && deviceInterval < minDeviceIntervalMs) {
            minDeviceIntervalMs = deviceInterval;
          }
          if (minDeviceIntervalMs != double.MaxValue) {
            // The wand can't send faster than the 200 Hz cap, so integer-ms
            // timestamp quantization occasionally reporting a sub-5ms gap is
            // an artifact, not the true period. Clamp the inferred period up to
            // the physical floor before scoring, so a normal ~3ms gap doesn't
            // round to "one packet missing" off a spuriously small
            // minDeviceIntervalMs.
            double sendPeriodMs =
              Math.Max(minDeviceIntervalMs, WandMinSendIntervalMs);
            long slots = (long)Math.Round(deviceInterval / sendPeriodMs);
            if (slots > 1) {
              missingInWindow += slots - 1;
            }
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
    // Fraction (0..1) of the device's sent packets that never arrived, counted
    // from the uint8 packet sequence number where the device carries one and
    // otherwise inferred from its own send-timestamp cadence.
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
