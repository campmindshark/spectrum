using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Spectrum.Audio;
using XSerializer;
using System.IO;
using Spectrum.MIDI;
using System.Windows.Data;
using System.Collections.Generic;
using System.Windows.Media;
using Spectrum.Base;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Spectrum {

  public partial class MainWindow : Window {

    private class MidiDeviceEntry {

      public int DeviceID { get; set; }

      public string DeviceName {
        get {
          if (MidiInput.DeviceCount <= this.DeviceID) {
            return "< DISCONNECTED >";
          }
          return MidiInput.GetDeviceName(this.DeviceID);
        }
      }

      public string PresetName { get; set; } = string.Empty;

    }

    private class MidiBindingEntry {
      public string BindingName { get; set; } = string.Empty;
      public string BindingTypeName { get; set; } = string.Empty;
    }

    // LoadConfig completes this composition root before the constructor starts
    // the host or exposes the window.
    private Operator op = null!;
    private SpectrumConfiguration config;
    private SpectrumHost<Operator, Web.SpectrumWebHost> host;
    public static bool LoadingConfig { get; set; } = false;
    private List<int> midiDeviceIndices = new();
    private List<int> midiPresetIndices = new();
    private MidiPresetEditor midiPresetEditor = null!;
    private MainWindowChildWindows childWindows = null!;
    private WandSerialUiController wandSerialUi = null!;
    private ReadinessDashboardUiController readinessDashboard = null!;
    private DomeOpcAddressUiController domeOpcAddressUi = null!;
    private int? currentlyEditingPreset = null;
    private int? currentlyEditingBinding = null;
    private const int WebServerPort = 8080;
    // Spectrum is distributed as a portable application, so keep its mutable
    // state beside the executable. AppContext.BaseDirectory is stable when the
    // process is launched from a shortcut or an unrelated working directory.
    private static readonly SpectrumConfigurationPaths ConfigPaths =
      SpectrumConfigurationPaths.ForPortableDesktop(
        AppContext.BaseDirectory);
    private static readonly ConfigurationFileStore<SpectrumConfiguration>
      ConfigStore = new ConfigurationFileStore<SpectrumConfiguration>(
        ConfigPaths.PrimaryPath,
        ConfigPaths.BackupPath,
        ConfigPaths.DefaultPath,
        (stream, value) =>
          new XmlSerializer<SpectrumConfigurationDocument>().Serialize(
            stream,
            SpectrumConfigurationDocument.FromConfiguration(value)),
        stream => new XmlSerializer<SpectrumConfigurationDocument>()
          .Deserialize(stream).ToConfiguration());
    private static readonly string WindowPlacementPath = Path.Combine(
      AppContext.BaseDirectory, "spectrum_window_state.json");
    public MainWindow() {
      this.InitializeComponent();
      WindowPlacementStore.Restore(this, WindowPlacementPath);

      this.LoadConfig();
      this.host.Start();
      this.readinessDashboard = new ReadinessDashboardUiController(
        this.config,
        this.op,
        this.Dispatcher,
        new ReadinessDashboardView(
          this.powerButton,
          this.audioDevices,
          new ReadinessBadgeView(
            this.engineStatusBadge,
            this.engineStatusText,
            this.engineReadinessDetail),
          new ReadinessBadgeView(
            this.audioStatusBadge,
            this.audioStatusText,
            this.audioReadinessDetail),
          this.audioSignalText,
          new ReadinessBadgeView(
            this.domeStatusBadge,
            this.domeStatusText,
            this.domeReadinessDetail),
          new ReadinessBadgeView(
            this.homeWandStatusBadge,
            this.homeWandStatusText,
            this.homeWandReadinessDetail),
          this.connectedWandCountText,
          this.webControllerAddress,
          this.webControllerStatus,
          new ReadinessBadgeView(
            this.overallReadinessBadge,
            this.overallReadinessText)),
        key => this.FindResource(key),
        this.host.ServiceStartError?.Message,
        WebServerPort);
      this.config.PropertyChanged += ConfigUpdated;
    }

    private void ConfigUpdated(object? sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == nameof(this.config.wandSerialPort)) {
        this.Dispatcher.BeginInvoke(
          new Action(this.wandSerialUi.RepopulatePorts));
      }
      this.Dispatcher.BeginInvoke(
        new Action(this.readinessDashboard.Refresh));
    }

    private void HandleClose(object sender, EventArgs e) {
      this.op.Enabled = false;
      this.config.PropertyChanged -= ConfigUpdated;
      this.readinessDashboard.Dispose();
      this.domeOpcAddressUi.Dispose();
      this.wandSerialUi.Dispose();
      WindowPlacementStore.Save(this, WindowPlacementPath);
      try {
        this.host?.Dispose();
      } catch (Exception error) {
        App.LogException("Could not shut Spectrum down cleanly", error);
      }
    }

    [MemberNotNull(
      nameof(config),
      nameof(host),
      nameof(midiPresetEditor),
      nameof(childWindows))]
    private void LoadConfig() {
      MainWindow.LoadingConfig = true;

      ApplicationStateDispatcher applicationStateDispatcher =
        new DispatcherApplicationStateDispatcher(
          this.Dispatcher);
      this.host = new SpectrumHost<Operator, Web.SpectrumWebHost>(
        ConfigStore,
        applicationStateDispatcher,
        TimeSpan.FromMilliseconds(100),
        (config, dispatcher) => new Operator(
          config, dispatcher, new WindowsSpectrumInputFactory()),
        (config, dispatcher, runtime) => new Web.SpectrumWebHost(
          config, dispatcher, runtime, WebServerPort,
          nativeWindowControlsAvailable: true,
          reportBackgroundError: error => App.LogException(
            "Web background task failed", error)),
        SpectrumConfigurationSchema.RestartPropertyNames,
        saveEnabled: () => !MainWindow.LoadingConfig,
        reportLoadFailure: failure => Debug.WriteLine(
          "Failed to load configuration from " + failure.Path + ": " +
          failure.Error),
        reportSaveError: error => App.LogException(
          "Could not save Spectrum configuration", error),
        reportServiceStartError: error => Debug.WriteLine(
          "Web controller failed to start: " + error));
      this.config = this.host.Configuration;
      this.op = this.host.Runtime;
      Web.AdvisoryLockManager advisoryLocks =
        this.host.Service.AdvisoryLocks;
      Web.DomeCalibrationController domeCalibrationController =
        this.host.Service.DomeCalibration;
      this.midiPresetEditor = new MidiPresetEditor(this.config);
      this.childWindows = new MainWindowChildWindows(
        this.config,
        this.op,
        domeCalibrationController,
        advisoryLocks);

      this.RefreshAudioDevices(null, null);
      this.RefreshMidiDevices(null, null);
      this.LoadPresets();

      this.Bind(nameof(this.config.midiInputEnabled), this.midiEnabled, CheckBox.IsCheckedProperty);
      this.Bind(nameof(this.config.domeOutputInSeparateThread), this.domeThreadCheckbox, CheckBox.IsCheckedProperty);
      // FPS counters are runtime telemetry, not config — bind to the Operator's
      // RuntimeTelemetry (WPF marshals its background-thread notifications).
      this.Bind(nameof(this.op.Telemetry.OperatorFPS), this.operatorFPSLabel, Label.ContentProperty, BindingMode.OneWay, null, this.op.Telemetry);
      this.Bind(nameof(this.op.Telemetry.OperatorFPS), this.operatorFPSLabel, Label.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter(), this.op.Telemetry);
      this.domeOpcAddressUi = new DomeOpcAddressUiController(
        this.config,
        this.Dispatcher,
        this.domeBeagleboneOPCHostAndPort,
        this.domeOPCValidationStatus,
        key => this.FindResource(key));
      this.domeOpcAddressUi.Start();
      this.Bind(nameof(this.op.Telemetry.DomeBeagleboneOPCFPS), this.domeBeagleboneOPCFPSLabel, TextBlock.TextProperty, BindingMode.OneWay, null, this.op.Telemetry);
      this.Bind(nameof(this.op.Telemetry.DomeBeagleboneOPCFPS), this.domeBeagleboneOPCFPSLabel, TextBlock.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter(), this.op.Telemetry);
      this.Bind(nameof(this.op.Telemetry.DomeBeagleboneOPCFPS), this.homeDomeFPSLabel, TextBlock.TextProperty, BindingMode.OneWay, null, this.op.Telemetry);
      this.Bind(nameof(this.config.domeEnabled), this.domeEnabled, CheckBox.IsCheckedProperty);
      this.Bind(nameof(this.config.domeSimulationEnabled), this.domeSimulationEnabled, CheckBox.IsCheckedProperty);
      this.domeTestPatternSelector.ItemsSource = DomeTestPatterns.Names;
      this.Bind(nameof(this.config.domeTestPattern), this.domeTestPatternSelector,
        Selector.SelectedIndexProperty);
      this.Bind(nameof(this.config.domeMaxBrightness), this.domeMaxBrightnessSlider, Slider.ValueProperty);
      this.Bind(nameof(this.config.domeMaxBrightness), this.domeMaxBrightnessLabel, Label.ContentProperty,
        BindingMode.OneWay, new NormalizedPercentConverter());
      this.Bind(nameof(this.config.domeBrightness), this.domeBrightnessSlider, Slider.ValueProperty);
      this.Bind(nameof(this.config.domeBrightness), this.domeBrightnessLabel, Label.ContentProperty,
        BindingMode.OneWay, new NormalizedPercentConverter());
      this.Bind(nameof(this.config.vjHUDEnabled), this.vjHUDEnabled, CheckBox.IsCheckedProperty);

      this.wandSerialUi = new WandSerialUiController(
        this.config,
        this.wandSerialPortSelector,
        this.wandReceiverStatus,
        WandSerialReceiver.AvailablePorts,
        () => this.op.OrientationInput.WandSerial.StatusSnapshot());
      this.wandSerialUi.Start();

      MainWindow.LoadingConfig = false;
    }

    private void Bind(
      string configPath,
      FrameworkElement element,
      DependencyProperty property,
      BindingMode mode = BindingMode.TwoWay,
      IValueConverter? converter = null,
      object? source = null
    ) {
      var binding = new System.Windows.Data.Binding(configPath);
      binding.Source = source != null ? source : this.config;
      binding.Mode = mode;
      if (converter != null) {
        binding.Converter = converter;
      }
      element.SetBinding(property, binding);
    }

    private void ShowMidiSetup(object sender, RoutedEventArgs e) {
      this.mainTabs.SelectedItem = this.midiTab;
    }

    private void ShowDomeSetup(object sender, RoutedEventArgs e) {
      this.mainTabs.SelectedItem = this.domeTab;
    }

    private void CopyWebControllerAddress(object sender, RoutedEventArgs e) {
      try {
        Clipboard.SetText(this.webControllerAddress.Text);
        this.webControllerStatus.Text = "Address copied to the clipboard.";
        this.webControllerStatus.Foreground =
          (Brush)this.FindResource("SuccessBrush");
      } catch (Exception copyError) {
        this.webControllerStatus.Text = "Could not copy address: " +
          copyError.Message;
        this.webControllerStatus.Foreground =
          (Brush)this.FindResource("ErrorBrush");
      }
    }

    private void DomeOpcAddressChanged(
      object sender,
      TextChangedEventArgs e
    ) {
      if (this.domeOpcAddressUi == null) {
        return;
      }
      this.domeOpcAddressUi.ShowValidation();
    }

    private void DomeOpcAddressLostFocus(
      object sender,
      RoutedEventArgs e
    ) {
      this.domeOpcAddressUi.CommitAddress();
    }

    private void SliderStarted(object sender, DragStartedEventArgs e) {
      MainWindow.LoadingConfig = true;
    }

    private void SliderCompleted(object sender, DragCompletedEventArgs e) {
      MainWindow.LoadingConfig = false;
    }

    private void PowerButtonClicked(object sender, RoutedEventArgs e) {
      this.op.Enabled = !this.op.Enabled;
      this.readinessDashboard.Refresh();
    }

    private void RefreshAudioDevices(object? sender, RoutedEventArgs? e) {
      this.op.Enabled = false;

      var audioDevices = AudioInput.AudioDevices;

      this.audioDevices.Items.Clear();
      foreach (var audioDevice in audioDevices) {
        this.audioDevices.Items.Add(audioDevice);
      }

      this.audioDevices.SelectedIndex = audioDevices.FindIndex(
        device => device.id == this.config.audioDeviceID
      );
      this.readinessDashboard.Refresh();
    }

    private void AudioInputDeviceChanged(
      object sender,
      SelectionChangedEventArgs e
    ) {
      if (this.audioDevices.SelectedIndex == -1) {
        return;
      }
      this.config.audioDeviceID = ((AudioDevice)this.audioDevices.SelectedItem).id;
    }

    // Refresh the list of available devices for the "Add device" panel
    private void RefreshMidiDevices(object? sender, RoutedEventArgs? e) {
      var currentDevice = this.midiDevices.SelectedItem;

      this.midiDevices.Items.Clear();
      this.midiDeviceIndices = new List<int>();
      for (int i = 0; i < MidiInput.DeviceCount; i++) {
        if (!this.config.midiDevices.ContainsKey(i)) {
          this.midiDevices.Items.Add(MidiInput.GetDeviceName(i));
          this.midiDeviceIndices.Add(i);
        }
      }

      this.midiDevices.SelectedItem = currentDevice;

      // This may change the name of devices with a given device ID, so we
      // regenerate the midiDeviceList
      this.LoadMidiDevices();
    }

    // Refresh the list of active/created "devices" (paired with a preset)
    private void LoadMidiDevices() {
      this.midiDeviceList.Items.Clear();
      foreach (var pair in this.config.midiDevices) {
        this.midiDeviceList.Items.Add(new MidiDeviceEntry {
          DeviceID = pair.Key,
          PresetName = this.config.midiPresets[pair.Value].Name ??
            "(unnamed preset)",
        });
      }
    }

    // Only called once, at the start, to populate the parts of the UI that
    // need a list of all presets
    private void LoadPresets() {
      this.midiNewDevicePreset.Items.Clear();
      this.midiPresetIndices = new List<int>();
      foreach (var pair in this.config.midiPresets) {
        var midiPreset = pair.Value;
        this.midiNewDevicePreset.Items.Add(midiPreset.Name);
        this.midiPresetIndices.Add(midiPreset.Id);
        this.midiPresetList.Items.Add(midiPreset.Name);
      }
      this.midiNewDevicePreset.Items.Add("New preset");
    }

    private void MidiNewDeviceSelectionChanged(object sender, RoutedEventArgs e) {
      var lastVisibility = this.midiNewDevicePresetNameGrid.Visibility;
      this.midiNewDevicePresetNameGrid.Visibility =
        this.midiNewDevicePreset.SelectedIndex == this.midiPresetIndices.Count
          ? Visibility.Visible
          : Visibility.Collapsed;
      if (
        this.midiNewDevicePresetNameGrid.Visibility != lastVisibility &&
        this.midiNewDevicePresetNameGrid.Visibility == Visibility.Visible
      ) {
        this.midiNewDevicePresetName.Focus();
      }
    }

    private void MidiNewDeviceNewPresetNameLostFocus(object sender, RoutedEventArgs e) {
      var name = this.midiNewDevicePresetName.Text.Trim();
      if (String.IsNullOrEmpty(name)) {
        this.ClearMidiNewDevicePresetName();
      }
    }

    private void MidiNewDeviceNewPresetNameGotFocus(object sender, RoutedEventArgs e) {
      this.midiNewDevicePresetName.Foreground = new SolidColorBrush(Colors.Black);
      this.midiNewDevicePresetName.FontStyle = FontStyles.Normal;
      var name = this.midiNewDevicePresetName.Text.Trim();
      if (String.Equals(name, MidiPresetEditor.NewPresetPlaceholder)) {
        this.midiNewDevicePresetName.Text = "";
      }
    }

    private void ClearMidiNewDevicePresetName() {
      this.midiNewDevicePresetName.Text =
        MidiPresetEditor.NewPresetPlaceholder;
      this.midiNewDevicePresetName.Foreground = new SolidColorBrush(Colors.Gray);
      this.midiNewDevicePresetName.FontStyle = FontStyles.Italic;
    }

    private MidiPreset? AddNewMidiPresetWithName(string presetName) {
      if (!this.midiPresetEditor.TryCreatePreset(
          presetName, out MidiPreset? newPreset)) {
        return null;
      }
      this.AddMidiPresetToControls(newPreset);
      return newPreset;
    }

    private void AddMidiPresetToControls(MidiPreset preset) {
      this.midiNewDevicePreset.Items.Insert(
        this.midiNewDevicePreset.Items.Count - 1,
        preset.Name
      );
      this.midiPresetIndices.Add(preset.id);

      this.midiPresetList.Items.Add(preset.Name);
    }

    private void MidiAddDeviceClicked(object sender, RoutedEventArgs e) {
      if (this.midiNewDevicePreset.SelectedIndex == -1) {
        this.midiNewDevicePreset.Focus();
        return;
      }
      if (this.midiDevices.SelectedIndex == -1) {
        this.midiDevices.Focus();
        return;
      }
      int presetID;
      string presetName;
      if (this.midiNewDevicePreset.SelectedIndex >= this.midiPresetIndices.Count) {
        // "New preset" was selected
        var result = this.AddNewMidiPresetWithName(this.midiNewDevicePresetName.Text);
        if (result == null) {
          this.midiNewDevicePresetName.Focus();
          return;
        }
        presetID = result.id;
        presetName = result.Name ?? "(unnamed preset)";
      } else {
        presetID = this.midiPresetIndices[this.midiNewDevicePreset.SelectedIndex];
        presetName = this.config.midiPresets[presetID].Name ??
          "(unnamed preset)";
      }
      var deviceID = this.midiDeviceIndices[this.midiDevices.SelectedIndex];
      this.midiDeviceList.Items.Add(new MidiDeviceEntry {
        DeviceID = deviceID,
        PresetName = presetName,
      });
      if (!this.midiPresetEditor.TryAssignDevice(deviceID, presetID)) {
        this.LoadMidiDevices();
        this.RefreshMidiDevices(null, null);
        return;
      }
      this.midiDeviceIndices.RemoveAt(this.midiDevices.SelectedIndex);
      this.midiDevices.Items.RemoveAt(this.midiDevices.SelectedIndex);
      this.midiDevices.SelectedIndex = -1;
      this.midiNewDevicePreset.SelectedIndex = -1;
      this.ClearMidiNewDevicePresetName();
      this.midiNewDevicePresetNameGrid.Visibility = Visibility.Collapsed;

      if (this.midiPresetList.SelectedIndex >= 0) {
        var currentPresetIndex = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
        if (presetID == currentPresetIndex) {
          this.midiDeletePreset.IsEnabled = false;
        }
      }
    }

    // Delete one of the active/created "devices"
    private void MidiDeleteDeviceClicked(object sender, RoutedEventArgs e) {
      if (this.midiDeviceList.SelectedItem is not MidiDeviceEntry selected) {
        return;
      }
      if (MessageBox.Show(
          this,
          $"Remove {selected.DeviceName} from Spectrum? The preset will be kept.",
          "Remove MIDI device",
          MessageBoxButton.YesNo,
          MessageBoxImage.Warning) != MessageBoxResult.Yes) {
        return;
      }
      MidiDeviceEntry item = selected;
      if (!this.midiPresetEditor.TryRemoveDevice(
          item.DeviceID, out int presetIndex)) {
        this.LoadMidiDevices();
        return;
      }
      this.midiDeviceList.Items.RemoveAt(this.midiDeviceList.SelectedIndex);
      this.RefreshMidiDevices(null, null);

      if (this.midiPresetList.SelectedIndex >= 0) {
        var currentPresetIndex = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
        if (
          presetIndex == currentPresetIndex &&
          !this.config.midiDevices.ContainsValue(currentPresetIndex)
        ) {
          this.midiDeletePreset.IsEnabled = true;
        }
      }
    }

    // Take a selected active/created "device" and load its preset into the preset panel
    private void MidiLoadPresetClicked(object sender, RoutedEventArgs e) {
      MidiDeviceEntry item = (MidiDeviceEntry)this.midiDeviceList.SelectedItem;
      this.midiPresetList.SelectedItem = item.PresetName;
    }

    private void MidiDeviceListSelectionChanged(object sender, SelectionChangedEventArgs e) {
      var deviceSelected = this.midiDeviceList.SelectedIndex >= 0;
      this.midiDeleteDevice.IsEnabled = deviceSelected;
      this.midiLoadDevicePreset.IsEnabled = deviceSelected;
    }

    private void MidiAddPresetClicked(object sender, RoutedEventArgs e) {
      if (this.currentlyEditingPreset.HasValue) {
        int presetId = this.currentlyEditingPreset.Value;
        if (!this.midiPresetEditor.TryRenamePreset(
            presetId,
            this.midiNewPresetName.Text,
            out MidiPreset? renamed)) {
          this.midiNewPresetName.Focus();
          return;
        }
        int presetIndex = this.midiPresetIndices.IndexOf(presetId);
        if (presetIndex < 0) {
          this.midiPresetList.Items.Clear();
          this.LoadPresets();
          this.LoadMidiDevices();
          this.ClearMidiNewPresetName();
          return;
        }
        this.LoadMidiDevices();
        this.midiPresetList.Items[presetIndex] = renamed.Name;
        this.midiNewDevicePreset.Items[presetIndex] = renamed.Name;
      } else {
        var result = this.AddNewMidiPresetWithName(this.midiNewPresetName.Text);
        if (result == null) {
          this.midiNewPresetName.Focus();
          return;
        }
      }
      this.ClearMidiNewPresetName();
    }

    private void MidiDeletePresetClicked(object sender, RoutedEventArgs e) {
      if (this.midiPresetList.SelectedIndex < 0) {
        return;
      }
      string name = this.midiPresetList.SelectedItem?.ToString() ?? "this preset";
      if (MessageBox.Show(
          this,
          $"Delete {name}? This cannot be undone.",
          "Delete MIDI preset",
          MessageBoxButton.YesNo,
          MessageBoxImage.Warning) != MessageBoxResult.Yes) {
        return;
      }
      var presetID = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
      if (!this.midiPresetEditor.TryDeletePreset(presetID)) {
        this.midiDeletePreset.IsEnabled = false;
        return;
      }
      this.midiPresetIndices.RemoveAt(this.midiPresetList.SelectedIndex);
      this.midiNewDevicePreset.Items.RemoveAt(this.midiPresetList.SelectedIndex);
      this.midiPresetList.Items.RemoveAt(this.midiPresetList.SelectedIndex);
    }

    private void MidiPresetListSelectionChanged(object sender, SelectionChangedEventArgs e) {
      this.midiBindingList.Items.Clear();
      if (this.currentlyEditingPreset.HasValue) {
        this.MidiCancelEditPresetClicked(null, null);
      }
      if (this.currentlyEditingBinding.HasValue) {
        this.MidiCancelEditBindingClicked(null, null);
      }
      if (this.midiPresetList.SelectedIndex < 0) {
        this.midiDeletePreset.IsEnabled = false;
        this.midiClonePreset.IsEnabled = false;
        this.midiRenamePreset.IsEnabled = false;
        this.midiAddBinding.IsEnabled = false;
        return;
      }
      var presetID = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
      this.midiDeletePreset.IsEnabled =
        this.midiPresetEditor.CanDeletePreset(presetID);
      this.midiClonePreset.IsEnabled = true;
      this.midiRenamePreset.IsEnabled = true;
      this.midiAddBinding.IsEnabled = true;
      foreach (var binding in this.config.midiPresets[presetID].Bindings) {
        ComboBoxItem item = (ComboBoxItem)this.midiBindingType.Items[binding.BindingType];
        this.midiBindingList.Items.Add(new MidiBindingEntry() {
          BindingName = binding.BindingName ?? "(unnamed binding)",
          BindingTypeName = (string)(item.Content),
        });
      }
    }

    private void MidiClonePresetClicked(object sender, RoutedEventArgs e) {
      if (this.midiPresetList.SelectedIndex < 0) {
        return;
      }
      var presetID = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
      if (!this.midiPresetEditor.TryClonePreset(
          presetID, out MidiPreset? clonedPreset)) {
        return;
      }
      this.AddMidiPresetToControls(clonedPreset);
    }

    private void MidiRenamePresetClicked(object sender, RoutedEventArgs e) {
      if (this.midiPresetList.SelectedIndex < 0) {
        return;
      }
      var presetID = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
      this.currentlyEditingPreset = presetID;

      this.midiPresetEditLabel.Content = "Rename preset";
      this.midiNewPresetName.Width = 120;
      this.midiAddPreset.Content = "Save";
      this.midiAddPreset.Margin = new Thickness(0, 0, 55, 0);
      this.midiCancelEditPreset.Visibility = Visibility.Visible;
      this.midiNewPresetName.Text =
        this.config.midiPresets[presetID].Name ?? string.Empty;
      this.midiNewPresetName.Focus();
      this.midiNewPresetName.SelectionStart = this.midiNewPresetName.Text.Length;
      this.midiNewPresetName.SelectionLength = 0;
    }

    private void MidiCancelEditPresetClicked(
      object? sender, RoutedEventArgs? e
    ) {
      if (!this.currentlyEditingPreset.HasValue) {
        return;
      }
      this.currentlyEditingPreset = null;
      this.midiPresetEditLabel.Content = "Add preset";
      this.midiNewPresetName.Width = 140;
      this.midiAddPreset.Content = "Add preset";
      this.midiAddPreset.Margin = new Thickness(0, 0, 0, 0);
      this.midiCancelEditPreset.Visibility = Visibility.Collapsed;
      this.ClearMidiNewPresetName();
    }

    private void MidiNewPresetNameLostFocus(object sender, RoutedEventArgs e) {
      var name = this.midiNewPresetName.Text.Trim();
      if (String.IsNullOrEmpty(name)) {
        this.ClearMidiNewPresetName();
      }
    }

    private void MidiNewPresetNameGotFocus(object sender, RoutedEventArgs e) {
      this.midiNewPresetName.Foreground = new SolidColorBrush(Colors.Black);
      this.midiNewPresetName.FontStyle = FontStyles.Normal;
      var name = this.midiNewPresetName.Text.Trim();
      if (String.Equals(name, MidiPresetEditor.NewPresetPlaceholder)) {
        this.midiNewPresetName.Text = "";
      }
    }

    private void ClearMidiNewPresetName() {
      this.midiNewPresetName.Text = MidiPresetEditor.NewPresetPlaceholder;
      this.midiNewPresetName.Foreground = new SolidColorBrush(Colors.Gray);
      this.midiNewPresetName.FontStyle = FontStyles.Italic;
    }

    private void MidiBindingTypeSelectionChanged(object sender, SelectionChangedEventArgs e) {
      this.ClearMidiBindingValidation();
      this.midiTapTempoBindingPanel.Visibility = this.midiBindingType.SelectedIndex == 0
        ? Visibility.Visible
        : Visibility.Collapsed;
      this.midiContinuousKnobBindingPanel.Visibility = this.midiBindingType.SelectedIndex == 1
        ? Visibility.Visible
        : Visibility.Collapsed;
      this.midiDiscreteKnobBindingPanel.Visibility = this.midiBindingType.SelectedIndex == 2
        ? Visibility.Visible
        : Visibility.Collapsed;
      this.midiLogarithmicKnobBindingPanel.Visibility = this.midiBindingType.SelectedIndex == 3
        ? Visibility.Visible
        : Visibility.Collapsed;
      this.midiAdsrLevelDriverBindingPanel.Visibility = this.midiBindingType.SelectedIndex == 4
        ? Visibility.Visible
        : Visibility.Collapsed;
    }

    private void MidiAddBindingClicked(object sender, RoutedEventArgs e) {
      if (this.midiPresetList.SelectedIndex == -1) {
        this.midiPresetList.Focus();
        return;
      }

      MidiBindingDraft draft = this.CaptureMidiBindingDraft();
      if (!MidiBindingEditor.TryCreate(
          draft,
          out IMidiBindingConfig? newBinding,
          out MidiBindingValidationError? validationError)) {
        this.ShowMidiBindingValidation(validationError);
        return;
      }

      int editingBindingIndex =
        this.currentlyEditingBinding.GetValueOrDefault();
      bool editing = this.currentlyEditingBinding.HasValue;

      int editedPresetId =
        this.midiPresetIndices[this.midiPresetList.SelectedIndex];
      MidiPreset midiPreset =
        this.config.midiPresets[editedPresetId].ToPreset();
      if (editing) {
        midiPreset.Bindings[editingBindingIndex] = newBinding;
      } else {
        midiPreset.Bindings.Add(newBinding);
      }
      this.config.UpsertMidiPreset(editedPresetId, midiPreset);

      this.ClearMidiBindingFields(draft.BindingType);

      ComboBoxItem item = (ComboBoxItem)this.midiBindingType.SelectedItem;
      string bindingTypeName = (string)item.Content;
      string newName = newBinding.BindingName ?? "";
      if (editing) {
        var entry = (MidiBindingEntry)
          this.midiBindingList.Items[editingBindingIndex];
        this.midiBindingList.Items[editingBindingIndex] = new MidiBindingEntry() {
          BindingName = newName,
          BindingTypeName = entry.BindingTypeName,
        };
      } else {
        this.midiBindingList.Items.Add(new MidiBindingEntry() {
          BindingName = newName,
          BindingTypeName = bindingTypeName,
        });
      }

      this.midiBindingType.SelectedIndex = -1;
      this.midiNewBindingName.Text = "";
      this.ClearMidiBindingValidation();
    }

    private MidiBindingDraft CaptureMidiBindingDraft() =>
      new MidiBindingDraft {
        BindingName = this.midiNewBindingName.Text,
        BindingType = this.midiBindingType.SelectedIndex,
        TapTempoButtonType = this.midiTapTempoButtonType.SelectedIndex,
        TapTempoButtonIndex = this.midiTapTempoButtonIndex.Text,
        ContinuousKnobIndex = this.midiContinuousKnobIndex.Text,
        ContinuousKnobPropertyName =
          this.midiContinuousKnobPropertyName.Text,
        ContinuousKnobStartValue =
          this.midiContinuousKnobStartValue.Text,
        ContinuousKnobEndValue = this.midiContinuousKnobEndValue.Text,
        DiscreteKnobIndex = this.midiDiscreteKnobIndex.Text,
        DiscreteKnobPropertyName = this.midiDiscreteKnobPropertyName.Text,
        DiscreteKnobNumPossibleValues =
          this.midiDiscreteKnobNumPossibleValues.Text,
        LogarithmicKnobIndex = this.midiLogarithmicKnobIndex.Text,
        LogarithmicKnobPropertyName =
          this.midiLogarithmicKnobPropertyName.Text,
        LogarithmicKnobNumPossibleValues =
          this.midiLogarithmicKnobNumPossibleValues.Text,
        LogarithmicKnobStartValue =
          this.midiLogarithmicKnobStartValue.Text,
        AdsrLevelDriverIndexRangeStart =
          this.midiAdsrLevelDriverIndexRangeStart.Text,
      };

    private void ShowMidiBindingValidation(
      MidiBindingValidationError error
    ) {
      this.midiBindingValidationMessage.Text = error.Message;
      this.midiBindingValidationMessage.Visibility = Visibility.Visible;
      Control control = error.Field switch {
        MidiBindingEditorField.BindingName => this.midiNewBindingName,
        MidiBindingEditorField.BindingType => this.midiBindingType,
        MidiBindingEditorField.TapTempoButtonType =>
          this.midiTapTempoButtonType,
        MidiBindingEditorField.TapTempoButtonIndex =>
          this.midiTapTempoButtonIndex,
        MidiBindingEditorField.ContinuousKnobIndex =>
          this.midiContinuousKnobIndex,
        MidiBindingEditorField.ContinuousKnobPropertyName =>
          this.midiContinuousKnobPropertyName,
        MidiBindingEditorField.ContinuousKnobStartValue =>
          this.midiContinuousKnobStartValue,
        MidiBindingEditorField.ContinuousKnobEndValue =>
          this.midiContinuousKnobEndValue,
        MidiBindingEditorField.DiscreteKnobIndex =>
          this.midiDiscreteKnobIndex,
        MidiBindingEditorField.DiscreteKnobPropertyName =>
          this.midiDiscreteKnobPropertyName,
        MidiBindingEditorField.DiscreteKnobNumPossibleValues =>
          this.midiDiscreteKnobNumPossibleValues,
        MidiBindingEditorField.LogarithmicKnobIndex =>
          this.midiLogarithmicKnobIndex,
        MidiBindingEditorField.LogarithmicKnobPropertyName =>
          this.midiLogarithmicKnobPropertyName,
        MidiBindingEditorField.LogarithmicKnobNumPossibleValues =>
          this.midiLogarithmicKnobNumPossibleValues,
        MidiBindingEditorField.LogarithmicKnobStartValue =>
          this.midiLogarithmicKnobStartValue,
        MidiBindingEditorField.AdsrLevelDriverIndexRangeStart =>
          this.midiAdsrLevelDriverIndexRangeStart,
        _ => this.midiBindingType,
      };
      control.Focus();
    }

    private void ClearMidiBindingValidation() {
      this.midiBindingValidationMessage.Text = "";
      this.midiBindingValidationMessage.Visibility = Visibility.Collapsed;
    }

    private void ClearMidiBindingFields(int bindingType) {
      if (bindingType == 0) {
        this.midiTapTempoButtonType.SelectedIndex = -1;
        this.midiTapTempoButtonIndex.Text = "";
      } else if (bindingType == 1) {
        this.midiContinuousKnobIndex.Text = "";
        this.midiContinuousKnobPropertyName.Text = "";
        this.midiContinuousKnobStartValue.Text = "";
        this.midiContinuousKnobEndValue.Text = "";
      } else if (bindingType == 2) {
        this.midiDiscreteKnobIndex.Text = "";
        this.midiDiscreteKnobPropertyName.Text = "";
        this.midiDiscreteKnobNumPossibleValues.Text = "";
      } else if (bindingType == 3) {
        this.midiLogarithmicKnobIndex.Text = "";
        this.midiLogarithmicKnobPropertyName.Text = "";
        this.midiLogarithmicKnobNumPossibleValues.Text = "";
        this.midiLogarithmicKnobStartValue.Text = "";
      } else if (bindingType == 4) {
        this.midiAdsrLevelDriverIndexRangeStart.Text = "";
      }
    }

    private void MidiBindingListSelectionChanged(object sender, SelectionChangedEventArgs e) {
      var buttonsEnabled = this.midiPresetList.SelectedIndex >= 0 &&
        this.midiBindingList.SelectedIndex >= 0;
      this.midiDeleteBinding.IsEnabled = buttonsEnabled;
      this.midiEditBinding.IsEnabled = buttonsEnabled;
      if (this.currentlyEditingBinding.HasValue) {
        this.MidiCancelEditBindingClicked(null, null);
      }
    }

    private void MidiDeleteBindingClicked(object sender, RoutedEventArgs e) {
      if (this.midiPresetList.SelectedIndex == -1) {
        return;
      }
      var presetID = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
      if (this.midiBindingList.SelectedIndex == -1) {
        return;
      }
      string name = (this.midiBindingList.SelectedItem as MidiBindingEntry)?.BindingName
        ?? "this binding";
      if (MessageBox.Show(
          this,
          $"Delete binding “{name}”?",
          "Delete MIDI binding",
          MessageBoxButton.YesNo,
          MessageBoxImage.Warning) != MessageBoxResult.Yes) {
        return;
      }
      var bindingID = this.midiBindingList.SelectedIndex;
      MidiPreset editedPreset = this.config.midiPresets[presetID].ToPreset();
      editedPreset.Bindings.RemoveAt(bindingID);
      this.config.UpsertMidiPreset(presetID, editedPreset);
      this.midiBindingList.Items.RemoveAt(bindingID);
    }

    private void MidiEditBindingClicked(object sender, RoutedEventArgs e) {
      if (
        this.midiPresetList.SelectedIndex < 0 ||
        this.midiBindingList.SelectedIndex < 0
      ) {
        return;
      }
      var presetID = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
      var bindingIndex = this.midiBindingList.SelectedIndex;
      var bindingConfig = this.config.midiPresets[presetID].Bindings[bindingIndex];
      this.currentlyEditingBinding = bindingIndex;

      this.midiBindingEditLabel.Content = "Edit binding";
      this.midiAddBinding.Content = "Save";
      this.midiCancelEditBinding.Visibility = Visibility.Visible;
      this.midiNewBindingName.Text =
        bindingConfig.BindingName ?? string.Empty;
      this.midiNewBindingName.Focus();
      this.midiNewBindingName.SelectionStart = this.midiNewBindingName.Text.Length;
      this.midiNewBindingName.SelectionLength = 0;

      this.midiBindingType.SelectedIndex = bindingConfig.BindingType;
      if (bindingConfig.BindingType == 0) {
        var config = (TapTempoMidiBindingView)bindingConfig;
        this.midiTapTempoButtonType.SelectedIndex =
          MidiBindingEditor.CommandTypeIndex(config.ButtonType);
        this.midiTapTempoButtonIndex.Text = config.ButtonIndex.ToString();
      } else if (this.midiBindingType.SelectedIndex == 1) {
        var config = (ContinuousKnobMidiBindingView)bindingConfig;
        this.midiContinuousKnobIndex.Text = config.KnobIndex.ToString();
        this.midiContinuousKnobPropertyName.Text = config.ConfigPropertyName;
        this.midiContinuousKnobStartValue.Text = config.StartValue.ToString();
        this.midiContinuousKnobEndValue.Text = config.EndValue.ToString();
      } else if (this.midiBindingType.SelectedIndex == 2) {
        var config = (DiscreteKnobMidiBindingView)bindingConfig;
        this.midiDiscreteKnobIndex.Text = config.KnobIndex.ToString();
        this.midiDiscreteKnobPropertyName.Text = config.ConfigPropertyName;
        this.midiDiscreteKnobNumPossibleValues.Text = config.NumPossibleValues.ToString();
      } else if (this.midiBindingType.SelectedIndex == 3) {
        var config = (DiscreteLogarithmicKnobMidiBindingView)bindingConfig;
        this.midiLogarithmicKnobIndex.Text = config.KnobIndex.ToString();
        this.midiLogarithmicKnobPropertyName.Text = config.ConfigPropertyName;
        this.midiLogarithmicKnobNumPossibleValues.Text = config.NumPossibleValues.ToString();
        this.midiLogarithmicKnobStartValue.Text = config.StartValue.ToString();
      } else if (this.midiBindingType.SelectedIndex == 4) {
        var config = (AdsrLevelDriverMidiBindingView)bindingConfig;
        this.midiAdsrLevelDriverIndexRangeStart.Text = config.IndexRangeStart.ToString();
      }
    }

    private void MidiCancelEditBindingClicked(
      object? sender, RoutedEventArgs? e
    ) {
      if (!this.currentlyEditingBinding.HasValue) {
        return;
      }

      this.currentlyEditingBinding = null;
      this.midiBindingEditLabel.Content = "Add binding";
      this.midiAddBinding.Content = "Add binding";
      this.midiCancelEditBinding.Visibility = Visibility.Collapsed;
      this.midiNewBindingName.Text = "";
      this.midiBindingType.SelectedIndex = -1;

      this.midiTapTempoButtonType.SelectedIndex = -1;
      this.midiTapTempoButtonIndex.Text = "";

      this.midiContinuousKnobIndex.Text = "";
      this.midiContinuousKnobPropertyName.Text = "";
      this.midiContinuousKnobStartValue.Text = "";
      this.midiContinuousKnobEndValue.Text = "";

      this.midiDiscreteKnobIndex.Text = "";
      this.midiDiscreteKnobPropertyName.Text = "";
      this.midiDiscreteKnobNumPossibleValues.Text = "";

      this.midiLogarithmicKnobIndex.Text = "";
      this.midiLogarithmicKnobPropertyName.Text = "";
      this.midiLogarithmicKnobNumPossibleValues.Text = "";
      this.midiLogarithmicKnobStartValue.Text = "";
      this.midiAdsrLevelDriverIndexRangeStart.Text = "";
      this.ClearMidiBindingValidation();
    }

    private void OpenVJHUD(object sender, RoutedEventArgs e) {
      this.childWindows.OpenVjHud();
    }

    private void CloseVJHUD(object sender, RoutedEventArgs e) {
      this.childWindows.CloseVjHud();
    }

    private void OpenDomeSimulator(object sender, RoutedEventArgs e) {
      this.childWindows.OpenDomeSimulator();
    }

    private void CloseDomeSimulator(object sender, RoutedEventArgs e) {
      this.childWindows.CloseDomeSimulator();
    }

    private void OpenDomeMapping(object sender, RoutedEventArgs e) {
      this.childWindows.OpenDomeMapping();
    }

    private void WandSerialPortDropDownOpened(object sender, EventArgs e) {
      this.wandSerialUi.RepopulatePorts();
    }

    private void WandSerialPortSelectionChanged(
      object sender, SelectionChangedEventArgs e
    ) {
      this.wandSerialUi.ApplySelectedPort();
    }

    private void OpenWandStatus(object sender, RoutedEventArgs e) {
      this.childWindows.OpenWandStatus();
    }

  }

}
