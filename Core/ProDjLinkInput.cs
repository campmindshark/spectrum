using Spectrum.Base;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Spectrum {

  /**
   * Portable passive listener for Pioneer Pro DJ Link beat packets. CDJs and
   * DJM mixers broadcast a UDP packet on port 50001 on every beat, carrying
   * the exact track BPM, current pitch, and beat-within-bar. No virtual-CDJ
   * announcement is needed for these receive-only beat packets.
   *
   * Operator owns this separately from platform audio capture so the same
   * tempo source works with WASAPI on Windows and ALSA on Linux. Bind and
   * receive failures stay on the worker thread and retry, allowing startup
   * before the show network is available and recovery after an interface flap.
   */
  internal sealed class ProDjLinkInput : Input, IDisposable {
    internal const int DefaultPort = 50001;
    private static readonly TimeSpan DefaultRetryDelay =
      TimeSpan.FromSeconds(1);

    // Every Pro DJ Link packet opens with this 10-byte magic header, followed
    // by a one-byte packet type at offset 0x0a. Beat packets are type 0x28.
    private static readonly byte[] MagicHeader = {
      0x51, 0x73, 0x70, 0x74, 0x31, 0x57, 0x6d, 0x4a, 0x4f, 0x4c,
    };
    private const int PacketTypeOffset = 0x0a;
    private const byte BeatPacketType = 0x28;
    // 1-4 = players, 33 = mixer, higher = rekordbox / other software.
    private const int DeviceNumberOffset = 0x21;
    // 3-byte big-endian playback-speed multiplier; 0x100000 == 1.0x.
    private const int PitchOffset = 0x55;
    private const int Pitch1x = 0x100000;
    // 2-byte big-endian track BPM * 100 (for example 12050 == 120.50 BPM).
    private const int BpmOffset = 0x5a;
    // 1 byte, 1-4, where 1 is the downbeat.
    private const int BeatWithinBarOffset = 0x5c;
    private const int MinBeatPacketLength = BeatWithinBarOffset + 1;

    private const int MixerDeviceNumber = 33;
    private const long DeviceTimeoutMs = 2000;
    private const double MinPlausibleBpm = 20.0;
    private const double MaxPlausibleBpm = 500.0;

    private readonly IRuntimeSettingsConfiguration runtimeSettings;
    private readonly BeatBroadcaster beat;
    private readonly bool connectNetwork;
    private readonly int port;
    private readonly TimeSpan retryDelay;
    private readonly ManualResetEventSlim stopRequested =
      new ManualResetEventSlim(true);

    // Only the receive thread touches source selection.
    private int followedDevice = -1;
    private long lastFollowedBeatMs;

    private UdpClient? client;
    private Thread? receiveThread;
    private volatile bool running;
    private bool active;
    private string? lastError;

    public ProDjLinkInput(
      Configuration config,
      BeatBroadcaster beat,
      bool connectNetwork = true
    ) : this(config, beat, connectNetwork, DefaultPort, DefaultRetryDelay) {
    }

    internal ProDjLinkInput(
      Configuration config,
      BeatBroadcaster beat,
      bool connectNetwork,
      int port,
      TimeSpan retryDelay
    ) {
      this.runtimeSettings = config as IRuntimeSettingsConfiguration ??
        throw new ArgumentException(
          "ProDjLinkInput requires immutable runtime settings.",
          nameof(config));
      this.beat = beat ?? throw new ArgumentNullException(nameof(beat));
      if (port < 1 || port > 65535) {
        throw new ArgumentOutOfRangeException(nameof(port));
      }
      if (retryDelay < TimeSpan.Zero) {
        throw new ArgumentOutOfRangeException(nameof(retryDelay));
      }
      this.connectNetwork = connectNetwork;
      this.port = port;
      this.retryDelay = retryDelay;
    }

    public bool Active {
      get => this.active;
      set {
        if (this.active == value) {
          return;
        }
        this.active = value;
        if (value) {
          this.Start();
        } else {
          this.Stop();
        }
      }
    }

    public bool AlwaysActive => true;

    public bool Enabled =>
      this.runtimeSettings.AudioSettingsSnapshot.BeatInput == 2;

    internal string? LastError => Volatile.Read(ref this.lastError);

    internal bool Listening => Volatile.Read(ref this.client) != null;

    internal event Action? StatusChanged;

    public void OperatorUpdate() { }

    private void Start() {
      if (!this.connectNetwork) {
        return;
      }
      this.followedDevice = -1;
      this.lastFollowedBeatMs = 0;
      this.stopRequested.Reset();
      this.running = true;
      this.receiveThread = new Thread(this.ReceiveWorker) {
        IsBackground = true,
        Name = "ProDjLink",
      };
      this.receiveThread.Start();
    }

    private void Stop() {
      this.running = false;
      this.stopRequested.Set();
      this.ClosePublishedClient();
      Thread? thread = this.receiveThread;
      if (thread != null) {
        thread.Join();
        this.receiveThread = null;
      }
    }

    private void ReceiveWorker() {
      while (this.running) {
        UdpClient? current = null;
        try {
          current = this.CreateBoundClient();
          if (!this.running) {
            break;
          }
          UdpClient? previous = Interlocked.Exchange(
            ref this.client, current);
          previous?.Dispose();
          Volatile.Write(ref this.lastError, null);
          this.PublishStatusChanged();
          this.ReceivePackets(current);
        } catch (Exception error) {
          if (this.running) {
            Volatile.Write(ref this.lastError, error.Message);
            this.PublishStatusChanged();
            Debug.WriteLine(
              "ProDjLinkInput: UDP listener failed; retrying. " + error);
          }
        } finally {
          if (current != null) {
            if (ReferenceEquals(
                Interlocked.CompareExchange(
                  ref this.client, null, current), current)) {
              this.PublishStatusChanged();
            }
            current.Dispose();
          }
        }

        if (this.running) {
          this.stopRequested.Wait(this.retryDelay);
        }
      }
    }

    private UdpClient CreateBoundClient() {
      var result = new UdpClient();
      try {
        result.ExclusiveAddressUse = false;
        result.Client.SetSocketOption(
          SocketOptionLevel.Socket,
          SocketOptionName.ReuseAddress,
          true);
        result.EnableBroadcast = true;
        result.Client.Bind(new IPEndPoint(IPAddress.Any, this.port));
        return result;
      } catch {
        result.Dispose();
        throw;
      }
    }

    private void ReceivePackets(UdpClient current) {
      var remote = new IPEndPoint(IPAddress.Any, 0);
      while (this.running) {
        byte[] data = current.Receive(ref remote);
        try {
          this.HandlePacket(data);
        } catch (Exception error) {
          // Malformed packets are untrusted network input. Keep receiving even
          // if an unexpected parser edge case escapes the validation below.
          Debug.WriteLine(
            "ProDjLinkInput: dropped malformed packet: " + error);
        }
      }
    }

    private void ClosePublishedClient() {
      UdpClient? current = Interlocked.Exchange(ref this.client, null);
      if (current != null) {
        this.PublishStatusChanged();
        try {
          current.Close();
        } catch (Exception error) {
          Debug.WriteLine(
            "ProDjLinkInput: error closing UDP listener: " + error);
        }
      }
    }

    private void PublishStatusChanged() {
      try {
        this.StatusChanged?.Invoke();
      } catch (Exception error) {
        Debug.WriteLine(
          "ProDjLinkInput: status observer failed: " + error);
      }
    }

    private void HandlePacket(byte[] data) {
      if (data == null || data.Length < MinBeatPacketLength) {
        return;
      }
      for (int i = 0; i < MagicHeader.Length; i++) {
        if (data[i] != MagicHeader[i]) {
          return;
        }
      }
      if (data[PacketTypeOffset] != BeatPacketType) {
        return;
      }

      int deviceNumber = data[DeviceNumberOffset];
      int bpmRaw = (data[BpmOffset] << 8) | data[BpmOffset + 1];
      int pitchRaw = (data[PitchOffset] << 16)
        | (data[PitchOffset + 1] << 8)
        | data[PitchOffset + 2];
      int beatWithinBar = data[BeatWithinBarOffset];

      if (bpmRaw <= 0 || pitchRaw <= 0) {
        return;
      }
      double effectiveBpm =
        (bpmRaw / 100.0) * ((double)pitchRaw / Pitch1x);
      if (effectiveBpm < MinPlausibleBpm ||
          effectiveBpm > MaxPlausibleBpm) {
        return;
      }
      if (!this.ShouldFollow(deviceNumber)) {
        return;
      }
      this.beat.ReportProDjLinkBeat(effectiveBpm, beatWithinBar);
    }

    private bool ShouldFollow(int deviceNumber) {
      long now = Environment.TickCount64;
      bool followedStale = this.followedDevice == -1 ||
        now - this.lastFollowedBeatMs > DeviceTimeoutMs;

      if (deviceNumber == MixerDeviceNumber) {
        this.followedDevice = deviceNumber;
      } else if (this.followedDevice != MixerDeviceNumber || followedStale) {
        this.followedDevice = deviceNumber;
      }
      if (deviceNumber != this.followedDevice) {
        return false;
      }
      this.lastFollowedBeatMs = now;
      return true;
    }

    public void Dispose() {
      this.Active = false;
      this.stopRequested.Dispose();
    }
  }
}
