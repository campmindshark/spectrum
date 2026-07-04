using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Threading;
using Spectrum.Base;

namespace Spectrum {

  // Reads wand structs off the ESP32 receiver's USB-CDC serial port and feeds
  // them into the same OrientationInput sink as the UDP listener.
  //
  // The receiver frames each wand struct as
  //   COBS(struct ‖ CRC8(struct)) ‖ 0x00
  // and also emits a 500 ms deviceType-5 heartbeat so we can distinguish
  // "receiver up, no wands transmitting" from "receiver dead/unplugged".
  //
  // This runs ADDITIVELY alongside the UDP path: each remote is configured for
  // exactly one transport, so the two paths feed disjoint deviceIds into the
  // shared device map and there is no double-counting.
  //
  // Modeled on ProDjLinkHandler's self-managing lifecycle, with one deliberate
  // divergence: all blocking serial calls (open/close/read) live on a single
  // persistent worker thread, and shutdown is via ReadTimeout + desired-state
  // flags, NOT by closing the port from another thread. Unlike UdpClient,
  // closing a SerialPort while a thread is blocked in Read is a classic .NET
  // hang / ObjectDisposedException source, so we never do it.
  public class WandSerialReceiver {

    // The receiver is considered alive when the most recent heartbeat OR data
    // frame arrived within this window (~3 missed 500 ms heartbeats). Any data
    // frame counts as liveness, not just the heartbeat — under a full-rate wand
    // stream the firmware may crowd out or suppress heartbeats, and we must not
    // flip to "dead" then. This is the canonical value; both UIs mirror it.
    public const double RECEIVER_ALIVE_MS = 1500;

    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;
    // "Never happened yet" reads as infinitely long ago. We can't put an actual
    // ∞ in the value (Results.Json can't serialize double.PositiveInfinity), so
    // an unset last-seen maps to this large finite sentinel, which both UIs read
    // as "not alive" (>> RECEIVER_ALIVE_MS).
    private const double NeverSentinelMs = 1e9;

    // Serial reads block for at most this long, so the loop re-checks desired
    // state (SetActive / port change) promptly even under a continuous stream.
    private const int ReadTimeoutMs = 200;
    // Backoff after an open failure or port-gone close, so a yanked cable or a
    // stale port name doesn't spin the worker hot.
    private const int RetryWaitMs = 1000;
    // Idle-park wait when there's nothing to open; a liveness/retry backstop
    // only (status is computed from timestamps at snapshot time, not ticked).
    private const int IdleWaitMs = 1000;
    // A single runaway frame with no delimiter is the only failure to guard; a
    // burst of several ~20-byte frames in one Read is fine.
    private const int MaxFrameBytes = 256;

    private readonly Configuration config;
    private readonly OrientationInput sink;

    // Desired state + all status fields are guarded by this lock. The worker
    // thread reads desired state and writes status; callers write desired state
    // and read status. `wake` nudges the worker out of a park/read-retry wait.
    private readonly object stateLock = new object();
    private readonly AutoResetEvent wake = new AutoResetEvent(false);
    private bool desiredActive;
    private string targetPort;

    // Status (all under stateLock). Last-seen timestamps are nullable so "never
    // happened yet" is distinguishable from "0 ms ago".
    private string statusPortName;
    private bool portOpen;
    private long? lastHeartbeatTs;
    private long? lastFrameTs;
    private string lastError;

    public WandSerialReceiver(Configuration config, OrientationInput sink) {
      this.config = config;
      this.sink = sink;
      this.targetPort = config.wandSerialPort;
      this.config.PropertyChanged += this.ConfigUpdated;

      var worker = new Thread(this.WorkerLoop) {
        IsBackground = true,
        Name = "WandSerial",
      };
      worker.Start();
    }

    // Called by OrientationInput.Active. Only a flag write + signal, so it's safe
    // to call under OrientationInput.lifecycleLock and never blocks on the port.
    public void SetActive(bool value) {
      lock (this.stateLock) {
        this.desiredActive = value;
        // Refresh the target so an activate picks up the latest configured port.
        this.targetPort = this.config.wandSerialPort;
      }
      this.wake.Set();
    }

    private void ConfigUpdated(object sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == nameof(this.config.wandSerialPort)) {
        lock (this.stateLock) {
          this.targetPort = this.config.wandSerialPort;
        }
        this.wake.Set();
      }
    }

    public static string[] AvailablePorts() {
      // On Windows GetPortNames() can return stale registry entries; harmless
      // here — a stale pick just fails to open and the worker retries — but it's
      // why the UI keeps a configured-but-absent port as a "(missing)" item.
      return SerialPort.GetPortNames();
    }

    public WandSerialStatus StatusSnapshot() {
      long now = Stopwatch.GetTimestamp();
      lock (this.stateLock) {
        return new WandSerialStatus(
          this.statusPortName,
          this.portOpen,
          MillisSince(this.lastHeartbeatTs, now),
          MillisSince(this.lastFrameTs, now),
          this.lastError
        );
      }
    }

    private static double MillisSince(long? ts, long now) {
      return ts.HasValue ? (now - ts.Value) * TicksToMs : NeverSentinelMs;
    }

    // The worker is a background/daemon thread with no stop flag: nothing disposes
    // this receiver. Deactivation (op.Enabled = false → SetActive(false)) drops
    // desiredOpen, the worker closes the port and parks on `wake`, and the daemon
    // thread then dies with the process. So this loop runs for the app's lifetime.
    private void WorkerLoop() {
      while (true) {
        bool open;
        string port;
        lock (this.stateLock) {
          open = this.desiredActive && !string.IsNullOrEmpty(this.targetPort);
          port = this.targetPort;
        }
        if (!open) {
          this.wake.WaitOne(IdleWaitMs);
          continue;
        }
        this.RunPort(port);
      }
    }

    // Opens `portName`, reads/deframes until the desired state changes or the
    // port fails, then closes on this thread. Returns to WorkerLoop to reevaluate.
    private void RunPort(string portName) {
      SerialPort port;
      try {
        port = new SerialPort(portName, 115200) {
          // USB CDC ignores the baud rate; 115200 is a nominal placeholder.
          Handshake = Handshake.None,
          ReadTimeout = ReadTimeoutMs,
          // DtrEnable = true is MANDATORY, not cosmetic. On an ESP32-S2/S3/C3
          // native USB CDC (TinyUSB) receiver, the firmware's serial writes are
          // discarded or block until the host asserts DTR — so without this the
          // port opens cleanly but delivers ZERO bytes forever, presenting as a
          // permanent "Port open — no data" with no lastError to debug from.
          // This is the single most likely "it opened but nothing comes
          // through" cause. RtsEnable is set too; harmless.
          DtrEnable = true,
          RtsEnable = true,
        };
        port.Open();
        // The driver can hold pre-open stale bytes; the truncated-first-frame
        // handling already tolerates them (they fail CRC), but discarding starts
        // clean.
        port.DiscardInBuffer();
      } catch (Exception e) {
        // Open failure is a port failure → record it and back off. (Not to be
        // confused with per-frame decode/CRC rejects, which never set lastError.)
        lock (this.stateLock) {
          this.lastError = e.Message;
          this.portOpen = false;
          this.statusPortName = portName;
        }
        this.wake.WaitOne(RetryWaitMs);
        return;
      }

      // Opened. Reset the liveness timestamps so a reopened port doesn't
      // momentarily show "alive" off pre-unplug timestamps.
      lock (this.stateLock) {
        this.statusPortName = portName;
        this.portOpen = true;
        this.lastError = null;
        this.lastHeartbeatTs = null;
        this.lastFrameTs = null;
      }

      var scratch = new byte[512];
      // Per-frame accumulator: the bytes since the last 0x00, NOT the whole read.
      var frame = new byte[MaxFrameBytes];
      int frameLen = 0;

      try {
        while (true) {
          // Re-check before every read so SetActive(false) / a port change is
          // honored promptly.
          if (this.TargetChanged(portName)) {
            break;
          }

          int n;
          try {
            n = port.Read(scratch, 0, scratch.Length);
          } catch (TimeoutException) {
            // No data this interval; loop re-checks desired state. Must not gate
            // the recheck on the timeout — a full-rate stream never times out.
            continue;
          } catch (Exception e) {
            // Port-gone (IOException / UnauthorizedAccessException /
            // OperationCanceledException) → record and close.
            lock (this.stateLock) {
              this.lastError = e.Message;
            }
            break;
          }

          if (n == 0) {
            // On some USB-CDC driver stacks an unplug makes Read return 0
            // repeatedly instead of throwing; treat 0 as port-gone so the loop
            // doesn't spin hot "open" with no data.
            lock (this.stateLock) {
              this.lastError = "Read returned no data (device disconnected?)";
            }
            break;
          }

          for (int i = 0; i < n; i++) {
            byte b = scratch[i];
            if (b == 0x00) {
              this.FinalizeFrame(frame, frameLen);
              frameLen = 0;
            } else if (frameLen >= MaxFrameBytes) {
              // Runaway frame with no delimiter: drop and reset.
              frameLen = 0;
            } else {
              frame[frameLen++] = b;
            }
          }

          // Re-check after the read too, so a change mid-stream breaks promptly.
          if (this.TargetChanged(portName)) {
            break;
          }
        }
      } finally {
        // Always close on the worker thread itself, never from a caller.
        try {
          port.Close();
        } catch {
          // Closing can throw on an already-gone device; nothing to do.
        }
        port.Dispose();
        lock (this.stateLock) {
          this.portOpen = false;
        }
      }

      // Back off before a reopen so a yanked cable doesn't spin. A deliberate
      // port change already signaled `wake`, so this returns immediately then.
      this.wake.WaitOne(RetryWaitMs);
    }

    private bool TargetChanged(string portName) {
      lock (this.stateLock) {
        return !this.desiredActive
          || string.IsNullOrEmpty(this.targetPort)
          || this.targetPort != portName;
      }
    }

    // Deframe one delimiter-stripped run: COBS-decode → strip the CRC byte →
    // validate length and CRC → dispatch. A failed decode/CRC is expected (the
    // first frame after every open is usually a truncated mid-stream tail) and
    // is silently dropped — never recorded as lastError, which is reserved for
    // open/read PORT failures so the status line doesn't flap on reconnect.
    private void FinalizeFrame(byte[] frame, int frameLen) {
      if (frameLen == 0) {
        return;
      }
      var encoded = new ReadOnlySpan<byte>(frame, 0, frameLen);
      if (!CobsCodec.TryDecode(encoded, out var decoded)) {
        return;
      }
      // decoded = payload ‖ CRC8(payload). CobsCodec guarantees length >= 7, so
      // payloadLen >= 6, but keep the explicit guard: it must precede the
      // header read in Dispatch (TryReadHeader needs at least MinDatagramLength
      // bytes, and byte 6 for the seq-carrying heartbeat/wand-v3 layout).
      int payloadLen = decoded.Length - 1;
      if (payloadLen < DatagramHandler.MinDatagramLength) {
        return;
      }
      byte crc = decoded[payloadLen];
      var payload = new byte[payloadLen];
      Array.Copy(decoded, payload, payloadLen);
      if (Crc8.Compute(payload) != crc) {
        return;
      }
      this.Dispatch(payload);
    }

    private void Dispatch(byte[] payload) {
      long now = Stopwatch.GetTimestamp();
      // The heartbeat uses the seq-carrying header (id[1] timestamp[4]
      // deviceType[1] seq[1]); its deviceType is at byte 5 like every other type,
      // with the seq byte after it. TryReadHeader classifies the layout for us; a
      // payload too short to classify is dropped.
      if (!DatagramHandler.TryReadHeader(payload, out var header)) {
        return;
      }
      if (header.DeviceType == 5) {
        // Heartbeat: receiver is up, but this is NOT a wand. Record liveness and
        // do not submit — the unknown-type path would otherwise register a ghost
        // device in the wand list.
        lock (this.stateLock) {
          this.lastHeartbeatTs = now;
        }
        return;
      }
      // A validated data frame also counts as liveness (see RECEIVER_ALIVE_MS).
      lock (this.stateLock) {
        this.lastFrameTs = now;
      }
      // Same public entry point the UDP ReceiveCallback uses; it re-validates
      // length/type and does all device-map/stats work under its own lock.
      this.sink.ProcessDatagram(payload);
    }
  }

  // Immutable snapshot of the serial receiver's state for the UIs. Value type so
  // callers read a consistent set of numbers with no shared mutable state.
  // The millis-since fields are large finite sentinels (not ∞) when the
  // corresponding event has never been seen, so they serialize to JSON.
  public readonly struct WandSerialStatus {
    public string PortName { get; }
    public bool PortOpen { get; }
    public double MillisSinceLastHeartbeat { get; }
    public double MillisSinceLastFrame { get; }
    public string LastError { get; }

    public WandSerialStatus(
      string portName,
      bool portOpen,
      double millisSinceLastHeartbeat,
      double millisSinceLastFrame,
      string lastError
    ) {
      this.PortName = portName;
      this.PortOpen = portOpen;
      this.MillisSinceLastHeartbeat = millisSinceLastHeartbeat;
      this.MillisSinceLastFrame = millisSinceLastFrame;
      this.LastError = lastError;
    }
  }
}
