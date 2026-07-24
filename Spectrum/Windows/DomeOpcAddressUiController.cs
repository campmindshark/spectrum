using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Spectrum {

  /**
   * Owns the editable OPC-address field. Configuration changes can arrive from
   * the web host, so synchronization is marshalled to the WPF dispatcher and
   * never overwrites an active keyboard edit.
   */
  internal sealed class DomeOpcAddressUiController : IDisposable {
    private readonly SpectrumConfiguration config;
    private readonly Dispatcher dispatcher;
    private readonly TextBox address;
    private readonly TextBlock validationStatus;
    private readonly Func<string, object> findResource;
    private bool started;
    private bool disposed;

    internal DomeOpcAddressUiController(
      SpectrumConfiguration config,
      Dispatcher dispatcher,
      TextBox address,
      TextBlock validationStatus,
      Func<string, object> findResource
    ) {
      this.config = config;
      this.dispatcher = dispatcher;
      this.address = address;
      this.validationStatus = validationStatus;
      this.findResource = findResource;
    }

    internal void Start() {
      if (this.disposed || this.started) {
        return;
      }
      this.started = true;
      this.config.PropertyChanged += this.ConfigurationUpdated;
      this.SynchronizeFromConfiguration();
    }

    public void Dispose() {
      if (this.disposed) {
        return;
      }
      this.disposed = true;
      if (this.started) {
        this.config.PropertyChanged -= this.ConfigurationUpdated;
      }
    }

    internal void SynchronizeFromConfiguration() {
      if (this.disposed || this.address.IsKeyboardFocusWithin) {
        return;
      }
      string configuredAddress =
        this.config.domeBeagleboneOPCAddress ?? "";
      if (this.address.Text != configuredAddress) {
        this.address.Text = configuredAddress;
      }
      this.ShowValidation();
    }

    internal void ShowValidation() {
      if (this.disposed) {
        return;
      }
      if (TryNormalize(
          this.address.Text, out _, out string? error)) {
        this.validationStatus.Text = "Valid host and port format.";
        this.validationStatus.Foreground =
          (Brush)this.findResource("SuccessBrush");
      } else {
        this.validationStatus.Text = "Error: " + error + ".";
        this.validationStatus.Foreground =
          (Brush)this.findResource("ErrorBrush");
      }
    }

    internal void CommitAddress() {
      if (this.disposed ||
          !TryNormalize(
            this.address.Text, out string? normalized, out _)) {
        return;
      }
      this.address.Text = normalized;
      this.config.domeBeagleboneOPCAddress = normalized;
    }

    private static bool TryNormalize(
      string value,
      [NotNullWhen(true)] out string? normalized,
      [NotNullWhen(false)] out string? error
    ) {
      try {
        normalized = Web.SpectrumParameters.NormalizeOpcAddress(value);
        error = null;
        return true;
      } catch (ArgumentException validationError) {
        normalized = null;
        error = validationError.Message;
        return false;
      }
    }

    private void ConfigurationUpdated(
      object? sender,
      PropertyChangedEventArgs change
    ) {
      if (this.disposed ||
          change.PropertyName !=
            nameof(this.config.domeBeagleboneOPCAddress)) {
        return;
      }
      this.dispatcher.BeginInvoke(
        new Action(this.SynchronizeFromConfiguration));
    }
  }
}
