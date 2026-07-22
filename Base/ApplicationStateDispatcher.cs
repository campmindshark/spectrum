using System;
using System.Threading.Tasks;

namespace Spectrum.Base {

  /**
   * The single writer for persisted application state.
   *
   * Native UI handlers already execute on this dispatcher's owner thread.
   * Every other producer (web requests, MIDI callbacks, and operator/runtime
   * housekeeping) publishes a command through this interface before touching
   * Configuration. SpectrumConfiguration also uses CheckAccess as a final
   * guard, so an accidental off-thread setter call is queued rather than
   * delivering PropertyChanged on the wrong thread.
   */
  public interface ApplicationStateDispatcher {

    // True only while the caller is executing on the state-owner thread.
    bool CheckAccess();

    // Queue a command without waiting for it to execute.
    void Post(Action mutation);

    // Queue a command and complete when it has executed (or faulted).
    Task InvokeAsync(Action mutation);

    // Capture a compound application projection on the owner thread. Web
    // request handlers use this for DTO reads just as they use the Action
    // overload for writes, so one response cannot mix owner-side generations.
    Task<T> InvokeAsync<T>(Func<T> read);
  }
}
