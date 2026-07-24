using System;
using System.Collections.Generic;
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
      run(nameof(OperatorSettingsOwnBindingsAndAudioSelection),
        OperatorSettingsOwnBindingsAndAudioSelection);
      run(nameof(MidiDeviceUiOwnsDiscoveryAndAssignment),
        MidiDeviceUiOwnsDiscoveryAndAssignment);
      run(nameof(MidiPresetUiOwnsSparseIdentityAndEditModes),
        MidiPresetUiOwnsSparseIdentityAndEditModes);
      run(nameof(MidiSetupUiOwnsDevicePresetAndBindingPresentation),
        MidiSetupUiOwnsDevicePresetAndBindingPresentation);
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

        config.wandSerialPort = "COM9";
        DrainDispatcher(Dispatcher.CurrentDispatcher);
        Assert(selector.SelectedItem is
            global::Spectrum.WandSerialPortOption externalSelection &&
            externalSelection.Value == "COM9",
          "an external wand-port update did not reach the selector");

        controller.Dispose();
        config.wandSerialPort = "";
        DrainDispatcher(Dispatcher.CurrentDispatcher);
        controller.ApplySelectedPort();
        controller.RepopulatePorts();
        Assert(selector.SelectedItem is
            global::Spectrum.WandSerialPortOption disposedSelection &&
            disposedSelection.Value == "COM9",
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
        DrainDispatcher(Dispatcher.CurrentDispatcher);
        Assert(dome.Text.Text == "! Invalid address" &&
            ReferenceEquals(dome.Badge.Style, errorBadge) &&
            overall.Text.Text == "! Action required" &&
            ReferenceEquals(overall.Badge.Style, errorBadge),
          "the readiness controller did not apply blocking OPC state");

        string? copiedAddress = null;
        controller.CopyControllerAddress(
          address => copiedAddress = address);
        Assert(copiedAddress == "http://show-host:8080" &&
            webStatus.Text == "Address copied to the clipboard." &&
            ReferenceEquals(webStatus.Foreground, successBrush),
          "the readiness controller did not own address-copy presentation");

        controller.Dispose();
        string disposedStatus = webStatus.Text;
        webStatus.Text = "disposed";
        controller.Refresh();
        Assert(webStatus.Text == "disposed" &&
            disposedStatus != webStatus.Text,
          "the disposed readiness controller accepted a queued refresh");
      });
    }

    private static void OperatorSettingsOwnBindingsAndAudioSelection() {
      RunOnStaThread("OperatorSettingsUiTest", () => {
        var config = new global::Spectrum.SpectrumConfiguration {
          audioDeviceID = "audio-a",
          midiInputEnabled = true,
          domeBrightness = 0.4,
        };
        var runtime = new global::Spectrum.Operator(config);
        var audioDevices = new ComboBox();
        var midiEnabled = new CheckBox();
        var domeThread = new CheckBox();
        var operatorFps = new Label();
        var domeFps = new TextBlock();
        var homeDomeFps = new TextBlock();
        var domeEnabled = new CheckBox();
        var domeSimulation = new CheckBox();
        var testPattern = new ComboBox();
        var maxBrightness = new Slider();
        var maxBrightnessLabel = new Label();
        var brightness = new Slider();
        var brightnessLabel = new Label();
        var vjHudEnabled = new CheckBox();
        IReadOnlyList<global::Spectrum.Audio.AudioDevice> discovered =
          new[] {
            new global::Spectrum.Audio.AudioDevice {
              id = "audio-a",
              name = "Audio A",
              index = 2,
            },
            new global::Spectrum.Audio.AudioDevice {
              id = "audio-b",
              name = "Audio B",
              index = 5,
            },
          };
        int readinessRefreshes = 0;
        var controller =
          new global::Spectrum.OperatorSettingsUiController(
            config,
            runtime,
            new global::Spectrum.OperatorSettingsView(
              audioDevices,
              midiEnabled,
              domeThread,
              operatorFps,
              domeFps,
              homeDomeFps,
              domeEnabled,
              domeSimulation,
              testPattern,
              maxBrightness,
              maxBrightnessLabel,
              brightness,
              brightnessLabel,
              vjHudEnabled),
            () => discovered,
            () => readinessRefreshes++);
        audioDevices.SelectionChanged +=
          (_, _) => controller.ApplySelectedAudioDevice();

        controller.Start();

        Assert(audioDevices.Items.Count == 2 &&
            audioDevices.SelectedItem is
              global::Spectrum.Audio.AudioDevice selected &&
            selected.id == "audio-a" &&
            config.audioDeviceID == "audio-a" &&
            readinessRefreshes == 1,
          "settings startup did not preserve and present the configured " +
          "audio device");
        Assert(midiEnabled.IsChecked == true &&
            Math.Abs(brightness.Value - 0.4) < 0.0001 &&
            testPattern.Items.Count ==
              global::Spectrum.DomeTestPatterns.Names.Count,
          "settings startup did not establish configuration bindings");

        audioDevices.SelectedIndex = 1;
        Assert(config.audioDeviceID == "audio-b" &&
            readinessRefreshes == 2,
          "a genuine audio-device selection was not persisted");

        discovered = new[] {
          new global::Spectrum.Audio.AudioDevice {
            id = "audio-c",
            name = "Audio C",
            index = 8,
          },
        };
        controller.RefreshAudioDevices();
        Assert(audioDevices.SelectedIndex == -1 &&
            config.audioDeviceID == "audio-b" &&
            readinessRefreshes == 3,
          "programmatic audio-device population rewrote configuration");

        config.midiInputEnabled = false;
        brightness.Value = 0.7;
        Assert(midiEnabled.IsChecked == false &&
            Math.Abs(config.domeBrightness - 0.7) < 0.0001,
          "settings bindings did not synchronize both directions");
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

    private static void MidiDeviceUiOwnsDiscoveryAndAssignment() {
      RunOnStaThread("MidiDeviceUiTest", () => {
        var config = new global::Spectrum.SpectrumConfiguration();
        config.ReplaceMidiPresets(new Dictionary<int,
          global::Spectrum.Base.MidiPreset> {
          [4] = new global::Spectrum.Base.MidiPreset {
            id = 4,
            Name = "Warm",
          },
          [9] = new global::Spectrum.Base.MidiPreset {
            id = 9,
            Name = "Cool",
          },
        });
        config.ReplaceMidiDevices(new Dictionary<int, int> {
          [0] = 4,
          [5] = 99,
        });
        global::Spectrum.MidiDeviceSetupView view =
          MidiSetupView().Device;
        bool allowDeletion = false;
        var controller =
          new global::Spectrum.MidiDeviceUiController(
            config,
            view,
            deviceCount: () => 2,
            getDeviceName: deviceId => "Device " + deviceId,
            confirmDestructiveAction: (_, _) => allowDeletion);

        controller.Start();

        global::Spectrum.MidiDeviceEntry connected =
          view.ConfiguredDevices.Items
            .OfType<global::Spectrum.MidiDeviceEntry>()
            .Single(entry => entry.DeviceID == 0);
        global::Spectrum.MidiDeviceEntry disconnected =
          view.ConfiguredDevices.Items
            .OfType<global::Spectrum.MidiDeviceEntry>()
            .Single(entry => entry.DeviceID == 5);
        Assert(connected.PresetID == 4 &&
            connected.DeviceName == "Device 0" &&
            connected.PresetName == "Warm" &&
            disconnected.DeviceName == "< DISCONNECTED >" &&
            disconnected.PresetName == "(missing preset)" &&
            view.AvailableDevices.Items.Count == 1 &&
            Equals(view.AvailableDevices.Items[0], "Device 1"),
          "the device controller did not project connected, " +
          "disconnected, and available devices");

        view.ConfiguredDevices.SelectedItem = connected;
        controller.SelectionChanged();
        Assert(view.DeleteDevice.IsEnabled &&
            view.LoadPreset.IsEnabled &&
            controller.TryGetSelectedPreset(out int selectedPresetId) &&
            selectedPresetId == 4,
          "configured-device selection did not expose actions and " +
          "stable preset identity");

        view.AvailableDevices.SelectedIndex = 0;
        Assert(controller.EnsureAvailableDeviceSelected() &&
            controller.TryAssignSelectedDevice(9) &&
            config.midiDevices.TryGetValue(1, out int assignedPresetId) &&
            assignedPresetId == 9 &&
            view.AvailableDevices.Items.Count == 0,
          "device assignment did not persist and refresh projections");

        view.ConfiguredDevices.SelectedItem =
          view.ConfiguredDevices.Items
            .OfType<global::Spectrum.MidiDeviceEntry>()
            .Single(entry => entry.DeviceID == 1);
        Assert(!controller.TryDeleteSelectedDevice(
              out int cancelledPresetId) &&
            cancelledPresetId == 0 &&
            config.midiDevices.ContainsKey(1),
          "cancelled device removal mutated configuration");

        allowDeletion = true;
        Assert(controller.TryDeleteSelectedDevice(
              out int removedPresetId) &&
            removedPresetId == 9 &&
            !config.midiDevices.ContainsKey(1) &&
            view.AvailableDevices.Items.Count == 1 &&
            Equals(view.AvailableDevices.Items[0], "Device 1"),
          "confirmed device removal did not persist and refresh " +
          "projections");
      });
    }

    private static void
      MidiPresetUiOwnsSparseIdentityAndEditModes() {
      RunOnStaThread("MidiPresetUiTest", () => {
        var config = new global::Spectrum.SpectrumConfiguration();
        config.ReplaceMidiPresets(new Dictionary<int,
          global::Spectrum.Base.MidiPreset> {
          [4] = new global::Spectrum.Base.MidiPreset {
            id = 4,
            Name = "Warm",
          },
          [9] = new global::Spectrum.Base.MidiPreset {
            id = 9,
            Name = "Cool",
          },
        });
        config.ReplaceMidiDevices(new Dictionary<int, int> {
          [0] = 4,
        });
        global::Spectrum.MidiSetupView setup = MidiSetupView();
        int configuredDeviceRefreshes = 0;
        var controller =
          new global::Spectrum.MidiPresetUiController(
            config,
            setup.Preset,
            setup.Device.NewDevicePreset,
            setup.Device.NewPresetNameContainer,
            setup.Device.NewPresetName,
            setup.Binding,
            confirmDestructiveAction: (_, _) => true,
            presetsChanged: () => configuredDeviceRefreshes++);

        controller.Start();
        setup.Preset.Presets.SelectedIndex = 1;
        controller.SelectionChanged();
        Assert(setup.Preset.DeletePreset.IsEnabled &&
            setup.Binding.Save.IsEnabled,
          "the preset controller did not project sparse preset " +
          "selection into actions and bindings");

        controller.BeginRename();
        Assert(setup.Preset.EditLabel.Content?.ToString() ==
              "Rename preset" &&
            setup.Preset.Name.Text == "Cool" &&
            setup.Preset.Cancel.Visibility == Visibility.Visible,
          "the preset controller did not enter rename mode");

        setup.Preset.Name.Text = "Warm";
        controller.Save();
        Assert(config.midiPresets[9].Name == "Cool" &&
            setup.Preset.EditLabel.Content?.ToString() ==
              "Rename preset" &&
            setup.Preset.Cancel.Visibility == Visibility.Visible,
          "an invalid duplicate rename mutated the preset or left " +
          "rename mode");

        setup.Preset.Name.Text = "Cooler";
        controller.Save();
        Assert(config.midiPresets[9].Name == "Cooler" &&
            Equals(setup.Preset.Presets.Items[1], "Cooler") &&
            Equals(setup.Device.NewDevicePreset.Items[1], "Cooler") &&
            setup.Preset.EditLabel.Content?.ToString() ==
              "Add preset" &&
            setup.Preset.Cancel.Visibility == Visibility.Collapsed &&
            configuredDeviceRefreshes == 1,
          "a valid sparse-ID rename did not synchronize projections " +
          "and restore add mode");

        controller.CloneSelected();
        Assert(config.midiPresets.TryGetValue(
              10, out global::Spectrum.Base.MidiPresetView? clone) &&
            clone.Name == "Cooler (clone)" &&
            setup.Preset.Presets.Items.Count == 3 &&
            setup.Device.NewDevicePreset.Items.Count == 4,
          "preset cloning lost sparse identity or a list projection");

        config.ReplaceMidiDevices(new Dictionary<int, int> {
          [0] = 4,
          [1] = 9,
        });
        controller.RefreshDeletionState(9);
        Assert(!setup.Preset.DeletePreset.IsEnabled,
          "an assigned preset remained deletable");

        config.ReplaceMidiDevices(new Dictionary<int, int> {
          [0] = 4,
        });
        controller.RefreshDeletionState(9);
        Assert(setup.Preset.DeletePreset.IsEnabled,
          "an unassigned preset did not become deletable");

        controller.DeleteSelected();
        Assert(!config.midiPresets.ContainsKey(9) &&
            config.midiPresets.ContainsKey(4) &&
            config.midiPresets.ContainsKey(10) &&
            setup.Preset.Presets.Items.Count == 2 &&
            setup.Device.NewDevicePreset.Items.Count == 3 &&
            !setup.Binding.Save.IsEnabled,
          "preset deletion did not synchronize persistence, lists, " +
          "and binding selection");
      });
    }

    private static void
      MidiSetupUiOwnsDevicePresetAndBindingPresentation() {
      RunOnStaThread("MidiSetupUiTest", () => {
        var config = new global::Spectrum.SpectrumConfiguration();
        config.ReplaceMidiPresets(new Dictionary<int,
          global::Spectrum.Base.MidiPreset> {
          [4] = new global::Spectrum.Base.MidiPreset {
            id = 4,
            Name = "Warm",
          },
          [9] = new global::Spectrum.Base.MidiPreset {
            id = 9,
            Name = "Cool",
          },
        });
        config.ReplaceMidiDevices(new Dictionary<int, int> {
          [0] = 4,
        });
        global::Spectrum.MidiSetupView view = MidiSetupView();
        var controller = new global::Spectrum.MidiSetupUiController(
          config,
          view,
          deviceCount: () => 2,
          getDeviceName: deviceId => "Device " + deviceId,
          confirmDestructiveAction: (_, _) => true);

        controller.Start();

        Assert(view.Device.ConfiguredDevices.Items.Count == 1 &&
            view.Device.ConfiguredDevices.Items[0] is
              global::Spectrum.MidiDeviceEntry configured &&
            configured.DeviceID == 0 &&
            configured.PresetID == 4 &&
            configured.DeviceName == "Device 0" &&
            configured.PresetName == "Warm" &&
            view.Device.AvailableDevices.Items.Count == 1 &&
            Equals(view.Device.AvailableDevices.Items[0], "Device 1") &&
            view.Preset.Presets.Items.Count == 2 &&
            view.Device.NewDevicePreset.Items.Count == 3,
          "the MIDI controller did not initialize device and preset " +
          "projections");

        view.Device.NewDevicePreset.SelectedIndex = 1;
        view.Device.AvailableDevices.SelectedIndex = 0;
        controller.AddDevice();
        Assert(config.midiDevices.TryGetValue(1, out int presetId) &&
            presetId == 9 &&
            view.Device.ConfiguredDevices.Items.Count == 2 &&
            view.Device.AvailableDevices.Items.Count == 0,
          "the MIDI controller lost a sparse preset identity while " +
          "assigning a device");

        view.Device.ConfiguredDevices.SelectedItem =
          view.Device.ConfiguredDevices.Items
            .OfType<global::Spectrum.MidiDeviceEntry>()
            .Single(entry => entry.DeviceID == 1);
        controller.LoadSelectedDevicePreset();
        Assert(view.Preset.Presets.SelectedIndex == 1 &&
            Equals(view.Preset.Presets.SelectedItem, "Cool"),
          "configured-device navigation lost the assigned sparse " +
          "preset identity");
        controller.PresetSelectionChanged();
        Assert(!view.Preset.DeletePreset.IsEnabled &&
            view.Binding.Save.IsEnabled,
          "the MIDI controller did not apply assigned-preset action state");

        view.Binding.Type.SelectedIndex = 4;
        controller.BindingTypeSelectionChanged();
        view.Binding.AdsrLevelDriverIndexRangeStart.Text = "60";
        controller.SaveBinding();
        Assert(view.Binding.ValidationMessage.Visibility ==
            Visibility.Visible &&
            view.Binding.ValidationMessage.Text.Contains(
              "name", StringComparison.OrdinalIgnoreCase) &&
            config.midiPresets[9].Bindings.Length == 0,
          "the MIDI controller persisted an invalid binding draft");

        view.Binding.Name.Text = "Envelope";
        controller.SaveBinding();
        Assert(config.midiPresets[9].Bindings.Length == 1 &&
            config.midiPresets[9].Bindings[0] is
              global::Spectrum.Base.AdsrLevelDriverMidiBindingView binding &&
            binding.BindingName == "Envelope" &&
            binding.IndexRangeStart == 60 &&
            view.Binding.Bindings.Items.Count == 1 &&
            view.Binding.Bindings.Items[0] is
              global::Spectrum.MidiBindingEntry entry &&
            entry.BindingTypeName == "ADSR level driver" &&
            view.Binding.ValidationMessage.Visibility ==
              Visibility.Collapsed,
          "the MIDI controller did not persist and present a valid " +
          "binding");

        view.Binding.Bindings.SelectedIndex = 0;
        controller.BindingSelectionChanged();
        controller.BeginBindingEdit();
        Assert(view.Binding.EditLabel.Content?.ToString() ==
              "Edit binding" &&
            view.Binding.Name.Text == "Envelope" &&
            view.Binding.Type.SelectedIndex == 4 &&
            view.Binding.AdsrLevelDriverIndexRangeStart.Text == "60",
          "the MIDI binding controller did not restore the selected " +
          "binding draft");

        view.Binding.Name.Text = "Renamed envelope";
        controller.SaveBinding();
        Assert(config.midiPresets[9].Bindings.Length == 1 &&
            config.midiPresets[9].Bindings[0].BindingName ==
              "Renamed envelope" &&
            config.midiPresets[4].Bindings.Length == 0 &&
            view.Binding.Bindings.Items[0] is
              global::Spectrum.MidiBindingEntry edited &&
            edited.BindingName == "Renamed envelope",
          "binding editing lost the explicit sparse preset identity");

        view.Binding.Bindings.SelectedIndex = 0;
        controller.DeleteSelectedBinding();
        Assert(config.midiPresets[9].Bindings.Length == 0 &&
            config.midiPresets[4].Bindings.Length == 0 &&
            view.Binding.Bindings.Items.Count == 0,
          "binding deletion did not persist through the isolated " +
          "binding controller");
      });
    }

    private static global::Spectrum.MidiSetupView MidiSetupView() {
      var bindingType = new ComboBox();
      foreach (string name in new[] {
          "Tap tempo",
          "Continuous knob",
          "Discrete knob",
          "Logarithmic knob",
          "ADSR level driver",
        }) {
        bindingType.Items.Add(new ComboBoxItem { Content = name });
      }

      return new global::Spectrum.MidiSetupView(
        new global::Spectrum.MidiDeviceSetupView(
          new ListView(),
          new Button(),
          new Button(),
          new ComboBox(),
          new Grid(),
          new TextBox(),
          new ComboBox()),
        new global::Spectrum.MidiPresetSetupView(
          new ListBox(),
          new Button(),
          new Button(),
          new Button(),
          new Label(),
          new TextBox(),
          new Button(),
          new Button()),
        new global::Spectrum.MidiBindingSetupView(
          new ListView(),
          new Button(),
          new Button(),
          new Label(),
          new TextBox(),
          bindingType,
          new StackPanel(),
          new ComboBox(),
          new TextBox(),
          new StackPanel(),
          new TextBox(),
          new TextBox(),
          new TextBox(),
          new TextBox(),
          new StackPanel(),
          new TextBox(),
          new TextBox(),
          new TextBox(),
          new StackPanel(),
          new TextBox(),
          new TextBox(),
          new TextBox(),
          new TextBox(),
          new StackPanel(),
          new TextBox(),
          new TextBlock(),
          new Button(),
          new Button()));
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
