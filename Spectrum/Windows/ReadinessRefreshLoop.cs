using System;
using System.Windows.Threading;

namespace Spectrum {

  // Owns readiness refresh scheduling and the Operator event subscription so
  // MainWindow does not manage another timer/subscription shutdown pair.
  internal sealed class ReadinessRefreshLoop : IDisposable {
    private readonly Operator runtime;
    private readonly Dispatcher dispatcher;
    private readonly Action refresh;
    private readonly DispatcherTimer timer;
    private bool disposed;

    internal ReadinessRefreshLoop(
      Operator runtime,
      Dispatcher dispatcher,
      Action refresh
    ) {
      this.runtime = runtime;
      this.dispatcher = dispatcher;
      this.refresh = refresh;
      this.runtime.EnabledChanged += this.OperatorEnabledChanged;
      this.timer = new DispatcherTimer {
        Interval = TimeSpan.FromMilliseconds(500),
      };
      this.timer.Tick += this.TimerTick;
      this.timer.Start();
      this.refresh();
    }

    public void Dispose() {
      if (this.disposed) {
        return;
      }
      this.disposed = true;
      this.runtime.EnabledChanged -= this.OperatorEnabledChanged;
      this.timer.Stop();
      this.timer.Tick -= this.TimerTick;
    }

    private void OperatorEnabledChanged(bool enabled) {
      this.dispatcher.BeginInvoke(this.refresh);
    }

    private void TimerTick(object? sender, EventArgs e) {
      this.refresh();
    }
  }
}
