using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using Spectrum.Base;

namespace Spectrum {

  /**
   * ApplicationStateDispatcher implementation backed by the WPF Dispatcher.
   * Native UI writes already run here; web, MIDI, and runtime commands are
   * marshalled here so every persisted mutation and PropertyChanged delivery
   * has the same owner thread. This also preserves the debounced configuration
   * save and the operator's immutable publication boundaries.
   *
   * When the native GUI is retired this can be replaced by a dedicated
   * single-thread / Channel<T> serializer without touching any producer.
   */
  public sealed class DispatcherApplicationStateDispatcher :
    ApplicationStateDispatcher {

    private readonly Dispatcher dispatcher;

    public DispatcherApplicationStateDispatcher(Dispatcher dispatcher) {
      this.dispatcher = dispatcher ??
        throw new ArgumentNullException(nameof(dispatcher));
    }

    public bool CheckAccess() => this.dispatcher.CheckAccess();

    public void Post(Action mutation) {
      this.dispatcher.BeginInvoke(mutation);
    }

    public Task InvokeAsync(Action mutation) {
      return this.dispatcher.InvokeAsync(mutation).Task;
    }
  }
}
