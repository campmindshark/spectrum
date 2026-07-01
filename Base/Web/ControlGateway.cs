using System;
using System.Threading.Tasks;

namespace Spectrum.Base {

  /**
   * The single choke point through which every web-originated mutation of the
   * shared Configuration is serialized.
   *
   * Why this exists (see docs/web_architecture.md, "The threading boundary"):
   * SpectrumConfiguration.SetField fires PropertyChanged synchronously on the
   * calling thread, and its subscribers assume that thread is the WPF UI thread
   * (WPF bindings can only touch controls there, and the Operator's lock-free
   * reads assume well-behaved scalar writes). A Kestrel request handler runs on
   * an arbitrary thread-pool thread, so it must not write Configuration
   * directly. Every web write instead goes through this gateway.
   *
   * Callers depend only on this interface, never on the Dispatcher. The first
   * implementation (DispatcherControlGateway) marshals onto the WPF Dispatcher
   * so a web write becomes indistinguishable from a UI write. When the native
   * GUI is eventually retired, the internals can be swapped for a dedicated
   * single-thread / Channel<T> serializer with no caller changes.
   */
  public interface ControlGateway {

    /**
     * Queue a mutation to run on the serialization thread. Returns immediately
     * without waiting for the mutation to run. Use for fire-and-forget writes
     * where the caller doesn't need the result or to observe exceptions.
     */
    void Post(Action mutation);

    /**
     * Queue a mutation and return a Task that completes (or faults) when the
     * mutation has run on the serialization thread. Use from async request
     * handlers that need to report success/failure back to the client.
     */
    Task InvokeAsync(Action mutation);
  }
}
