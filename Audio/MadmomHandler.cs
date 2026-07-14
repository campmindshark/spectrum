using Spectrum.Base;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace Spectrum.Audio {

  public class MadmomHandler {

    private readonly Configuration config;
    private readonly AudioInput audio;
    // The tempo service detected beats are reported into (owned by the
    // Operator, not part of Configuration).
    private readonly BeatBroadcaster beat;

    private Process process;

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
      if (this.process != null) {
        if (!process.HasExited) { this.process.Kill(); }
        try {
          this.process.Dispose();
        } catch (Exception e) {
          Debug.WriteLine("MadmomHandler: error disposing process: " + e);
        }
        this.process = null;
      }

      if (!this.active || this.config.beatInput != 1) {
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
      this.process = Process.Start(start);
      this.process.OutputDataReceived += BeatDetected;
      this.process.BeginOutputReadLine();
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
