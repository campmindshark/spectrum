using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;

namespace Spectrum.Base {

  public sealed record ConfigurationLoadFailure(
    string Path, Exception Error);

  public sealed record ConfigurationLoadResult<T>(
    T Value,
    string SourcePath,
    IReadOnlyList<ConfigurationLoadFailure> Failures
  );

  /**
   * Loads a configuration from primary, backup, and packaged-default files and
   * saves it with an atomic same-directory replacement.
   *
   * Serialization stays outside this class so the portable host foundation
   * does not depend on a particular XML or JSON package. The caller supplies
   * stream delegates for its document format.
   */
  public sealed class ConfigurationFileStore<T> {
    private readonly Action<Stream, T> serialize;
    private readonly Func<Stream, T> deserialize;
    private readonly object saveLock = new();

    public ConfigurationFileStore(
      string primaryPath,
      string backupPath,
      string defaultPath,
      Action<Stream, T> serialize,
      Func<Stream, T> deserialize
    ) {
      this.PrimaryPath = RequiredPath(primaryPath, nameof(primaryPath));
      this.BackupPath = RequiredPath(backupPath, nameof(backupPath));
      this.DefaultPath = RequiredPath(defaultPath, nameof(defaultPath));
      this.TemporaryPath = this.PrimaryPath + ".tmp";
      this.serialize = serialize ??
        throw new ArgumentNullException(nameof(serialize));
      this.deserialize = deserialize ??
        throw new ArgumentNullException(nameof(deserialize));
    }

    public string PrimaryPath { get; }
    public string BackupPath { get; }
    public string DefaultPath { get; }
    public string TemporaryPath { get; }

    public ConfigurationLoadResult<T> Load(Func<T> createFallback) {
      if (createFallback == null) {
        throw new ArgumentNullException(nameof(createFallback));
      }

      var failures = new List<ConfigurationLoadFailure>();
      foreach (string candidate in new[] {
          this.PrimaryPath, this.BackupPath, this.DefaultPath }) {
        if (!File.Exists(candidate)) {
          continue;
        }
        try {
          using FileStream stream = File.OpenRead(candidate);
          T value = this.deserialize(stream);
          if (value == null) {
            throw new InvalidDataException(
              "The configuration deserializer returned null.");
          }
          return new ConfigurationLoadResult<T>(
            value, candidate, failures.AsReadOnly());
        } catch (Exception error) {
          failures.Add(new ConfigurationLoadFailure(candidate, error));
        }
      }

      T fallback = createFallback();
      if (fallback == null) {
        throw new InvalidOperationException(
          "The configuration fallback factory returned null.");
      }
      return new ConfigurationLoadResult<T>(
        fallback, null, failures.AsReadOnly());
    }

    public void Save(T value) {
      if (value == null) {
        throw new ArgumentNullException(nameof(value));
      }

      lock (this.saveLock) {
        Exception failure = null;
        try {
          string directory = Path.GetDirectoryName(this.PrimaryPath);
          if (!string.IsNullOrEmpty(directory)) {
            Directory.CreateDirectory(directory);
          }

          // Serialize and flush completely before changing the live file.
          // Keeping the temporary file in the same directory makes the final
          // rename/replace an atomic filesystem operation.
          using (var stream = new FileStream(
              this.TemporaryPath, FileMode.Create, FileAccess.Write,
              FileShare.None)) {
            this.serialize(stream, value);
            stream.Flush(true);
          }
          if (File.Exists(this.PrimaryPath)) {
            File.Replace(
              this.TemporaryPath, this.PrimaryPath, this.BackupPath);
          } else {
            File.Move(this.TemporaryPath, this.PrimaryPath);
          }
        } catch (Exception error) {
          failure = error;
        }

        try {
          if (File.Exists(this.TemporaryPath)) {
            File.Delete(this.TemporaryPath);
          }
        } catch (Exception cleanupError) {
          failure ??= cleanupError;
        }

        if (failure != null) {
          ExceptionDispatchInfo.Capture(failure).Throw();
        }
      }
    }

    private static string RequiredPath(string path, string parameterName) {
      if (string.IsNullOrWhiteSpace(path)) {
        throw new ArgumentException(
          "A configuration path is required.", parameterName);
      }
      return path;
    }
  }
}
