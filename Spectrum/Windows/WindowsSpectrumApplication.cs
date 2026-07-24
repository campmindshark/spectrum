using System;
using System.Diagnostics;
using System.Windows.Threading;
using Spectrum.Base;
using XSerializer;

namespace Spectrum {

  /**
   * Windows composition boundary for the portable Spectrum host. The WPF
   * window owns control-specific controllers; this class owns persistent
   * configuration paths, platform runtime/service construction, and the
   * process-level host lifecycle.
   */
  internal sealed class WindowsSpectrumApplication : IDisposable {
    public const int WebServerPort = 8080;

    private static readonly SpectrumConfigurationPaths ConfigPaths =
      SpectrumConfigurationPaths.ForPortableDesktop(
        AppContext.BaseDirectory);
    private static readonly ConfigurationFileStore<SpectrumConfiguration>
      ConfigStore = new ConfigurationFileStore<SpectrumConfiguration>(
        ConfigPaths.PrimaryPath,
        ConfigPaths.BackupPath,
        ConfigPaths.DefaultPath,
        (stream, value) =>
          new XmlSerializer<SpectrumConfigurationDocument>().Serialize(
            stream,
            SpectrumConfigurationDocument.FromConfiguration(value)),
        stream => new XmlSerializer<SpectrumConfigurationDocument>()
          .Deserialize(stream).ToConfiguration());

    private readonly SpectrumHost<Operator, Web.SpectrumWebHost> host;

    public WindowsSpectrumApplication(
      Dispatcher dispatcher,
      Func<bool> saveEnabled
    ) {
      if (dispatcher == null) {
        throw new ArgumentNullException(nameof(dispatcher));
      }
      if (saveEnabled == null) {
        throw new ArgumentNullException(nameof(saveEnabled));
      }

      ApplicationStateDispatcher applicationStateDispatcher =
        new DispatcherApplicationStateDispatcher(dispatcher);
      this.host = new SpectrumHost<Operator, Web.SpectrumWebHost>(
        ConfigStore,
        applicationStateDispatcher,
        TimeSpan.FromMilliseconds(100),
        (config, stateDispatcher) => new Operator(
          config,
          stateDispatcher,
          new WindowsSpectrumInputFactory()),
        (config, stateDispatcher, runtime) => new Web.SpectrumWebHost(
          config,
          stateDispatcher,
          runtime,
          WebServerPort,
          nativeWindowControlsAvailable: true,
          reportBackgroundError: error => App.LogException(
            "Web background task failed",
            error)),
        SpectrumConfigurationSchema.RestartPropertyNames,
        saveEnabled,
        reportLoadFailure: failure => Debug.WriteLine(
          "Failed to load configuration from " + failure.Path + ": " +
          failure.Error),
        reportSaveError: error => App.LogException(
          "Could not save Spectrum configuration",
          error),
        reportServiceStartError: error => Debug.WriteLine(
          "Web controller failed to start: " + error));
    }

    public SpectrumConfiguration Configuration => this.host.Configuration;

    public Operator Runtime => this.host.Runtime;

    public Web.SpectrumWebHost WebHost => this.host.Service;

    public Exception? ServiceStartError => this.host.ServiceStartError;

    public void Start() {
      this.host.Start();
    }

    public void Dispose() {
      this.host.Dispose();
    }
  }
}
