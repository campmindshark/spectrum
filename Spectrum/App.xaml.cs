using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Spectrum {
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App : Application {

    // The operator loop's frame throttle (Operator.ThrottleFrame) relies on
    // Thread.Sleep landing near 1ms. Windows' default timer granularity is
    // ~15.6ms, and since Windows 10 2004 the timer resolution is per-process —
    // so we can't lean on WPF's compositor (which only raises it while actively
    // rendering, e.g. not while minimized) to keep it fine for us. Request 1ms
    // explicitly for the lifetime of the app so the throttle holds close to its
    // rate cap regardless of window state. Paired with timeEndPeriod on exit.
    private const uint TimerResolutionMs = 1;
    private static readonly object ErrorLogLock = new object();

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint timeBeginPeriod(uint uMilliseconds);

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint timeEndPeriod(uint uMilliseconds);

    protected override void OnStartup(StartupEventArgs e) {
      this.DispatcherUnhandledException += OnDispatcherUnhandledException;
      AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
      TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
      timeBeginPeriod(TimerResolutionMs);
      base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e) {
      try {
        base.OnExit(e);
      } finally {
        timeEndPeriod(TimerResolutionMs);
        this.DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
      }
    }

    private static void OnDispatcherUnhandledException(
      object sender, DispatcherUnhandledExceptionEventArgs e
    ) {
      LogException("Unhandled WPF dispatcher exception", e.Exception);
      // Logging is last-chance diagnostics, not a blanket recovery policy.
      // Leave Handled false so an unknown exception cannot continue against
      // potentially inconsistent live-show state.
    }

    private static void OnUnhandledException(
      object sender, UnhandledExceptionEventArgs e
    ) {
      LogException(
        "Unhandled application-domain exception",
        e.ExceptionObject as Exception ??
          new Exception(e.ExceptionObject?.ToString() ?? "Unknown exception")
      );
    }

    private static void OnUnobservedTaskException(
      object sender, UnobservedTaskExceptionEventArgs e
    ) {
      LogException("Unobserved task exception", e.Exception);
    }

    internal static void LogException(string context, Exception exception) {
      string entry =
        "[" + DateTimeOffset.Now.ToString("O") + "] " + context +
        Environment.NewLine + exception + Environment.NewLine;
      Debug.WriteLine(entry);

      // Error reporting must never become another crash path. LocalAppData is
      // writable even when the portable application directory is read-only.
      try {
        string directory = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          "Spectrum", "Logs"
        );
        Directory.CreateDirectory(directory);
        lock (ErrorLogLock) {
          File.AppendAllText(
            Path.Combine(directory, "spectrum-errors.log"),
            entry,
            Encoding.UTF8
          );
        }
      } catch {
        // There is nowhere safer to report a failure of the failure logger.
      }
    }
  }
}
