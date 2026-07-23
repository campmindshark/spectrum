using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Spectrum.Base {

  /**
   * Owns application state on a dedicated, platform-neutral thread.
   *
   * Headless frontends use this in place of WPF's Dispatcher. Commands are
   * processed in publication order, and Dispose drains every command accepted
   * before shutdown. Exceptions from Post commands are reported without
   * terminating the state-owner thread; exceptions from InvokeAsync commands
   * are returned through their Tasks.
   */
  public sealed class DedicatedThreadApplicationStateDispatcher :
    ApplicationStateDispatcher, IDisposable {

    private readonly BlockingCollection<Action> commands = new();
    private readonly ManualResetEventSlim started = new(false);
    private readonly Thread ownerThread;
    private readonly Action<Exception> reportUnhandledException;
    private int ownerThreadId;
    private int disposeStarted;

    public DedicatedThreadApplicationStateDispatcher(
      string threadName = "Spectrum application state",
      Action<Exception>? reportUnhandledException = null
    ) {
      if (string.IsNullOrWhiteSpace(threadName)) {
        throw new ArgumentException(
          "The state-owner thread must have a name.", nameof(threadName));
      }
      this.reportUnhandledException = reportUnhandledException ??
        (error => Trace.TraceError(error.ToString()));
      this.ownerThread = new Thread(this.Run) {
        IsBackground = true,
        Name = threadName,
      };
      this.ownerThread.Start();
      this.started.Wait();
    }

    public bool CheckAccess() =>
      Environment.CurrentManagedThreadId == Volatile.Read(
        ref this.ownerThreadId);

    public void Post(Action mutation) {
      if (mutation == null) {
        throw new ArgumentNullException(nameof(mutation));
      }
      this.Enqueue(mutation);
    }

    public Task InvokeAsync(Action mutation) {
      if (mutation == null) {
        throw new ArgumentNullException(nameof(mutation));
      }
      if (this.CheckAccess()) {
        try {
          mutation();
          return Task.CompletedTask;
        } catch (Exception error) {
          return Task.FromException(error);
        }
      }

      var completion = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
      this.Enqueue(() => {
        try {
          mutation();
          completion.SetResult();
        } catch (Exception error) {
          completion.SetException(error);
        }
      });
      return completion.Task;
    }

    public Task<T> InvokeAsync<T>(Func<T> read) {
      if (read == null) {
        throw new ArgumentNullException(nameof(read));
      }
      if (this.CheckAccess()) {
        try {
          return Task.FromResult(read());
        } catch (Exception error) {
          return Task.FromException<T>(error);
        }
      }

      var completion = new TaskCompletionSource<T>(
        TaskCreationOptions.RunContinuationsAsynchronously);
      this.Enqueue(() => {
        try {
          completion.SetResult(read());
        } catch (Exception error) {
          completion.SetException(error);
        }
      });
      return completion.Task;
    }

    public void Dispose() {
      if (Interlocked.Exchange(ref this.disposeStarted, 1) == 0) {
        this.commands.CompleteAdding();
      }
      if (!this.CheckAccess()) {
        this.ownerThread.Join();
      }
    }

    private void Enqueue(Action command) {
      if (Volatile.Read(ref this.disposeStarted) != 0) {
        throw new ObjectDisposedException(this.GetType().Name);
      }
      try {
        this.commands.Add(command);
      } catch (ObjectDisposedException) {
        throw new ObjectDisposedException(this.GetType().Name);
      } catch (InvalidOperationException) {
        throw new ObjectDisposedException(this.GetType().Name);
      }
    }

    private void Run() {
      Volatile.Write(
        ref this.ownerThreadId, Environment.CurrentManagedThreadId);
      this.started.Set();
      try {
        foreach (Action command in this.commands.GetConsumingEnumerable()) {
          try {
            command();
          } catch (Exception error) {
            try {
              this.reportUnhandledException(error);
            } catch (Exception reportingError) {
              Trace.TraceError(reportingError.ToString());
            }
          }
        }
      } finally {
        Volatile.Write(ref this.ownerThreadId, 0);
      }
    }
  }
}
