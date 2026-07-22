using System;
using Spectrum.Base;

namespace Spectrum {

  /**
   * Portable owner for one loaded configuration and its persistence lifecycle.
   * Frontends provide their dispatcher, paths, and document serializer; the
   * load order, owner-thread attachment, debounce, and shutdown flush are shared.
   */
  public sealed class SpectrumConfigurationSession : IDisposable {
    private readonly ConfigurationPersistenceCoordinator<
      SpectrumConfiguration> persistence;

    public SpectrumConfigurationSession(
      ConfigurationFileStore<SpectrumConfiguration> store,
      ApplicationStateDispatcher stateDispatcher,
      TimeSpan debounceDelay,
      Func<bool> saveEnabled = null,
      Action<ConfigurationLoadFailure> reportLoadFailure = null,
      Action<Exception> reportSaveError = null
    ) {
      if (store == null) {
        throw new ArgumentNullException(nameof(store));
      }
      if (stateDispatcher == null) {
        throw new ArgumentNullException(nameof(stateDispatcher));
      }

      this.LoadResult = store.Load(() => new SpectrumConfiguration());
      this.Configuration = this.LoadResult.Value;
      if (reportLoadFailure != null) {
        foreach (ConfigurationLoadFailure failure in
            this.LoadResult.Failures) {
          reportLoadFailure(failure);
        }
      }

      if (stateDispatcher.CheckAccess()) {
        this.Configuration.AttachMutationDispatcher(stateDispatcher);
      } else {
        stateDispatcher.InvokeAsync(
          () => this.Configuration.AttachMutationDispatcher(stateDispatcher))
          .GetAwaiter().GetResult();
      }

      this.persistence = new ConfigurationPersistenceCoordinator<
        SpectrumConfiguration>(
          this.Configuration,
          store,
          stateDispatcher,
          debounceDelay,
          // This notification publishes derived immutable runtime state after
          // a persisted property already changed. Saving it again is redundant.
          propertyName => propertyName !=
            DomeShowStateSnapshot.NotificationPropertyName,
          saveEnabled,
          reportSaveError);
    }

    public SpectrumConfiguration Configuration { get; }

    public ConfigurationLoadResult<SpectrumConfiguration> LoadResult {
      get;
    }

    public void SaveNow() => this.persistence.SaveNow();

    public void Dispose() => this.persistence.Dispose();
  }
}
