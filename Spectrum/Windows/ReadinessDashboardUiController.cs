using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Spectrum.Audio;
using Spectrum.Base;

namespace Spectrum {

  internal sealed record ReadinessBadgeView(
    Border Badge,
    TextBlock Text,
    TextBlock? Detail = null
  );

  internal sealed record ReadinessDashboardView(
    Button PowerButton,
    ComboBox AudioDevices,
    ReadinessBadgeView Engine,
    ReadinessBadgeView Audio,
    TextBlock AudioSignal,
    ReadinessBadgeView Dome,
    ReadinessBadgeView Wand,
    TextBlock ConnectedWandCount,
    TextBlock WebControllerAddress,
    TextBlock WebControllerStatus,
    ReadinessBadgeView Overall
  );

  /**
   * Owns the WPF home-dashboard lifecycle. MainWindow supplies the generated
   * controls and forwards explicit refresh requests, while this controller
   * captures runtime facts, evaluates readiness, applies styles/text, and owns
   * periodic/operator-event refresh teardown.
   */
  internal sealed class ReadinessDashboardUiController : IDisposable {
    private readonly SpectrumConfiguration config;
    private readonly Operator runtime;
    private readonly ReadinessDashboardView view;
    private readonly Func<string, object> findResource;
    private readonly string? webServerError;
    private readonly int webServerPort;
    private readonly ReadinessRefreshLoop refreshLoop;
    private bool disposed;

    internal ReadinessDashboardUiController(
      SpectrumConfiguration config,
      Operator runtime,
      Dispatcher dispatcher,
      ReadinessDashboardView view,
      Func<string, object> findResource,
      string? webServerError,
      int webServerPort,
      Func<string>? resolveControllerHost = null
    ) {
      this.config = config;
      this.runtime = runtime;
      this.view = view;
      this.findResource = findResource;
      this.webServerError = webServerError;
      this.webServerPort = webServerPort;

      string controllerHost =
        (resolveControllerHost ?? ResolveControllerHost)();
      this.view.WebControllerAddress.Text =
        $"http://{controllerHost}:{this.webServerPort}";
      this.refreshLoop = new ReadinessRefreshLoop(
        this.runtime, dispatcher, this.Refresh);
    }

    public void Dispose() {
      if (this.disposed) {
        return;
      }
      this.disposed = true;
      this.refreshLoop.Dispose();
    }

    internal void Refresh() {
      if (this.disposed) {
        return;
      }

      bool running = this.runtime.Enabled;
      string? selectedAudioName =
        this.view.AudioDevices.SelectedItem is AudioDevice selectedAudio
          ? selectedAudio.name
          : null;
      bool opcValid = TryNormalizeOpcAddress(
        this.config.domeBeagleboneOPCAddress, out string? opcError);
      int wandCount = this.runtime.OrientationInput.DevicesSnapshot().Count;
      WandSerialStatus receiver =
        this.runtime.OrientationInput.WandSerial.StatusSnapshot();
      ShowReadinessSnapshot readiness = ShowReadinessEvaluator.Evaluate(
        new ShowReadinessInput(
          running,
          selectedAudioName,
          this.runtime.AudioInput.Volume,
          this.config.domeEnabled,
          opcValid,
          opcError,
          this.runtime.Telemetry.DomeBeagleboneOPCFPS,
          wandCount,
          this.config.wandSerialPort,
          receiver.LastError,
          this.webServerError,
          this.webServerPort));

      this.view.PowerButton.Content = readiness.PowerButtonText;
      this.view.PowerButton.Style = (Style)this.findResource(
        running ? "DestructiveButton" : "PrimaryButton");
      this.SetBadge(this.view.Engine, readiness.Engine);
      this.SetBadge(this.view.Audio, readiness.Audio);
      this.view.AudioSignal.Text = readiness.AudioSignalText;
      this.SetBadge(this.view.Dome, readiness.Dome);
      this.SetBadge(this.view.Wand, readiness.Wand);
      this.view.ConnectedWandCount.Text =
        readiness.ConnectedWandCountText;
      this.view.WebControllerStatus.Text = readiness.WebStatus;
      this.view.WebControllerStatus.Foreground =
        (Brush)this.findResource(
          readiness.WebLevel == ReadinessLevel.Ready
            ? "SuccessBrush"
            : "ErrorBrush");
      this.SetBadge(this.view.Overall, readiness.Overall);
    }

    private static string ResolveControllerHost() {
      string? host = null;
      try {
        host = Dns.GetHostEntry(Dns.GetHostName()).AddressList
          .FirstOrDefault(address =>
            address.AddressFamily == AddressFamily.InterNetwork &&
            !IPAddress.IsLoopback(address))?.ToString();
      } catch (SocketException error) {
        Debug.WriteLine(
          "Could not resolve the LAN address: " + error.Message);
      }
      return string.IsNullOrWhiteSpace(host)
        ? Dns.GetHostName()
        : host;
    }

    private static bool TryNormalizeOpcAddress(
      string value,
      out string? error
    ) {
      try {
        Web.SpectrumParameters.NormalizeOpcAddress(value);
        error = null;
        return true;
      } catch (ArgumentException validationError) {
        error = validationError.Message;
        return false;
      }
    }

    private void SetBadge(
      ReadinessBadgeView view,
      ReadinessStatus status
    ) {
      string styleKey = status.Level switch {
        ReadinessLevel.Disabled => "DisabledBadge",
        ReadinessLevel.Warning => "WarningBadge",
        ReadinessLevel.Error => "ErrorBadge",
        ReadinessLevel.Ready => "ReadyBadge",
        _ => throw new ArgumentOutOfRangeException(
          nameof(status), status.Level, "Unknown readiness level."),
      };
      view.Badge.Style = (Style)this.findResource(styleKey);
      view.Text.Text = status.Badge;
      if (view.Detail != null) {
        view.Detail.Text = status.Detail;
      }
    }
  }
}
