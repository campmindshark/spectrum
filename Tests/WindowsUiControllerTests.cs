using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using static Spectrum.LayerPipeline.Tests.TestAssertions;

namespace Spectrum.LayerPipeline.Tests {

  public static class WindowsUiControllerTests {
    public static void Register(Action<string, Action> run) {
      run(nameof(WandSerialUiOwnsSelectionAndTimer),
        WandSerialUiOwnsSelectionAndTimer);
      run(nameof(ReadinessDashboardOwnsRuntimePresentation),
        ReadinessDashboardOwnsRuntimePresentation);
      run(nameof(DomeOpcAddressUiOwnsValidationAndSynchronization),
        DomeOpcAddressUiOwnsValidationAndSynchronization);
    }

    private static void WandSerialUiOwnsSelectionAndTimer() {
      RunOnStaThread("WandSerialUiTest", () => {
        var config = new global::Spectrum.SpectrumConfiguration {
          wandSerialPort = "COM9",
        };
        var selector = new ComboBox();
        var status = new TextBlock();
        using var controller = new global::Spectrum.WandSerialUiController(
          config,
          selector,
          status,
          () => new[] { "COM3" },
          () => new global::Spectrum.WandSerialStatus(
            "COM9", false, 1e9, 1e9, null));

        controller.Start();

        Assert(selector.Items.Count == 3 &&
            selector.SelectedItem is
              global::Spectrum.WandSerialPortOption selected &&
            selected.Value == "COM9" &&
            config.wandSerialPort == "COM9",
          "programmatic wand-port population rewrote configuration");
        Assert(status.Text == "Opening…",
          "wand status was not presented when the controller started");

        selector.SelectedItem = selector.Items
          .OfType<global::Spectrum.WandSerialPortOption>()
          .Single(option => option.Value == "COM3");
        controller.ApplySelectedPort();
        Assert(config.wandSerialPort == "COM3",
          "a genuine wand-port selection was not persisted");

        controller.Dispose();
        selector.SelectedItem = selector.Items
          .OfType<global::Spectrum.WandSerialPortOption>()
          .Single(option => option.Value == "");
        controller.ApplySelectedPort();
        controller.RepopulatePorts();
        Assert(config.wandSerialPort == "COM3",
          "disposed wand UI controller accepted a queued UI update");
      });
    }

    private static void ReadinessDashboardOwnsRuntimePresentation() {
      RunOnStaThread("ReadinessDashboardUiTest", () => {
        var config = new global::Spectrum.SpectrumConfiguration {
          domeBeagleboneOPCAddress = "127.0.0.1:7890",
        };
        var runtime = new global::Spectrum.Operator(config);
        var audioDevices = new ComboBox();
        audioDevices.Items.Add(new global::Spectrum.Audio.AudioDevice {
          id = "test-audio",
          name = "Test audio",
          index = 0,
        });
        audioDevices.SelectedIndex = 0;

        var primaryButton = new Style(typeof(Button));
        var destructiveButton = new Style(typeof(Button));
        var disabledBadge = new Style(typeof(Border));
        var warningBadge = new Style(typeof(Border));
        var errorBadge = new Style(typeof(Border));
        var readyBadge = new Style(typeof(Border));
        Brush successBrush = Brushes.Green;
        Brush errorBrush = Brushes.Red;
        object FindResource(string key) => key switch {
          "PrimaryButton" => primaryButton,
          "DestructiveButton" => destructiveButton,
          "DisabledBadge" => disabledBadge,
          "WarningBadge" => warningBadge,
          "ErrorBadge" => errorBadge,
          "ReadyBadge" => readyBadge,
          "SuccessBrush" => successBrush,
          "ErrorBrush" => errorBrush,
          _ => throw new InvalidOperationException(
            "Unexpected resource key: " + key),
        };

        var powerButton = new Button();
        var engine = ReadinessBadge();
        var audio = ReadinessBadge();
        var audioSignal = new TextBlock();
        var dome = ReadinessBadge();
        var wand = ReadinessBadge();
        var wandCount = new TextBlock();
        var webAddress = new TextBlock();
        var webStatus = new TextBlock();
        var overall = ReadinessBadge(withDetail: false);
        var view = new global::Spectrum.ReadinessDashboardView(
          powerButton,
          audioDevices,
          engine,
          audio,
          audioSignal,
          dome,
          wand,
          wandCount,
          webAddress,
          webStatus,
          overall);
        using var controller =
          new global::Spectrum.ReadinessDashboardUiController(
            config,
            runtime,
            Dispatcher.CurrentDispatcher,
            view,
            FindResource,
            webServerError: null,
            webServerPort: 8080,
            resolveControllerHost: () => "show-host");

        Assert(webAddress.Text == "http://show-host:8080" &&
            Equals(powerButton.Content, "Start engine") &&
            ReferenceEquals(powerButton.Style, primaryButton),
          "the readiness controller did not initialize its address and " +
          "stopped-engine presentation");
        Assert(audio.Text.Text == "○ Configured" &&
            audio.Detail?.Text.Contains("Test audio") == true &&
            ReferenceEquals(audio.Badge.Style, warningBadge) &&
            webStatus.Text == "Ready — listening on port 8080." &&
            ReferenceEquals(webStatus.Foreground, successBrush),
          "the readiness controller did not apply its initial snapshot");

        config.domeEnabled = true;
        config.domeBeagleboneOPCAddress = "missing-port";
        controller.Refresh();
        Assert(dome.Text.Text == "! Invalid address" &&
            ReferenceEquals(dome.Badge.Style, errorBadge) &&
            overall.Text.Text == "! Action required" &&
            ReferenceEquals(overall.Badge.Style, errorBadge),
          "the readiness controller did not apply blocking OPC state");

        controller.Dispose();
        string disposedStatus = webStatus.Text;
        webStatus.Text = "disposed";
        controller.Refresh();
        Assert(webStatus.Text == "disposed" &&
            disposedStatus != webStatus.Text,
          "the disposed readiness controller accepted a queued refresh");
      });
    }

    private static void DomeOpcAddressUiOwnsValidationAndSynchronization() {
      RunOnStaThread("DomeOpcAddressUiTest", () => {
        var config = new global::Spectrum.SpectrumConfiguration {
          domeBeagleboneOPCAddress = "initial-host:7890",
        };
        var address = new TextBox();
        var status = new TextBlock();
        Brush successBrush = Brushes.Green;
        Brush errorBrush = Brushes.Red;
        object FindResource(string key) => key switch {
          "SuccessBrush" => successBrush,
          "ErrorBrush" => errorBrush,
          _ => throw new InvalidOperationException(
            "Unexpected resource key: " + key),
        };
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        using var controller =
          new global::Spectrum.DomeOpcAddressUiController(
            config,
            dispatcher,
            address,
            status,
            FindResource);

        controller.Start();
        Assert(address.Text == "initial-host:7890" &&
            status.Text == "Valid host and port format." &&
            ReferenceEquals(status.Foreground, successBrush),
          "the OPC editor did not initialize from configuration");

        config.domeBeagleboneOPCAddress = "external-host:7891";
        DrainDispatcher(dispatcher);
        Assert(address.Text == "external-host:7891",
          "an external OPC address update did not reach the editor");

        address.Text = "missing-port";
        controller.ShowValidation();
        controller.CommitAddress();
        Assert(status.Text.StartsWith("Error: ") &&
            ReferenceEquals(status.Foreground, errorBrush) &&
            config.domeBeagleboneOPCAddress == "external-host:7891",
          "an invalid OPC address was persisted");

        address.Text = "  committed-host:7892:7  ";
        controller.ShowValidation();
        controller.CommitAddress();
        DrainDispatcher(dispatcher);
        Assert(address.Text == "committed-host:7892:7" &&
            config.domeBeagleboneOPCAddress == "committed-host:7892:7" &&
            status.Text == "Valid host and port format.",
          "a valid OPC address was not normalized and persisted");

        controller.Dispose();
        config.domeBeagleboneOPCAddress = "after-disposal:7893";
        DrainDispatcher(dispatcher);
        controller.SynchronizeFromConfiguration();
        Assert(address.Text == "committed-host:7892:7",
          "the disposed OPC editor accepted a queued update");
      });
    }

    private static global::Spectrum.ReadinessBadgeView ReadinessBadge(
      bool withDetail = true
    ) => new global::Spectrum.ReadinessBadgeView(
      new Border(),
      new TextBlock(),
      withDetail ? new TextBlock() : null);

    private static void DrainDispatcher(Dispatcher dispatcher) {
      var frame = new DispatcherFrame();
      dispatcher.BeginInvoke(
        DispatcherPriority.ApplicationIdle,
        new Action(() => frame.Continue = false));
      Dispatcher.PushFrame(frame);
    }

    private static void RunOnStaThread(string name, Action test) {
      Exception? failure = null;
      var thread = new Thread(() => {
        try {
          test();
        } catch (Exception error) {
          failure = error;
        }
      }) {
        IsBackground = true,
        Name = name,
      };
      thread.SetApartmentState(ApartmentState.STA);
      thread.Start();
      Assert(thread.Join(TimeSpan.FromSeconds(5)),
        name + " did not complete");
      if (failure != null) {
        throw new InvalidOperationException(name + " contract failed", failure);
      }
    }
  }
}
