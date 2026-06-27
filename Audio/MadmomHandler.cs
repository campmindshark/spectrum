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

    private Process process;

    public MadmomHandler(Configuration config, AudioInput audio) {
      this.config = config;
      this.audio = audio;
      this.config.PropertyChanged += ConfigUpdated;
    }

    private void ConfigUpdated(object sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == "audioDeviceID" || e.PropertyName == "beatInput") {
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

    // Walks up from the running assembly's directory looking for the bundled
    // Madmom virtualenv. Returns null if it can't be found, so callers can fail
    // gracefully instead of throwing an opaque Win32Exception from Process.Start.
    private static string FindMadmomScriptsDir() {
      var dir = new DirectoryInfo(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
      );
      while (dir != null) {
        var candidate = Path.Combine(dir.FullName, "Madmom", "env", "Scripts");
        if (File.Exists(Path.Combine(candidate, "python.exe"))) {
          return candidate;
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

      var envScriptPath = FindMadmomScriptsDir();
      if (envScriptPath == null) {
        Debug.WriteLine(
          "MadmomHandler: could not locate Madmom/env/Scripts/python.exe; " +
          "beat detection disabled."
        );
        return;
      }

      ProcessStartInfo start = new ProcessStartInfo();
      start.WorkingDirectory = envScriptPath;
      start.FileName = Path.Combine(envScriptPath, "python.exe");
      start.Arguments = string.Format(
        "TorchBeatTracker --host_api --audio_input={0} online",
        this.audio.CurrentAudioDeviceIndex
      );
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
      if (!double.TryParse(
        line.Substring(5),
        NumberStyles.Float,
        CultureInfo.InvariantCulture,
        out double seconds
      )) {
        return;
      }
      long msSinceBoot = (long)(seconds * 1000);
      this.config.beatBroadcaster.ReportMadmomBeat(msSinceBoot);
    }

  }

}
