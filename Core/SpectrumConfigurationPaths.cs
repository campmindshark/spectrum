using System;
using System.IO;

namespace Spectrum {

  /**
   * Host-selected locations for the live, recovery, and packaged-default
   * configuration files.
   */
  public sealed record SpectrumConfigurationPaths(
    string PrimaryPath,
    string BackupPath,
    string DefaultPath
  ) {
    private const string PrimaryFileName = "spectrum_config.xml";
    private const string BackupFileName = "spectrum_old_config.xml";

    public static SpectrumConfigurationPaths ForPortableDesktop(
      string applicationDirectory
    ) => InDataDirectory(
      applicationDirectory,
      Path.Combine(applicationDirectory, "spectrum_default_config.xml"));

    /**
     * Selects a writable headless-host directory. An explicit argument wins,
     * followed by SPECTRUM_DATA_DIR. Linux/macOS then use XDG_CONFIG_HOME or
     * ~/.config/spectrum; Windows uses LocalApplicationData/Spectrum.
     */
    public static SpectrumConfigurationPaths ForHeadlessHost(
      string packagedDefaultPath,
      string dataDirectory = null
    ) => ForHeadlessHost(
      packagedDefaultPath,
      dataDirectory,
      Environment.GetEnvironmentVariable,
      Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
      Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData),
      OperatingSystem.IsWindows());

    internal static SpectrumConfigurationPaths ForHeadlessHost(
      string packagedDefaultPath,
      string dataDirectory,
      Func<string, string> getEnvironmentVariable,
      string userProfileDirectory,
      string localApplicationDataDirectory,
      bool isWindows
    ) {
      if (string.IsNullOrWhiteSpace(packagedDefaultPath)) {
        throw new ArgumentException(
          "A packaged default configuration path is required.",
          nameof(packagedDefaultPath));
      }
      if (getEnvironmentVariable == null) {
        throw new ArgumentNullException(nameof(getEnvironmentVariable));
      }

      string selectedDirectory = dataDirectory;
      if (string.IsNullOrWhiteSpace(selectedDirectory)) {
        selectedDirectory = getEnvironmentVariable("SPECTRUM_DATA_DIR");
      }
      if (string.IsNullOrWhiteSpace(selectedDirectory)) {
        if (isWindows) {
          if (string.IsNullOrWhiteSpace(localApplicationDataDirectory)) {
            throw new InvalidOperationException(
              "The local application-data directory is unavailable.");
          }
          selectedDirectory = Path.Combine(
            localApplicationDataDirectory, "Spectrum");
        } else {
          string xdgConfigHome =
            getEnvironmentVariable("XDG_CONFIG_HOME");
          if (string.IsNullOrWhiteSpace(xdgConfigHome) ||
              !Path.IsPathFullyQualified(xdgConfigHome)) {
            if (string.IsNullOrWhiteSpace(userProfileDirectory)) {
              throw new InvalidOperationException(
                "Neither XDG_CONFIG_HOME nor the user profile is available.");
            }
            xdgConfigHome = Path.Combine(userProfileDirectory, ".config");
          }
          selectedDirectory = Path.Combine(xdgConfigHome, "spectrum");
        }
      }

      return InDataDirectory(selectedDirectory, packagedDefaultPath);
    }

    private static SpectrumConfigurationPaths InDataDirectory(
      string dataDirectory,
      string defaultPath
    ) {
      if (string.IsNullOrWhiteSpace(dataDirectory)) {
        throw new ArgumentException(
          "A configuration data directory is required.",
          nameof(dataDirectory));
      }
      if (string.IsNullOrWhiteSpace(defaultPath)) {
        throw new ArgumentException(
          "A default configuration path is required.", nameof(defaultPath));
      }
      return new SpectrumConfigurationPaths(
        Path.Combine(dataDirectory, PrimaryFileName),
        Path.Combine(dataDirectory, BackupFileName),
        defaultPath);
    }
  }
}
