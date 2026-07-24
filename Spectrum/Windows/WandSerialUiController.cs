using System;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Spectrum {

  // Owns the WPF lifecycle for wand-port selection and receiver liveness.
  // MainWindow forwards XAML events while the UI-neutral presentation model
  // remains the single source of port/status branch policy.
  internal sealed class WandSerialUiController : IDisposable {
    private readonly SpectrumConfiguration configuration;
    private readonly ComboBox portSelector;
    private readonly TextBlock status;
    private readonly Func<string[]> availablePorts;
    private readonly Func<WandSerialStatus> statusSource;
    private readonly DispatcherTimer refreshTimer;
    private bool started;
    private bool repopulatingPorts;
    private bool disposed;

    internal WandSerialUiController(
      SpectrumConfiguration configuration,
      ComboBox portSelector,
      TextBlock status,
      Func<string[]> availablePorts,
      Func<WandSerialStatus> statusSource
    ) {
      this.configuration = configuration ??
        throw new ArgumentNullException(nameof(configuration));
      this.portSelector = portSelector ??
        throw new ArgumentNullException(nameof(portSelector));
      this.status = status ??
        throw new ArgumentNullException(nameof(status));
      this.availablePorts = availablePorts ??
        throw new ArgumentNullException(nameof(availablePorts));
      this.statusSource = statusSource ??
        throw new ArgumentNullException(nameof(statusSource));
      this.refreshTimer = new DispatcherTimer {
        Interval = TimeSpan.FromMilliseconds(500),
      };
      this.refreshTimer.Tick += this.RefreshStatus;
    }

    internal void Start() {
      this.ThrowIfDisposed();
      if (this.started) {
        return;
      }
      this.started = true;
      this.configuration.PropertyChanged += this.ConfigurationUpdated;
      this.RepopulatePorts();
      this.RefreshStatus(null, EventArgs.Empty);
      this.refreshTimer.Start();
    }

    // Rebuilds (none) + live ports while retaining a configured missing port.
    // Programmatic selection is guarded so only a genuine user pick writes
    // configuration back.
    internal void RepopulatePorts() {
      if (this.disposed) {
        return;
      }
      this.repopulatingPorts = true;
      try {
        this.portSelector.Items.Clear();
        string configured = this.configuration.wandSerialPort ?? "";
        foreach (WandSerialPortOption option in
            WandSerialPresentationModel.BuildPortOptions(
              configured,
              this.availablePorts())) {
          this.portSelector.Items.Add(option);
        }

        foreach (WandSerialPortOption item in this.portSelector.Items) {
          if (item.Value == configured) {
            this.portSelector.SelectedItem = item;
            break;
          }
        }
      } finally {
        this.repopulatingPorts = false;
      }
    }

    internal void ApplySelectedPort() {
      if (this.disposed || this.repopulatingPorts) {
        return;
      }
      if (this.portSelector.SelectedItem is WandSerialPortOption item) {
        this.configuration.wandSerialPort = item.Value;
      }
    }

    private void RefreshStatus(object? sender, EventArgs e) {
      if (this.disposed) {
        return;
      }
      WandSerialPresentation presentation =
        WandSerialPresentationModel.EvaluateStatus(
          this.configuration.wandSerialPort,
          this.statusSource());
      this.status.Text = presentation.Text;
      this.status.Foreground = presentation.Kind switch {
        WandSerialPresentationKind.Inactive => Brushes.Gray,
        WandSerialPresentationKind.Ready => Brushes.ForestGreen,
        WandSerialPresentationKind.Warning => Brushes.OrangeRed,
        WandSerialPresentationKind.Error => Brushes.OrangeRed,
        _ => throw new ArgumentOutOfRangeException(
          nameof(presentation),
          presentation.Kind,
          "Unknown wand receiver presentation."),
      };
    }

    public void Dispose() {
      if (this.disposed) {
        return;
      }
      this.disposed = true;
      this.configuration.PropertyChanged -= this.ConfigurationUpdated;
      this.refreshTimer.Stop();
      this.refreshTimer.Tick -= this.RefreshStatus;
    }

    private void ConfigurationUpdated(
      object? sender,
      PropertyChangedEventArgs change
    ) {
      if (this.disposed ||
          change.PropertyName !=
            nameof(this.configuration.wandSerialPort)) {
        return;
      }
      this.portSelector.Dispatcher.BeginInvoke(
        new Action(this.RepopulatePorts));
    }

    private void ThrowIfDisposed() {
      ObjectDisposedException.ThrowIf(this.disposed, this);
    }
  }
}
