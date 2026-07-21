using Spectrum.Base;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Spectrum.Audio {

  // Passive listener for Pioneer Pro DJ Link beat packets. CDJs and DJM mixers
  // broadcast a UDP packet on port 50001 on every beat, carrying the exact track
  // BPM, the deck's current pitch, and the beat-within-bar. We bind a socket to
  // that port, parse the beat packets, and feed the effective tempo into
  // BeatBroadcaster.ReportProDjLinkBeat. This is purely receive-only: no
  // virtual-CDJ announcement is needed for beat packets (that's a Phase 3 lift for
  // the richer per-player status on port 50002).
  //
  // Lifecycle mirrors MadmomHandler: it subscribes to config changes and runs
  // whenever audio is Active and beatInput selects Pro DJ Link (== 2).
  public class ProDjLinkHandler {

    // Pro DJ Link beat packets are UDP-broadcast to this port.
    private const int ProDjLinkPort = 50001;

    // Every Pro DJ Link packet opens with this 10-byte magic header, followed by
    // a one-byte packet type at offset 0x0a. Beat packets are type 0x28. Field
    // offsets below are confirmed against Deep Symmetry's beat-link Beat.java.
    private static readonly byte[] MagicHeader = {
      0x51, 0x73, 0x70, 0x74, 0x31, 0x57, 0x6d, 0x4a, 0x4f, 0x4c,
    };
    private const int PacketTypeOffset = 0x0a;
    private const byte BeatPacketType = 0x28;
    // 1-4 = players, 33 = mixer, higher = rekordbox / other software.
    private const int DeviceNumberOffset = 0x21;
    // 3-byte big-endian playback-speed multiplier; 0x100000 (1048576) == 1.0x.
    private const int PitchOffset = 0x55;
    private const int Pitch1x = 0x100000;
    // 2-byte big-endian track BPM * 100 (e.g. 12050 == 120.50 BPM).
    private const int BpmOffset = 0x5a;
    // 1 byte, 1-4, where 1 is the downbeat.
    private const int BeatWithinBarOffset = 0x5c;
    // Shortest packet we can fully parse (must reach the beat-within-bar byte).
    private const int MinBeatPacketLength = BeatWithinBarOffset + 1;

    private const int MixerDeviceNumber = 33;
    // If the device we're currently following goes silent for this long, allow
    // another device to take over as the tempo source.
    private const long DeviceTimeoutMs = 2000;
    // Plausible effective-BPM band; rejects stopped decks (BPM 0) and the 0xffff
    // "no tempo" sentinel some gear emits.
    private const double MinPlausibleBpm = 20.0;
    private const double MaxPlausibleBpm = 500.0;

    private readonly Configuration config;
    private readonly IRuntimeSettingsConfiguration runtimeSettings;
    // The tempo service received beats are reported into (owned by the
    // Operator, not part of Configuration).
    private readonly BeatBroadcaster beat;

    // Touched only from the receive thread, so no locking is needed. Tracks which
    // broadcasting device (CDJ or mixer) we're currently treating as the tempo
    // source, and when we last accepted a beat from it.
    private int followedDevice = -1;
    private long lastFollowedBeatMs = 0;

    private UdpClient client;
    private Thread receiveThread;
    private volatile bool running;

    public ProDjLinkHandler(Configuration config, BeatBroadcaster beat) {
      this.config = config;
      this.runtimeSettings = config as IRuntimeSettingsConfiguration ??
        throw new ArgumentException(
          "ProDjLinkHandler requires immutable runtime settings.",
          nameof(config));
      this.beat = beat;
      this.config.PropertyChanged += ConfigUpdated;
    }

    private void ConfigUpdated(object sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == nameof(this.config.beatInput)) {
        this.UpdateEnabled();
      }
    }

    private bool active;
    public bool Active {
      get {
        return this.active;
      }
      set {
        if (this.active == value) {
          return;
        }
        this.active = value;
        this.UpdateEnabled();
      }
    }

    private void UpdateEnabled() {
      bool shouldRun = this.active &&
        this.runtimeSettings.AudioSettingsSnapshot.BeatInput == 2;
      if (shouldRun == this.running) {
        return;
      }
      if (shouldRun) {
        this.Start();
      } else {
        this.Stop();
      }
    }

    private void Start() {
      try {
        // ReuseAddress (set before Bind) lets us coexist with anything else on
        // the port; EnableBroadcast is required to receive the subnet broadcasts.
        this.client = new UdpClient();
        this.client.ExclusiveAddressUse = false;
        this.client.Client.SetSocketOption(
          SocketOptionLevel.Socket,
          SocketOptionName.ReuseAddress,
          true
        );
        this.client.EnableBroadcast = true;
        this.client.Client.Bind(new IPEndPoint(IPAddress.Any, ProDjLinkPort));
      } catch (Exception e) {
        // No network on that subnet, or the port is unusable. Fail quietly so the
        // app keeps running with no tempo, rather than throwing; recoverable by
        // reselecting the source once the network is present.
        Debug.WriteLine(
          "ProDjLinkHandler: could not bind UDP " + ProDjLinkPort + "; Pro DJ " +
          "Link tempo disabled. " + e.Message
        );
        this.CloseClient();
        return;
      }

      this.followedDevice = -1;
      this.running = true;
      this.receiveThread = new Thread(this.ReceiveLoop) {
        IsBackground = true,
        Name = "ProDjLink",
      };
      this.receiveThread.Start();
    }

    private void Stop() {
      this.running = false;
      // Closing the socket unblocks the blocking Receive on the receive thread.
      this.CloseClient();
      if (this.receiveThread != null) {
        this.receiveThread.Join();
        this.receiveThread = null;
      }
    }

    private void CloseClient() {
      if (this.client != null) {
        try {
          this.client.Close();
        } catch (Exception e) {
          Debug.WriteLine("ProDjLinkHandler: error closing socket: " + e);
        }
        this.client = null;
      }
    }

    private void ReceiveLoop() {
      var remote = new IPEndPoint(IPAddress.Any, 0);
      while (this.running) {
        byte[] data;
        try {
          data = this.client.Receive(ref remote);
        } catch (Exception) {
          // Socket closed on shutdown (SocketException / ObjectDisposedException),
          // or a transient receive error. If we're still meant to be running it
          // was transient; otherwise this is the expected shutdown path.
          if (!this.running) {
            break;
          }
          continue;
        }
        // Never let a malformed packet throw on the receive thread, where an
        // unhandled exception would go unobserved and could tear down the app
        // (same discipline as MadmomHandler.BeatDetected).
        try {
          this.HandlePacket(data);
        } catch (Exception e) {
          Debug.WriteLine("ProDjLinkHandler: dropped bad packet: " + e);
        }
      }
    }

    private void HandlePacket(byte[] data) {
      if (data.Length < MinBeatPacketLength) {
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
        // Stopped deck / no track loaded.
        return;
      }
      double effectiveBpm = (bpmRaw / 100.0) * ((double)pitchRaw / Pitch1x);
      if (effectiveBpm < MinPlausibleBpm || effectiveBpm > MaxPlausibleBpm) {
        return;
      }

      if (!this.ShouldFollow(deviceNumber)) {
        return;
      }
      this.beat.ReportProDjLinkBeat(effectiveBpm, beatWithinBar);
    }

    // Every playing deck plus the mixer broadcasts beats, so we pick one device to
    // follow. Policy (MVP): the mixer (device 33) wins whenever it's present, else
    // we follow the most recently seen player. If the followed device goes silent
    // past DeviceTimeoutMs, another device may take over. (Phase 3 upgrades this to
    // "follow the tempo master" using the port 50002 status stream.)
    private bool ShouldFollow(int deviceNumber) {
      long now = Environment.TickCount64;
      bool followedStale = this.followedDevice == -1
        || (now - this.lastFollowedBeatMs) > DeviceTimeoutMs;

      if (deviceNumber == MixerDeviceNumber) {
        // The mixer takes priority over any player.
        this.followedDevice = deviceNumber;
      } else if (this.followedDevice != MixerDeviceNumber || followedStale) {
        // No live mixer to defer to: follow this player.
        this.followedDevice = deviceNumber;
      }

      if (deviceNumber != this.followedDevice) {
        return false;
      }
      this.lastFollowedBeatMs = now;
      return true;
    }

  }

}
