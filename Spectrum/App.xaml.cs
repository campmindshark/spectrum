using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;

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

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint timeBeginPeriod(uint uMilliseconds);

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint timeEndPeriod(uint uMilliseconds);

    protected override void OnStartup(StartupEventArgs e) {
      timeBeginPeriod(TimerResolutionMs);
      base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e) {
      timeEndPeriod(TimerResolutionMs);
      base.OnExit(e);
    }
  }
}
