using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using XSerializer;
using System.IO;
using Spectrum.MIDI;
using Spectrum.Base;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Spectrum {

  public partial class MainWindow : Window {

    // LoadConfig completes this composition root before the constructor starts
    // the host or exposes the window.
    private Operator op = null!;
    private SpectrumConfiguration config;
    private SpectrumHost<Operator, Web.SpectrumWebHost> host;
    public static bool LoadingConfig { get; set; } = false;
    private MidiSetupUiController midiSetupUi = null!;
    private MainWindowChildWindows childWindows = null!;
    private WandSerialUiController wandSerialUi = null!;
    private ReadinessDashboardUiController readinessDashboard = null!;
    private DomeOpcAddressUiController domeOpcAddressUi = null!;
    private OperatorSettingsUiController operatorSettingsUi = null!;
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
      this.operatorSettingsUi.Start();
    }

    private void HandleClose(object sender, EventArgs e) {
      this.op.Enabled = false;
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
      nameof(midiSetupUi),
      nameof(childWindows),
      nameof(operatorSettingsUi))]
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
      this.childWindows = new MainWindowChildWindows(
        this.config,
        this.op,
        domeCalibrationController,
        advisoryLocks);

      this.midiSetupUi = new MidiSetupUiController(
        this.config,
        this.CreateMidiSetupView(),
        () => MidiInput.DeviceCount,
        MidiInput.GetDeviceName,
        (message, caption) => MessageBox.Show(
          this,
          message,
          caption,
          MessageBoxButton.YesNo,
          MessageBoxImage.Warning) == MessageBoxResult.Yes);
      this.midiSetupUi.Start();

      this.operatorSettingsUi = new OperatorSettingsUiController(
        this.config,
        this.op,
        new OperatorSettingsView(
          this.audioDevices,
          this.midiEnabled,
          this.domeThreadCheckbox,
          this.operatorFPSLabel,
          this.domeBeagleboneOPCFPSLabel,
          this.homeDomeFPSLabel,
          this.domeEnabled,
          this.domeSimulationEnabled,
          this.domeTestPatternSelector,
          this.domeMaxBrightnessSlider,
          this.domeMaxBrightnessLabel,
          this.domeBrightnessSlider,
          this.domeBrightnessLabel,
          this.vjHUDEnabled),
        () => Audio.AudioInput.AudioDevices,
        () => this.readinessDashboard.Refresh());
      this.domeOpcAddressUi = new DomeOpcAddressUiController(
        this.config,
        this.Dispatcher,
        this.domeBeagleboneOPCHostAndPort,
        this.domeOPCValidationStatus,
        key => this.FindResource(key));
      this.domeOpcAddressUi.Start();

      this.wandSerialUi = new WandSerialUiController(
        this.config,
        this.wandSerialPortSelector,
        this.wandReceiverStatus,
        WandSerialReceiver.AvailablePorts,
        () => this.op.OrientationInput.WandSerial.StatusSnapshot());
      this.wandSerialUi.Start();

      MainWindow.LoadingConfig = false;
    }

    private MidiSetupView CreateMidiSetupView() =>
      new MidiSetupView(
        new MidiDeviceSetupView(
          this.midiDeviceList,
          this.midiLoadDevicePreset,
          this.midiDeleteDevice,
          this.midiNewDevicePreset,
          this.midiNewDevicePresetNameGrid,
          this.midiNewDevicePresetName,
          this.midiDevices),
        new MidiPresetSetupView(
          this.midiPresetList,
          this.midiClonePreset,
          this.midiRenamePreset,
          this.midiDeletePreset,
          this.midiPresetEditLabel,
          this.midiNewPresetName,
          this.midiAddPreset,
          this.midiCancelEditPreset),
        new MidiBindingSetupView(
          this.midiBindingList,
          this.midiEditBinding,
          this.midiDeleteBinding,
          this.midiBindingEditLabel,
          this.midiNewBindingName,
          this.midiBindingType,
          this.midiTapTempoBindingPanel,
          this.midiTapTempoButtonType,
          this.midiTapTempoButtonIndex,
          this.midiContinuousKnobBindingPanel,
          this.midiContinuousKnobIndex,
          this.midiContinuousKnobPropertyName,
          this.midiContinuousKnobStartValue,
          this.midiContinuousKnobEndValue,
          this.midiDiscreteKnobBindingPanel,
          this.midiDiscreteKnobIndex,
          this.midiDiscreteKnobPropertyName,
          this.midiDiscreteKnobNumPossibleValues,
          this.midiLogarithmicKnobBindingPanel,
          this.midiLogarithmicKnobIndex,
          this.midiLogarithmicKnobPropertyName,
          this.midiLogarithmicKnobNumPossibleValues,
          this.midiLogarithmicKnobStartValue,
          this.midiAdsrLevelDriverBindingPanel,
          this.midiAdsrLevelDriverIndexRangeStart,
          this.midiBindingValidationMessage,
          this.midiAddBinding,
          this.midiCancelEditBinding));

    private void ShowMidiSetup(object sender, RoutedEventArgs e) {
      this.mainTabs.SelectedItem = this.midiTab;
    }

    private void ShowDomeSetup(object sender, RoutedEventArgs e) {
      this.mainTabs.SelectedItem = this.domeTab;
    }

    private void CopyWebControllerAddress(object sender, RoutedEventArgs e) {
      this.readinessDashboard.CopyControllerAddress();
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
      this.readinessDashboard.TogglePower();
    }

    private void RefreshAudioDevices(object? sender, RoutedEventArgs? e) {
      this.operatorSettingsUi.RefreshAudioDevices();
    }

    private void AudioInputDeviceChanged(
      object sender,
      SelectionChangedEventArgs e
    ) {
      this.operatorSettingsUi.ApplySelectedAudioDevice();
    }

    private void RefreshMidiDevices(object? sender, RoutedEventArgs? e) {
      this.midiSetupUi.RefreshDevices();
    }

    private void MidiNewDeviceSelectionChanged(
      object sender, RoutedEventArgs e
    ) {
      this.midiSetupUi.NewDevicePresetSelectionChanged();
    }

    private void MidiNewDeviceNewPresetNameLostFocus(
      object sender, RoutedEventArgs e
    ) {
      this.midiSetupUi.NewDevicePresetNameLostFocus();
    }

    private void MidiNewDeviceNewPresetNameGotFocus(
      object sender, RoutedEventArgs e
    ) {
      this.midiSetupUi.NewDevicePresetNameGotFocus();
    }

    private void MidiAddDeviceClicked(
      object sender, RoutedEventArgs e
    ) {
      this.midiSetupUi.AddDevice();
    }

    private void MidiDeleteDeviceClicked(
      object sender, RoutedEventArgs e
    ) {
      this.midiSetupUi.DeleteSelectedDevice();
    }

    private void MidiLoadPresetClicked(
      object sender, RoutedEventArgs e
    ) {
      this.midiSetupUi.LoadSelectedDevicePreset();
    }

    private void MidiDeviceListSelectionChanged(
      object sender, SelectionChangedEventArgs e
    ) {
      this.midiSetupUi.ConfiguredDeviceSelectionChanged();
    }

    private void MidiAddPresetClicked(
      object sender, RoutedEventArgs e
    ) {
      this.midiSetupUi.SavePreset();
    }

    private void MidiDeletePresetClicked(
      object sender, RoutedEventArgs e
    ) {
      this.midiSetupUi.DeleteSelectedPreset();
    }

    private void MidiPresetListSelectionChanged(
      object sender, SelectionChangedEventArgs e
    ) {
      this.midiSetupUi.PresetSelectionChanged();
    }

    private void MidiClonePresetClicked(
      object sender, RoutedEventArgs e
    ) {
      this.midiSetupUi.CloneSelectedPreset();
    }

    private void MidiRenamePresetClicked(
      object sender, RoutedEventArgs e
    ) {
      this.midiSetupUi.BeginPresetRename();
    }

    private void MidiCancelEditPresetClicked(
      object? sender, RoutedEventArgs? e
    ) {
      this.midiSetupUi.CancelPresetEdit();
    }

    private void MidiNewPresetNameLostFocus(
      object sender, RoutedEventArgs e
    ) {
      this.midiSetupUi.PresetNameLostFocus();
    }

    private void MidiNewPresetNameGotFocus(
      object sender, RoutedEventArgs e
    ) {
      this.midiSetupUi.PresetNameGotFocus();
    }

    private void MidiBindingTypeSelectionChanged(
      object sender, SelectionChangedEventArgs e
    ) {
      this.midiSetupUi.BindingTypeSelectionChanged();
    }

    private void MidiAddBindingClicked(
      object sender, RoutedEventArgs e
    ) {
      this.midiSetupUi.SaveBinding();
    }

    private void MidiBindingListSelectionChanged(
      object sender, SelectionChangedEventArgs e
    ) {
      this.midiSetupUi.BindingSelectionChanged();
    }

    private void MidiDeleteBindingClicked(
      object sender, RoutedEventArgs e
    ) {
      this.midiSetupUi.DeleteSelectedBinding();
    }

    private void MidiEditBindingClicked(
      object sender, RoutedEventArgs e
    ) {
      this.midiSetupUi.BeginBindingEdit();
    }

    private void MidiCancelEditBindingClicked(
      object? sender, RoutedEventArgs? e
    ) {
      this.midiSetupUi.CancelBindingEdit();
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
