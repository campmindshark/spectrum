using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using Spectrum.Base;

namespace Spectrum.Web {

  /**
   * The first ControlGateway implementation: marshals every web-originated
   * mutation onto the WPF Dispatcher (the UI thread). This makes a web write
   * indistinguishable from a UI write, which preserves for free:
   *
   *   - the debounced config save wired to Configuration.PropertyChanged in
   *     MainWindow (EventuallySaveConfig), and
   *   - the Operator thread's lock-free read assumptions (writes land on the
   *     same thread they always have).
   *
   * When the native GUI is retired this can be replaced by a dedicated
   * single-thread / Channel<T> serializer without touching any caller.
   */
  public class DispatcherControlGateway : ControlGateway {

    private readonly Dispatcher dispatcher;

    public DispatcherControlGateway(Dispatcher dispatcher) {
      this.dispatcher = dispatcher;
    }

    public void Post(Action mutation) {
      this.dispatcher.BeginInvoke(mutation);
    }

    public Task InvokeAsync(Action mutation) {
      return this.dispatcher.InvokeAsync(mutation).Task;
    }
  }
}
