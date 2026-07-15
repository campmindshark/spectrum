using Spectrum.Base;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Spectrum.Audio {

  public class MadmomHandler {

    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(2);

    private readonly Configuration config;
    private readonly AudioInput audio;
    // The tempo service detected beats are reported into (owned by the
    // Operator, not part of Configuration).
    private readonly BeatBroadcaster beat;

    private readonly object lifecycleLock = new object();
    private Process process;
    private Timer restartTimer;
    private int restartGeneration;

    public MadmomHandler(Configuration config, AudioInput audio, BeatBroadcaster beat) {
      this.config = config;
      this.audio = audio;
      this.beat = beat;
      this.config.PropertyChanged += ConfigUpdated;
    }

    private void ConfigUpdated(object sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == nameof(this.config.audioDeviceID) ||
          e.PropertyName == nameof(this.config.beatInput)) {
        this.UpdateEnabled();
      }
    }

    private bool active;
    public bool Active {
      get {
        lock (this.lifecycleLock) {
          return this.active;
        }
      }
      set {
        lock (this.lifecycleLock) {
          if (this.active == value) {
            return;
          }
          this.active = value;
          this.UpdateEnabledLocked();
        }
      }
    }

    private sealed class MadmomRuntime {
      public MadmomRuntime(string pythonPath, string scriptPath) {
        this.PythonPath = pythonPath;
        this.ScriptPath = scriptPath;
      }

      public string PythonPath { get; }
      public string ScriptPath { get; }
    }

    // Walks up from the running assembly's directory looking first for the
    // relocatable release runtime and then for the checkout's development
    // virtualenv. Returning both paths lets ProcessStartInfo.ArgumentList avoid
    // fragile command-line quoting.
    private static MadmomRuntime FindMadmomRuntime() {
      var dir = new DirectoryInfo(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
      );
      while (dir != null) {
        var portableRoot = Path.Combine(dir.FullName, "Madmom", "runtime");
        var portablePython = Path.Combine(portableRoot, "python.exe");
        var portableScript = Path.Combine(
          portableRoot, "Scripts", "DBNBeatTracker"
        );
        if (File.Exists(portablePython) && File.Exists(portableScript)) {
          return new MadmomRuntime(portablePython, portableScript);
        }

        foreach (var environmentName in new[] { ".build-env", "env" }) {
          var developmentScripts = Path.Combine(
            dir.FullName, "Madmom", environmentName, "Scripts"
          );
          var developmentPython = Path.Combine(
            developmentScripts, "python.exe"
          );
          var developmentScript = Path.Combine(
            developmentScripts, "DBNBeatTracker"
          );
          if (File.Exists(developmentPython) && File.Exists(developmentScript)) {
            return new MadmomRuntime(developmentPython, developmentScript);
          }
        }
        dir = dir.Parent;
      }
      return null;
    }

    private void UpdateEnabled() {
      lock (this.lifecycleLock) {
        this.UpdateEnabledLocked();
      }
    }

    private void UpdateEnabledLocked() {
      this.CancelRestartLocked();
      this.StopProcessLocked();
      if (this.ShouldRunLocked()) {
        this.TryStartProcessLocked();
      }
    }

    private bool ShouldRunLocked() {
      return this.active && this.config.beatInput == 1;
    }

    private void TryStartProcessLocked() {
      if (this.process != null || !this.ShouldRunLocked()) {
        return;
      }

      var runtime = FindMadmomRuntime();
      if (runtime == null) {
        Debug.WriteLine(
          "MadmomHandler: could not locate a portable Madmom runtime or " +
          "a Madmom development environment; " +
          "beat detection disabled."
        );
        return;
      }

      Process started = null;
      try {
        ProcessStartInfo start = new ProcessStartInfo();
        start.WorkingDirectory = Path.GetDirectoryName(runtime.ScriptPath);
        start.FileName = runtime.PythonPath;
        start.ArgumentList.Add(runtime.ScriptPath);
        start.ArgumentList.Add("--host_api");
        start.ArgumentList.Add(string.Format(
          "--audio_input={0}", this.audio.CurrentAudioDeviceIndex
        ));
        start.ArgumentList.Add("online");
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.CreateNoWindow = true;

        started = Process.Start(start) ?? throw new InvalidOperationException(
          "Process.Start returned no Madmom process.");
        started.OutputDataReceived += BeatDetected;
        started.Exited += ProcessExited;
        this.process = started;
        started.BeginOutputReadLine();
        // Enable exit notification only after stdout processing is ready. If
        // the child already exited, setting this still raises Exited, without
        // leaving a half-initialized process visible to the callback.
        started.EnableRaisingEvents = true;
      } catch (Exception e) {
        if (ReferenceEquals(this.process, started)) {
          this.process = null;
        }
        this.DisposeProcess(started, true);
        Debug.WriteLine("MadmomHandler: could not start beat tracker: " + e);
        if (this.ShouldRunLocked()) {
          this.ScheduleRestartLocked();
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
        this.process = null;
        string exitDescription = "unknown exit code";
        try {
          exitDescription = "exit code " + exited.ExitCode;
        } catch (Exception exitError) {
          Debug.WriteLine(
            "MadmomHandler: could not read beat tracker exit code: " +
            exitError);
        }
        Debug.WriteLine(
          "MadmomHandler: beat tracker stopped unexpectedly (" +
          exitDescription + ").");
        this.DisposeProcess(exited, false);
        if (this.ShouldRunLocked()) {
          this.ScheduleRestartLocked();
        }
      }
    }

    private void StopProcessLocked() {
      Process toStop = this.process;
      this.process = null;
      this.DisposeProcess(toStop, true);
    }

    private void DisposeProcess(Process target, bool terminate) {
      if (target == null) {
        return;
      }
      target.OutputDataReceived -= BeatDetected;
      target.Exited -= ProcessExited;
      if (terminate) {
        try {
          if (!target.HasExited) {
            target.Kill();
          }
        } catch (Exception e) {
          Debug.WriteLine("MadmomHandler: error stopping process: " + e);
        }
      }
      try {
        target.Dispose();
      } catch (Exception e) {
        Debug.WriteLine("MadmomHandler: error disposing process: " + e);
      }
    }

    private void ScheduleRestartLocked() {
      if (this.restartTimer != null) {
        return;
      }
      int generation = ++this.restartGeneration;
      this.restartTimer = new Timer(
        RestartProcess,
        generation,
        RestartDelay,
        Timeout.InfiniteTimeSpan
      );
    }

    private void RestartProcess(object state) {
      int generation = (int)state;
      lock (this.lifecycleLock) {
        if (generation != this.restartGeneration) {
          return;
        }
        Timer completedTimer = this.restartTimer;
        this.restartTimer = null;
        completedTimer?.Dispose();
        this.TryStartProcessLocked();
      }
    }

    private void CancelRestartLocked() {
      this.restartGeneration++;
      Timer cancelledTimer = this.restartTimer;
      this.restartTimer = null;
      cancelledTimer?.Dispose();
    }

    private void BeatDetected(object sender, DataReceivedEventArgs e) {
      string line = e.Data;
      if (line == null || !line.StartsWith("BEAT:")) {
        return;
      }

      // This runs on the process's stdout reader thread, where an unhandled
      // exception would be unobserved and could tear down the app. A malformed
      // BEAT: line must be dropped, not thrown on. Parse with InvariantCulture
      // since the Python side emits a '.'-decimal float regardless of locale.
      //
      // The timestamp is Madmom's audio-stream position (sample-derived, so it
      // is immune to the tracker's bursty per-frame latency). BeatBroadcaster uses
      // the spacing between these timestamps for tempo, and its own clock only
      // for the real-time phase anchor.
      if (!double.TryParse(
        line.Substring(5),
        NumberStyles.Float,
        CultureInfo.InvariantCulture,
        out double seconds
      )) {
        return;
      }
      this.beat.ReportMadmomBeat((long)(seconds * 1000));
    }

  }

}
