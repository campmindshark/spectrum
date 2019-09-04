using Spectrum.Base;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Spectrum.Audio {

  public class CarabinerHandler {

    private readonly Configuration config;
    private Process process;

    public CarabinerHandler(Configuration config) {
      this.config = config;
      this.config.PropertyChanged += ConfigUpdated;
    }

    private void ConfigUpdated(object sender, PropertyChangedEventArgs e) {
      if (
        e.PropertyName == "beatInput" ||
        e.PropertyName == "humanLinkOutput" ||
        e.PropertyName == "madmomLinkOutput"
      ) {
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
      if (this.process != null) {
        this.process.Kill();
        try {
          this.process.Dispose();
        } catch { }
        this.process = null;
      }

      if (
        !this.active ||
        (this.config.beatInput != 2 &&
          (this.config.beatInput != 0 || !this.config.humanLinkOutput) &&
          (this.config.beatInput != 1 || !this.config.madmomLinkOutput))
      ) {
        return;
      }

      var currentDir = new DirectoryInfo(Environment.CurrentDirectory);
      var buildDirectoryName = currentDir.Name;
      var rootDir = currentDir.Parent.Parent.Parent.FullName;
      var audioBuildPath = Path.Combine(
        rootDir,
        "Audio",
        "bin",
        buildDirectoryName
      );

      ProcessStartInfo start = new ProcessStartInfo();
      start.WorkingDirectory = audioBuildPath;
      start.FileName = "Carabiner.exe";
      start.UseShellExecute = false;
      start.CreateNoWindow = true;

      this.process = Process.Start(start);
    }

  }

}
