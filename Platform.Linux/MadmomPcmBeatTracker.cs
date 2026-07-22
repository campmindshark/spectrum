using Spectrum.Base;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Spectrum.Platform.Linux {

  internal interface IPcmBeatTracker : IDisposable {
    bool Enabled { get; set; }
    string LastError { get; }
    void Write(short[] samples, int sampleCount, int channels);
  }

  /**
   * Owns the Linux Madmom child and feeds it the PCM already captured by ALSA.
   * Keeping ALSA as the single hardware owner avoids translating stable ALSA
   * PCM names into PortAudio's independently-enumerated numeric indices.
   */
  internal sealed class MadmomPcmBeatTracker : IPcmBeatTracker {
    private static readonly TimeSpan DefaultRestartDelay =
      TimeSpan.FromSeconds(2);

    private readonly object lifecycleLock = new object();
    private readonly BeatBroadcaster beat;
    private readonly Func<ProcessStartInfo> startInfoFactory;
    private readonly long restartDelayTicks;
    private Process process;
    private bool enabled;
    private bool disposed;
    private long nextStartAttempt;
    private string lastError;
    private string lastStandardError;
    private byte[] monoPcm = Array.Empty<byte>();

    public MadmomPcmBeatTracker(BeatBroadcaster beat) : this(
      beat,
      () => {
        MadmomRuntimePaths runtime = MadmomRuntimeLocator.Find(
          AppContext.BaseDirectory,
          useWindowsLayout: false);
        if (runtime == null) {
          throw new FileNotFoundException(
            "Could not locate the packaged Linux Madmom runtime.");
        }
        return CreateStartInfo(runtime);
      },
      DefaultRestartDelay) {
    }

    internal MadmomPcmBeatTracker(
      BeatBroadcaster beat,
      Func<ProcessStartInfo> startInfoFactory,
      TimeSpan restartDelay
    ) {
      this.beat = beat ?? throw new ArgumentNullException(nameof(beat));
      this.startInfoFactory = startInfoFactory ??
        throw new ArgumentNullException(nameof(startInfoFactory));
      if (restartDelay < TimeSpan.Zero) {
        throw new ArgumentOutOfRangeException(nameof(restartDelay));
      }
      this.restartDelayTicks = checked((long)(
        restartDelay.TotalSeconds * Stopwatch.Frequency));
    }

    public bool Enabled {
      get {
        lock (this.lifecycleLock) {
          return this.enabled;
        }
      }
      set {
        lock (this.lifecycleLock) {
          this.ThrowIfDisposed();
          if (this.enabled == value) {
            return;
          }
          this.enabled = value;
          if (!value) {
            this.StopProcessLocked();
            this.nextStartAttempt = 0;
            this.lastStandardError = null;
            this.lastError = null;
          }
        }
      }
    }

    public string LastError {
      get {
        lock (this.lifecycleLock) {
          return this.lastError;
        }
      }
    }

    public void Write(short[] samples, int sampleCount, int channels) {
      if (samples == null) {
        throw new ArgumentNullException(nameof(samples));
      }
      if (sampleCount < 0 || sampleCount > samples.Length) {
        throw new ArgumentOutOfRangeException(nameof(sampleCount));
      }
      if (channels <= 0) {
        throw new ArgumentOutOfRangeException(nameof(channels));
      }

      Process target;
      Stream input;
      int byteCount;
      lock (this.lifecycleLock) {
        this.ThrowIfDisposed();
        if (!this.enabled || !this.EnsureProcessLocked()) {
          return;
        }
        byteCount = EncodeMonoPcm(
          samples, sampleCount, channels, ref this.monoPcm);
        if (byteCount == 0) {
          return;
        }
        target = this.process;
        input = target.StandardInput.BaseStream;
      }

      try {
        // Do not hold lifecycleLock while writing. Disabling the input can then
        // kill a stalled child and release this pipe write during shutdown.
        input.Write(this.monoPcm, 0, byteCount);
      } catch (Exception error) when (
          error is IOException ||
          error is InvalidOperationException ||
          error is ObjectDisposedException) {
        lock (this.lifecycleLock) {
          if (ReferenceEquals(this.process, target)) {
            this.FailProcessLocked(
              target, "Could not stream ALSA PCM to Madmom: " + error.Message);
          }
        }
      }
    }

    public void Dispose() {
      lock (this.lifecycleLock) {
        if (this.disposed) {
          return;
        }
        this.enabled = false;
        this.StopProcessLocked();
        this.disposed = true;
      }
    }

    private bool EnsureProcessLocked() {
      if (this.process != null) {
        return true;
      }
      long now = Stopwatch.GetTimestamp();
      if (now < this.nextStartAttempt) {
        return false;
      }

      Process started = null;
      try {
        ProcessStartInfo start = this.startInfoFactory() ??
          throw new InvalidOperationException(
            "The Madmom process factory returned no start information.");
        started = Process.Start(start) ?? throw new InvalidOperationException(
          "Process.Start returned no Madmom process.");
        started.OutputDataReceived += this.BeatDetected;
        started.ErrorDataReceived += this.StandardErrorReceived;
        started.Exited += this.ProcessExited;
        this.process = started;
        this.lastError = null;
        this.lastStandardError = null;
        this.nextStartAttempt = 0;
        started.BeginOutputReadLine();
        started.BeginErrorReadLine();
        started.EnableRaisingEvents = true;
        return true;
      } catch (Exception error) {
        if (ReferenceEquals(this.process, started)) {
          this.process = null;
        }
        DisposeProcess(started, terminate: true);
        this.DelayNextStartLocked(
          "Could not start the Madmom PCM tracker: " + error.Message);
        return false;
      }
    }

    internal static ProcessStartInfo CreateStartInfo(
      MadmomRuntimePaths runtime
    ) {
      if (runtime == null) {
        throw new ArgumentNullException(nameof(runtime));
      }
      var start = new ProcessStartInfo {
        WorkingDirectory = Path.GetDirectoryName(runtime.TrackerPath),
        FileName = runtime.PythonPath,
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
      };
      start.ArgumentList.Add(runtime.TrackerPath);
      start.ArgumentList.Add("--pcm_stdin");
      start.ArgumentList.Add("online");
      start.Environment["PYTHONUNBUFFERED"] = "1";
      return start;
    }

    internal static int EncodeMonoPcm(
      short[] samples,
      int sampleCount,
      int channels,
      ref byte[] destination
    ) {
      int frameCount = sampleCount / channels;
      int byteCount = checked(frameCount * sizeof(short));
      if (destination == null || destination.Length < byteCount) {
        destination = new byte[byteCount];
      }
      for (int frame = 0; frame < frameCount; frame++) {
        int sum = 0;
        int frameStart = frame * channels;
        for (int channel = 0; channel < channels; channel++) {
          sum += samples[frameStart + channel];
        }
        short mono = checked((short)(sum / channels));
        int offset = frame * sizeof(short);
        destination[offset] = (byte)mono;
        destination[offset + 1] = (byte)(mono >> 8);
      }
      return byteCount;
    }

    private void BeatDetected(object sender, DataReceivedEventArgs e) {
      if (TryParseBeat(e.Data, out long milliseconds)) {
        this.beat.ReportMadmomBeat(milliseconds);
      }
    }

    internal static bool TryParseBeat(string line, out long milliseconds) {
      milliseconds = 0;
      return line != null &&
        line.StartsWith("BEAT:", StringComparison.Ordinal) &&
        double.TryParse(
          line.Substring(5),
          NumberStyles.Float,
          CultureInfo.InvariantCulture,
          out double seconds) &&
        double.IsFinite(seconds) &&
        seconds >= 0 &&
        seconds <= long.MaxValue / 1000.0 &&
        (milliseconds = (long)(seconds * 1000)) >= 0;
    }

    private void StandardErrorReceived(object sender, DataReceivedEventArgs e) {
      if (!string.IsNullOrWhiteSpace(e.Data)) {
        lock (this.lifecycleLock) {
          if (ReferenceEquals(this.process, sender)) {
            this.lastStandardError = e.Data.Trim();
          }
        }
      }
    }

    private void ProcessExited(object sender, EventArgs e) {
      var exited = sender as Process;
      if (exited == null) {
        return;
      }
      lock (this.lifecycleLock) {
        if (!ReferenceEquals(this.process, exited)) {
          return;
        }
        string exit = "unknown exit code";
        try {
          exit = "exit code " + exited.ExitCode;
        } catch (Exception) {
        }
        string detail = string.IsNullOrWhiteSpace(this.lastStandardError)
          ? ""
          : ": " + this.lastStandardError;
        this.FailProcessLocked(
          exited, "Madmom PCM tracker stopped unexpectedly (" + exit + ")" +
            detail);
      }
    }

    private void FailProcessLocked(Process target, string error) {
      if (ReferenceEquals(this.process, target)) {
        this.process = null;
      }
      DisposeProcess(target, terminate: true);
      this.DelayNextStartLocked(error);
    }

    private void DelayNextStartLocked(string error) {
      this.lastError = error;
      this.nextStartAttempt =
        Stopwatch.GetTimestamp() + this.restartDelayTicks;
      Debug.WriteLine("MadmomPcmBeatTracker: " + error);
    }

    private void StopProcessLocked() {
      Process toStop = this.process;
      this.process = null;
      DisposeProcess(toStop, terminate: true);
    }

    private void DisposeProcess(Process target, bool terminate) {
      if (target == null) {
        return;
      }
      target.OutputDataReceived -= this.BeatDetected;
      target.ErrorDataReceived -= this.StandardErrorReceived;
      target.Exited -= this.ProcessExited;
      if (terminate) {
        try {
          if (!target.HasExited) {
            target.Kill(entireProcessTree: true);
          }
        } catch (Exception error) {
          Debug.WriteLine(
            "MadmomPcmBeatTracker: error stopping child: " + error);
        }
      }
      try {
        target.Dispose();
      } catch (Exception error) {
        Debug.WriteLine(
          "MadmomPcmBeatTracker: error disposing child: " + error);
      }
    }

    private void ThrowIfDisposed() {
      if (this.disposed) {
        throw new ObjectDisposedException(this.GetType().Name);
      }
    }
  }
}
