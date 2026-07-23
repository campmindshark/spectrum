using System;
using System.IO;

namespace Spectrum.Base {

  internal sealed class MadmomRuntimePaths {
    public MadmomRuntimePaths(string pythonPath, string trackerPath) {
      this.PythonPath = pythonPath;
      this.TrackerPath = trackerPath;
    }

    public string PythonPath { get; }
    public string TrackerPath { get; }
  }

  /// <summary>
  /// Finds the packaged or development Python environment used by Spectrum's
  /// Madmom beat tracker. Keeping this path policy in the portable foundation
  /// lets both platform frontends use the same release layout.
  /// </summary>
  internal static class MadmomRuntimeLocator {
    public static MadmomRuntimePaths? Find(
      string startDirectory,
      bool useWindowsLayout
    ) {
      if (string.IsNullOrWhiteSpace(startDirectory)) {
        throw new ArgumentException(
          "A runtime search directory is required.", nameof(startDirectory));
      }

      var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
      while (directory != null) {
        string madmomRoot = Path.Combine(directory.FullName, "Madmom");
        MadmomRuntimePaths? packaged = FindInEnvironment(
          Path.Combine(madmomRoot, "runtime"),
          useWindowsLayout,
          isPackagedRuntime: true);
        if (packaged != null) {
          return packaged;
        }

        foreach (string environmentName in new[] { ".build-env", "env" }) {
          MadmomRuntimePaths? development = FindInEnvironment(
            Path.Combine(madmomRoot, environmentName),
            useWindowsLayout,
            isPackagedRuntime: false);
          if (development != null) {
            return development;
          }
        }
        directory = directory.Parent;
      }
      return null;
    }

    private static MadmomRuntimePaths? FindInEnvironment(
      string environmentRoot,
      bool useWindowsLayout,
      bool isPackagedRuntime
    ) {
      string executablesDirectory = useWindowsLayout
        ? Path.Combine(environmentRoot, "Scripts")
        : Path.Combine(environmentRoot, "bin");
      string pythonPath;
      if (!useWindowsLayout) {
        pythonPath = Path.Combine(executablesDirectory, "python");
      } else if (isPackagedRuntime) {
        // The embeddable CPython distribution is rooted beside its DLLs.
        pythonPath = Path.Combine(environmentRoot, "python.exe");
      } else {
        // Standard Windows virtual environments put the interpreter and
        // installed entry points together under Scripts.
        pythonPath = Path.Combine(executablesDirectory, "python.exe");
      }
      string trackerPath = Path.Combine(
        executablesDirectory, "DBNBeatTracker");
      return File.Exists(pythonPath) && File.Exists(trackerPath)
        ? new MadmomRuntimePaths(pythonPath, trackerPath)
        : null;
    }
  }
}
