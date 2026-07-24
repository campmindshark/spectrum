using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Spectrum.Base {

  /**
   * Debounces configuration changes and persists them on the application-state
   * owner thread.
   *
   * Serializing on that thread captures one coherent mutable configuration
   * generation. The timer itself is platform-neutral and only requests work;
   * it never reads application state from its callback thread.
   */
  public sealed class ConfigurationPersistenceCoordinator<T> : IDisposable
      where T : class, INotifyPropertyChanged {

    private readonly T value;
    private readonly ConfigurationFileStore<T> store;
    private readonly ApplicationStateDispatcher stateDispatcher;
    private readonly TimeSpan debounceDelay;
    private readonly Func<string?, bool> shouldSaveProperty;
    private readonly Func<bool> saveEnabled;
    private readonly Action<Exception> reportSaveError;
    private readonly object gate = new();
    private Timer? timer;
    private readonly List<Task> timerDisposals = new();
    private long generation;
    private bool savePending;
    private bool disposed;

    public ConfigurationPersistenceCoordinator(
      T value,
      ConfigurationFileStore<T> store,
      ApplicationStateDispatcher stateDispatcher,
      TimeSpan debounceDelay,
      Func<string?, bool>? shouldSaveProperty = null,
      Func<bool>? saveEnabled = null,
      Action<Exception>? reportSaveError = null
    ) {
      this.value = value ?? throw new ArgumentNullException(nameof(value));
      this.store = store ?? throw new ArgumentNullException(nameof(store));
      this.stateDispatcher = stateDispatcher ??
        throw new ArgumentNullException(nameof(stateDispatcher));
      if (debounceDelay < TimeSpan.Zero) {
        throw new ArgumentOutOfRangeException(
          nameof(debounceDelay), "The debounce delay cannot be negative.");
      }
      this.debounceDelay = debounceDelay;
      this.shouldSaveProperty = shouldSaveProperty ?? (_ => true);
      this.saveEnabled = saveEnabled ?? (() => true);
      this.reportSaveError = reportSaveError ??
        (error => Trace.TraceError(error.ToString()));
      this.value.PropertyChanged += this.ValueChanged;
    }

    public void SaveNow() {
      Task timerCallbacks;
      lock (this.gate) {
        this.ThrowIfDisposed();
        timerCallbacks = this.CancelPendingSaveLocked();
      }
      this.JoinTimerCallbacks(timerCallbacks);
      this.SaveOnOwnerThread();
    }

    public void Dispose() {
      Task timerCallbacks;
      lock (this.gate) {
        if (this.disposed) {
          return;
        }
        this.disposed = true;
        this.value.PropertyChanged -= this.ValueChanged;
        timerCallbacks = this.CancelPendingSaveLocked();
      }

      this.JoinTimerCallbacks(timerCallbacks);
      // Match the desktop host's existing close behavior: always attempt one
      // final save, even when no debounce timer is pending.
      this.SaveOnOwnerThread();
    }

    private void ValueChanged(
      object? sender, PropertyChangedEventArgs eventArgs
    ) {
      if (!this.shouldSaveProperty(eventArgs.PropertyName)) {
        return;
      }
      // Suppression is checked when the change occurs as well as when the
      // timer fires. This prevents initial UI binding/population work from
      // becoming a delayed save immediately after loading completes.
      if (!this.saveEnabled()) {
        return;
      }

      lock (this.gate) {
        // An event already in flight during Dispose should not make a property
        // setter fail merely because the persistence listener was removed.
        if (this.disposed) {
          return;
        }
        this.savePending = true;
        long scheduledGeneration = ++this.generation;
        this.RetireTimerLocked();
        this.timer = new Timer(
          this.DebounceElapsed,
          scheduledGeneration,
          Timeout.InfiniteTimeSpan,
          Timeout.InfiniteTimeSpan);
        this.timer.Change(
          this.debounceDelay,
          Timeout.InfiniteTimeSpan);
      }
    }

    private void DebounceElapsed(object? state) {
      long scheduledGeneration = (long)(
        state ?? throw new InvalidOperationException(
          "The configuration debounce generation was not supplied."));
      try {
        this.stateDispatcher.Post(
          () => this.SaveIfCurrent(scheduledGeneration));
      } catch (Exception error) {
        lock (this.gate) {
          if (this.disposed) {
            return;
          }
        }
        this.ReportError(error);
      }
    }

    private void SaveIfCurrent(long scheduledGeneration) {
      lock (this.gate) {
        if (this.disposed || !this.savePending ||
            scheduledGeneration != this.generation) {
          return;
        }
        this.savePending = false;
        this.RetireTimerLocked();
      }
      this.SaveCore();
    }

    private void SaveOnOwnerThread() {
      if (this.stateDispatcher.CheckAccess()) {
        this.SaveCore();
        return;
      }
      try {
        this.stateDispatcher.InvokeAsync(this.SaveCore)
          .GetAwaiter().GetResult();
      } catch (Exception error) {
        this.ReportError(error);
      }
    }

    private void SaveCore() {
      try {
        if (!this.saveEnabled()) {
          return;
        }
        this.store.Save(this.value);
      } catch (Exception error) {
        this.ReportError(error);
      }
    }

    private Task CancelPendingSaveLocked() {
      this.savePending = false;
      this.generation++;
      this.RetireTimerLocked();
      return Task.WhenAll(this.timerDisposals);
    }

    private void RetireTimerLocked() {
      if (this.timer != null) {
        this.timerDisposals.Add(this.timer.DisposeAsync().AsTask());
      }
      this.timer = null;
      this.timerDisposals.RemoveAll(
        disposal => disposal.IsCompletedSuccessfully);
    }

    private void JoinTimerCallbacks(Task timerCallbacks) {
      try {
        timerCallbacks.GetAwaiter().GetResult();
      } catch (Exception error) {
        this.ReportError(error);
      }
    }

    private void ReportError(Exception error) {
      try {
        this.reportSaveError(error);
      } catch (Exception reportingError) {
        Trace.TraceError(reportingError.ToString());
      }
    }

    private void ThrowIfDisposed() {
      if (this.disposed) {
        throw new ObjectDisposedException(this.GetType().Name);
      }
    }
  }
}
