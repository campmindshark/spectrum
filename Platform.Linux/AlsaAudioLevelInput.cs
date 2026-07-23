using System;
using System.Collections.Generic;
using System.Threading;
using Spectrum.Base;

namespace Spectrum.Platform.Linux {

  /**
   * Captures interleaved signed 16-bit PCM from a configured ALSA device and
   * publishes its current peak level. The worker retries after device/library
   * failures so a missing or unplugged interface cannot stop the engine.
   */
  public sealed class AlsaAudioLevelInput :
    IAudioLevelInput, IAudioDeviceProvider, IDisposable {
    private const int SampleRate = 44100;
    private const int FramesPerRead = 1024;
    private static readonly TimeSpan DefaultRetryDelay =
      TimeSpan.FromSeconds(1);

    private readonly object lifecycleLock = new object();
    private readonly IRuntimeSettingsConfiguration runtimeSettings;
    private readonly IAlsaApi alsa;
    private readonly IPcmBeatTracker beatTracker;
    private readonly TimeSpan retryDelay;
    private CancellationTokenSource? cancellation;
    private Thread? worker;
    private bool active;
    private float volume;
    private string? captureError;
    private string? discoveryError;
    private bool disposed;

    public AlsaAudioLevelInput(
      Configuration config,
      BeatBroadcaster beat
    ) : this(
      config,
      new AlsaApi(),
      new MadmomPcmBeatTracker(beat),
      DefaultRetryDelay) {
    }

    internal AlsaAudioLevelInput(
      Configuration config,
      IAlsaApi alsa,
      IPcmBeatTracker beatTracker,
      TimeSpan retryDelay
    ) {
      this.runtimeSettings = config as IRuntimeSettingsConfiguration ??
        throw new ArgumentException(
          "ALSA audio requires immutable runtime settings.", nameof(config));
      this.alsa = alsa ?? throw new ArgumentNullException(nameof(alsa));
      this.beatTracker = beatTracker ??
        throw new ArgumentNullException(nameof(beatTracker));
      if (retryDelay < TimeSpan.Zero) {
        throw new ArgumentOutOfRangeException(nameof(retryDelay));
      }
      this.retryDelay = retryDelay;
    }

    public bool Active {
      get {
        lock (this.lifecycleLock) {
          return this.active;
        }
      }
      set {
        Thread? threadToJoin = null;
        CancellationTokenSource? cancellationToDispose = null;
        lock (this.lifecycleLock) {
          this.ThrowIfDisposed();
          if (this.active == value) {
            return;
          }
          this.active = value;
          if (value) {
            var cancellation = new CancellationTokenSource();
            var worker = new Thread(
              () => this.CaptureLoop(cancellation.Token)) {
              IsBackground = true,
              Name = "Spectrum ALSA capture",
            };
            this.cancellation = cancellation;
            this.worker = worker;
            worker.Start();
          } else {
            cancellationToDispose = this.cancellation;
            cancellationToDispose?.Cancel();
            // Stop the child before joining capture. If it has stopped reading
            // stdin, terminating it releases any pending pipe write promptly.
            this.beatTracker.Enabled = false;
            threadToJoin = this.worker;
            this.worker = null;
            this.cancellation = null;
          }
        }
        if (threadToJoin != null && threadToJoin != Thread.CurrentThread) {
          threadToJoin.Join();
        }
        cancellationToDispose?.Dispose();
        if (!value) {
          Volatile.Write(ref this.volume, 0);
        }
      }
    }

    public bool AlwaysActive => true;
    public bool Enabled => true;
    public float Volume => Volatile.Read(ref this.volume);
    public string BackendName => "ALSA";
    public string? LastError =>
      Volatile.Read(ref this.discoveryError) ??
      Volatile.Read(ref this.captureError) ??
      (this.runtimeSettings.AudioSettingsSnapshot.BeatInput == 1
        ? this.beatTracker.LastError
        : null);

    public IReadOnlyList<AudioCaptureDevice> GetAvailableDevices() {
      try {
        IReadOnlyList<AudioCaptureDevice> devices =
          this.alsa.GetCaptureDevices();
        Volatile.Write(ref this.discoveryError, null);
        return devices;
      } catch (Exception error) {
        Volatile.Write(ref this.discoveryError, error.Message);
        return Array.Empty<AudioCaptureDevice>();
      }
    }

    public void OperatorUpdate() { }

    public void Dispose() {
      lock (this.lifecycleLock) {
        if (this.disposed) {
          return;
        }
      }
      this.Active = false;
      this.beatTracker.Dispose();
      lock (this.lifecycleLock) {
        this.disposed = true;
      }
    }

    private void CaptureLoop(CancellationToken stop) {
      while (!stop.IsCancellationRequested) {
        AudioSettingsSnapshot settings =
          this.runtimeSettings.AudioSettingsSnapshot;
        this.beatTracker.Enabled = settings.BeatInput == 1;
        string? deviceId = settings.DeviceId;
        if (string.IsNullOrWhiteSpace(deviceId)) {
          Volatile.Write(
            ref this.captureError, "No ALSA capture device is selected.");
          Volatile.Write(ref this.volume, 0);
          if (stop.WaitHandle.WaitOne(this.retryDelay)) {
            break;
          }
          continue;
        }

        try {
          using IAlsaCapture capture = this.alsa.OpenCapture(
            deviceId, SampleRate, FramesPerRead);
          var samples = new short[FramesPerRead * capture.Channels];
          Volatile.Write(ref this.captureError, null);
          while (!stop.IsCancellationRequested) {
            int sampleCount = capture.Read(samples);
            if (sampleCount > 0) {
              Volatile.Write(
                ref this.volume, PeakLevel(samples, sampleCount));
              this.beatTracker.Enabled =
                this.runtimeSettings.AudioSettingsSnapshot.BeatInput == 1;
              this.beatTracker.Write(
                samples, sampleCount, capture.Channels);
            }
          }
        } catch (Exception error) {
          Volatile.Write(ref this.volume, 0);
          Volatile.Write(ref this.captureError, error.Message);
          if (stop.WaitHandle.WaitOne(this.retryDelay)) {
            break;
          }
        }
      }
      this.beatTracker.Enabled = false;
      Volatile.Write(ref this.volume, 0);
    }

    internal static float PeakLevel(short[] samples, int sampleCount) {
      if (samples == null) {
        throw new ArgumentNullException(nameof(samples));
      }
      if (sampleCount < 0 || sampleCount > samples.Length) {
        throw new ArgumentOutOfRangeException(nameof(sampleCount));
      }
      int peak = 0;
      for (int i = 0; i < sampleCount; i++) {
        int magnitude = samples[i] == short.MinValue
          ? 32768
          : Math.Abs(samples[i]);
        if (magnitude > peak) {
          peak = magnitude;
        }
      }
      return peak / 32768f;
    }

    private void ThrowIfDisposed() {
      if (this.disposed) {
        throw new ObjectDisposedException(this.GetType().Name);
      }
    }
  }
}
