using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Spectrum.Base;

namespace Spectrum {

  /**
   * Platform-specific engine boundary owned by SpectrumHost. The portable host
   * only needs to start/stop and reboot the engine; frontends retain access to
   * their concrete runtime for telemetry and maintenance surfaces.
   */
  public interface ISpectrumHostRuntime {
    bool Enabled { get; set; }
    void Reboot();
  }

  /**
   * Optional long-lived frontend service, such as the browser control host.
   * Start failures are non-fatal so the native frontend and lighting engine can
   * remain usable when a listener cannot bind.
   */
  public interface ISpectrumHostService {
    void Start();
    Task StopAsync();
  }

  /**
   * Portable owner for the process-level Spectrum lifecycle. Frontends supply
   * concrete runtime and service factories while this class owns configuration
   * loading, reboot policy, service startup, and ordered shutdown.
   */
  public sealed class SpectrumHost<TRuntime, TService> :
    IDisposable, IAsyncDisposable
    where TRuntime : class, ISpectrumHostRuntime
    where TService : class, ISpectrumHostService {

    private readonly object lifecycleLock = new object();
    private readonly SpectrumConfigurationSession configurationSession;
    private readonly HashSet<string> rebootPropertyNames;
    private readonly Action<Exception> reportServiceStartError;
    private int lifecycleState;

    public SpectrumHost(
      ConfigurationFileStore<SpectrumConfiguration> configurationStore,
      ApplicationStateDispatcher stateDispatcher,
      TimeSpan saveDebounceDelay,
      Func<SpectrumConfiguration, ApplicationStateDispatcher, TRuntime>
        createRuntime,
      Func<
        SpectrumConfiguration,
        ApplicationStateDispatcher,
        TRuntime,
        TService> createService,
      IEnumerable<string> rebootPropertyNames = null,
      Func<bool> saveEnabled = null,
      Action<ConfigurationLoadFailure> reportLoadFailure = null,
      Action<Exception> reportSaveError = null,
      Action<Exception> reportServiceStartError = null
    ) {
      if (configurationStore == null) {
        throw new ArgumentNullException(nameof(configurationStore));
      }
      if (stateDispatcher == null) {
        throw new ArgumentNullException(nameof(stateDispatcher));
      }
      if (createRuntime == null) {
        throw new ArgumentNullException(nameof(createRuntime));
      }
      if (createService == null) {
        throw new ArgumentNullException(nameof(createService));
      }

      this.rebootPropertyNames = rebootPropertyNames == null
        ? new HashSet<string>()
        : new HashSet<string>(rebootPropertyNames);
      this.reportServiceStartError = reportServiceStartError;
      this.configurationSession = new SpectrumConfigurationSession(
        configurationStore,
        stateDispatcher,
        saveDebounceDelay,
        saveEnabled,
        reportLoadFailure,
        reportSaveError);

      TRuntime runtime = null;
      try {
        runtime = createRuntime(
          this.configurationSession.Configuration, stateDispatcher) ??
          throw new InvalidOperationException(
            "The Spectrum runtime factory returned null.");
        this.Service = createService(
          this.configurationSession.Configuration,
          stateDispatcher,
          runtime) ?? throw new InvalidOperationException(
            "The Spectrum host-service factory returned null.");
        this.Runtime = runtime;
        this.Configuration.PropertyChanged += this.ConfigurationUpdated;
      } catch {
        if (runtime != null) {
          try {
            runtime.Enabled = false;
            (runtime as IDisposable)?.Dispose();
          } catch {
            // Preserve the construction failure that made cleanup necessary.
          }
        }
        this.configurationSession.Dispose();
        throw;
      }
    }

    public SpectrumConfiguration Configuration =>
      this.configurationSession.Configuration;

    public ConfigurationLoadResult<SpectrumConfiguration> LoadResult =>
      this.configurationSession.LoadResult;

    public TRuntime Runtime { get; }

    public TService Service { get; }

    public Exception ServiceStartError { get; private set; }

    /**
     * Starts the optional frontend service. A bind/start failure is recorded
     * and reported without terminating the runtime.
     */
    public void Start() {
      lock (this.lifecycleLock) {
        if (this.lifecycleState == 2) {
          throw new ObjectDisposedException(this.GetType().Name);
        }
        if (this.lifecycleState != 0) {
          throw new InvalidOperationException(
            "The Spectrum host has already been started.");
        }
        this.lifecycleState = 1;
      }

      try {
        this.Service.Start();
        this.ServiceStartError = null;
      } catch (Exception error) {
        this.ServiceStartError = error;
        this.reportServiceStartError?.Invoke(error);
      }
    }

    private void ConfigurationUpdated(
      object sender, PropertyChangedEventArgs e
    ) {
      if (e.PropertyName != null &&
          this.rebootPropertyNames.Contains(e.PropertyName)) {
        this.Runtime.Reboot();
      }
    }

    public void Dispose() {
      if (!this.BeginDispose()) {
        return;
      }

      var failures = new List<Exception>();
      try {
        this.Service.StopAsync().ConfigureAwait(false)
          .GetAwaiter().GetResult();
      } catch (Exception error) {
        failures.Add(error);
      }
      this.CompleteDispose(failures);
    }

    public async ValueTask DisposeAsync() {
      if (!this.BeginDispose()) {
        return;
      }

      var failures = new List<Exception>();
      try {
        await this.Service.StopAsync().ConfigureAwait(false);
      } catch (Exception error) {
        failures.Add(error);
      }
      this.CompleteDispose(failures);
    }

    private bool BeginDispose() {
      lock (this.lifecycleLock) {
        if (this.lifecycleState == 2) {
          return false;
        }
        this.lifecycleState = 2;
      }
      this.Configuration.PropertyChanged -= this.ConfigurationUpdated;
      return true;
    }

    private void CompleteDispose(List<Exception> failures) {
      try {
        this.Runtime.Enabled = false;
      } catch (Exception error) {
        failures.Add(error);
      }
      try {
        (this.Runtime as IDisposable)?.Dispose();
      } catch (Exception error) {
        failures.Add(error);
      }
      try {
        this.configurationSession.Dispose();
      } catch (Exception error) {
        failures.Add(error);
      }

      if (failures.Count == 1) {
        throw failures[0];
      }
      if (failures.Count > 1) {
        throw new AggregateException(
          "Spectrum host shutdown encountered multiple failures.",
          failures);
      }
    }
  }
}
