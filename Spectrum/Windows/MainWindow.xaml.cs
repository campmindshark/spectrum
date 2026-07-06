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
using System.Reflection;

namespace Spectrum {

  using Timer = System.Windows.Forms.Timer;

  public partial class MainWindow : Window {

    // One entry in the wand receiver-port combo. The display string is kept
    // separate from the value so picking an item shown as "COM7 (missing)" or
    // "(none)" writes the real port ("COM7" / "") into config.wandSerialPort,
    // not the label.
    private class PortItem {
      public string Display { get; set; }
      public string Value { get; set; }
      public override string ToString() => this.Display;
    }

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

      public string PresetName { get; set; }

    }

    private class MidiBindingEntry {
      public string BindingName { get; set; }
      public string BindingTypeName { get; set; }
    }

    private static readonly HashSet<string> configPropertiesToRebootOn = new HashSet<string>() {
      "midiInputInSeparateThread",
      "domeOutputInSeparateThread",
    };
    private static readonly HashSet<string> configPropertiesIgnored = new HashSet<string>() {
      "operatorFPS",
      "domeBeagleboneOPCFPS",
      "beatBroadcaster",
      "domeCommandQueue",
      "domeCalibrationActive",
      "domeCalibrationCableIndex",
    };

    private Operator op;
    private SpectrumConfiguration config;
    public static bool LoadingConfig { get; set; } = false;
    private List<int> midiDeviceIndices;
    private List<int> midiPresetIndices;
    private DomeSimulatorWindow domeSimulatorWindow;
    private DomeMappingWindow domeMappingWindow;
    private VJHUDWindow vjHUDWindow;
    private WandStatusWindow wandStatusWindow;
    // Guards programmatic repopulate/preselect of wandSerialPortSelector so it
    // never writes config back; only a genuine user pick does.
    private bool repopulatingWandPorts = false;
    private System.Windows.Threading.DispatcherTimer wandReceiverStatusTimer;
    private int? currentlyEditingPreset = null;
    private int? currentlyEditingBinding = null;
    private Timer configSaveTimer = null;
    private Web.WebServer webServer = null;
    private Web.ConfigEventStream webEventStream = null;
    private const int WebServerPort = 8080;

    public MainWindow() {
      this.InitializeComponent();

      this.LoadConfig();
      this.config.PropertyChanged += ConfigUpdated;
      this.StartWebServer();
    }

    private void StartWebServer() {
      // All web-originated mutations funnel through this gateway, which marshals
      // onto the WPF Dispatcher so they behave exactly like native GUI writes.
      var gateway = new Web.DispatcherControlGateway(
        System.Windows.Application.Current.Dispatcher);
      var registry = Web.SpectrumParameters.BuildRegistry();
      var controls = new Web.ControlService(registry, gateway, this.config);
      // The event stream subscribes to config.PropertyChanged (and the
      // Operator's EnabledChanged) and fans changes out to connected browsers
      // over SSE.
      this.webEventStream = new Web.ConfigEventStream(registry, this.config, this.op);
      // Advisory locks guard modal ops (calibration, per-device test patterns)
      // against concurrent maintenance users.
      var locks = new Web.AdvisoryLockManager();
      // The dome-mapping calibration flow: same state machine as
      // DomeMappingWindow, driven over REST and guarded by the domeCalibration
      // lease. Writes to Configuration go through the same gateway.
      var calibration = new Web.DomeCalibrationController(
        gateway, this.config, LEDs.LEDDomeOutput.NumCables);
      // Read-only wand/orientation-device diagnostics (the web port of
      // WandStatusWindow), plus its Calibrate All action through the gateway.
      var wands = new Web.WandStatusController(
        this.op.OrientationInput, gateway, this.config);
      // The global Start/Stop button: toggles the Operator's runtime Enabled
      // flag (the same engine switch the native power button drives) through the
      // gateway.
      var operatorControl = new Web.OperatorController(this.op, gateway);
      // The maintenance "Tap" tempo button: applies a browser-computed BPM as
      // the human tap tempo (and switches the source to Human) through the
      // gateway, the same as a native tap.
      var tempo = new Web.TempoController(this.config, gateway);
      this.webServer = new Web.WebServer(
        controls, this.webEventStream, locks, calibration, wands,
        operatorControl, tempo, WebServerPort);
      this.webServer.Start();
    }

    private void ConfigUpdated(object sender, PropertyChangedEventArgs e) {
      if (configPropertiesToRebootOn.Contains(e.PropertyName)) {
        this.op.Reboot();
      }
      // The wand receiver-port combo has a host-dependent item set, so it can't
      // use the two-way WPF Bind() every other control gets. Instead re-run the
      // guarded repopulate + preselect when the value changes externally (e.g. a
      // web PUT landing on the Dispatcher) — the dynamic-item equivalent of the
      // two-way binding. The repopulating flag stops the resulting
      // SelectionChanged from writing back.
      if (e.PropertyName == nameof(this.config.wandSerialPort)) {
        this.Dispatcher.BeginInvoke(
          new Action(this.RepopulateWandSerialPorts));
      }
      if (!configPropertiesIgnored.Contains(e.PropertyName)) {
        this.EventuallySaveConfig();
      }
    }

    private void HandleClose(object sender, EventArgs e) {
      this.op.Enabled = false;
      // Fire-and-forget: Kestrel shuts down on background threads, and the
      // process is exiting anyway, so don't block the UI thread waiting on it.
      this.webServer?.StopAsync();
      this.webEventStream?.Dispose();
      this.SaveConfig();
    }

    private void SaveConfig() {
      if (MainWindow.LoadingConfig) {
        return;
      }
      // We keep around the old config in case the new config causes a crash
      if (File.Exists("spectrum_config.xml")) {
        File.Copy("spectrum_config.xml", "spectrum_old_config.xml", true);
      }
      using (
        FileStream stream = new FileStream(
          "spectrum_config.xml",
          FileMode.Create
        )
      ) {
        new XmlSerializer<SpectrumConfiguration>().Serialize(
          stream,
          this.config
        );
      }
    }

    private void EventuallySaveConfig() {
      lock (this.op) {
        if (this.configSaveTimer == null) {
          this.configSaveTimer = new Timer();
          this.configSaveTimer.Interval = 100;
          this.configSaveTimer.Tick += DelayedConfigSave;
          this.configSaveTimer.Start();
        }
      }
    }

    private void DelayedConfigSave(object sender, EventArgs e) {
      lock (this.op) {
        this.SaveConfig();
        this.configSaveTimer.Stop();
        this.configSaveTimer.Dispose();
        this.configSaveTimer = null;
      }
    }

    private void LoadConfig() {
      MainWindow.LoadingConfig = true;

      string loadFile = null;
      if (File.Exists("spectrum_config.xml")) {
        loadFile = "spectrum_config.xml";
      } else if (File.Exists("spectrum_default_config.xml")) {
        loadFile = "spectrum_default_config.xml";
      }
      if (loadFile != null) {
        using (FileStream stream = File.OpenRead(loadFile)) {
          this.config = new XmlSerializer<SpectrumConfiguration>(
          ).Deserialize(stream);
        }
      }
      if (this.config == null) {
        this.config = new SpectrumConfiguration();
      }
      this.op = new Operator(this.config);

      this.RefreshAudioDevices(null, null);
      this.RefreshMidiDevices(null, null);
      this.LoadPresets();

      this.Bind("midiInputEnabled", this.midiEnabled, CheckBox.IsCheckedProperty);
      this.Bind("midiInputInSeparateThread", this.midiThreadCheckbox, CheckBox.IsCheckedProperty);
      this.Bind("domeOutputInSeparateThread", this.domeThreadCheckbox, CheckBox.IsCheckedProperty);
      this.Bind("operatorFPS", this.operatorFPSLabel, Label.ContentProperty);
      this.Bind("operatorFPS", this.operatorFPSLabel, Label.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("domeBeagleboneOPCAddress", this.domeBeagleboneOPCHostAndPort, TextBox.TextProperty);
      this.Bind("domeBeagleboneOPCFPS", this.domeBeagleboneOPCFPSLabel, Label.ContentProperty);
      this.Bind("domeBeagleboneOPCFPS", this.domeBeagleboneOPCFPSLabel, Label.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("domeOutputInSeparateThread", this.domeBeagleboneOPCFPSLabel, Label.VisibilityProperty, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("domeOutputInSeparateThread", this.domeBeagleboneOPCHostAndPort, ComboBox.WidthProperty, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("domeTestPattern", this.domeTestPattern, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<int, ComboBoxItem>(new Dictionary<int, ComboBoxItem> { [0] = this.domeTestPatternNone, [1] = this.domeTestPatternFlashColorsByStrut, [2] = this.domeTestPatternIterateThroughStruts, [3] = this.domeTestPatternStripTest, [4] = this.domeTestPatternFullColorFlash }, true));
      this.Bind("domeActiveVis", this.domeActiveVisualizer, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<int, ComboBoxItem>(new Dictionary<int, ComboBoxItem> {
        [0] = this.domeActiveVisualizerVolume,
        [1] = this.domeActiveVisualizerRadial,
        [2] = this.domeActiveVisualizerRace,
        [3] = this.domeActiveVisualizerSnakes,
        [4] = this.domeActiveVisualizerQuaternionTest,
        [5] = this.domeActiveVisualizerQuaternionMultiTest,
        [6] = this.domeActiveVisualizerQuaternionPaintbrush,
        [7] = this.domeActiveVisualizerSplat
      }, true));
      this.Bind("domeEnabled", this.domeEnabled, CheckBox.IsCheckedProperty);
      this.Bind("domeSimulationEnabled", this.domeSimulationEnabled, CheckBox.IsCheckedProperty);
      this.Bind("domeMaxBrightness", this.domeMaxBrightnessSlider, Slider.ValueProperty);
      this.Bind("domeMaxBrightness", this.domeMaxBrightnessLabel, Label.ContentProperty);
      this.Bind("domeBrightness", this.domeBrightnessSlider, Slider.ValueProperty);
      this.Bind("domeBrightness", this.domeBrightnessLabel, Label.ContentProperty);
      this.Bind("domeVolumeAnimationSize", this.domeVolumeAnimationSize, ComboBox.SelectedIndexProperty);
      this.Bind("domeAutoFlashDelay", this.domeAutoFlashDelay, TextBox.TextProperty);
      this.Bind("vjHUDEnabled", this.vjHUDEnabled, CheckBox.IsCheckedProperty);

      this.InitWandSerialUI();

      MainWindow.LoadingConfig = false;
    }

    private void Bind(
      string configPath,
      FrameworkElement element,
      DependencyProperty property,
      BindingMode mode = BindingMode.TwoWay,
      IValueConverter converter = null,
      object source = null
    ) {
      var binding = new System.Windows.Data.Binding(configPath);
      binding.Source = source != null ? source : this.config;
      binding.Mode = mode;
      if (converter != null) {
        binding.Converter = converter;
      }
      element.SetBinding(property, binding);
    }

    private void SliderStarted(object sender, DragStartedEventArgs e) {
      MainWindow.LoadingConfig = true;
    }

    private void SliderCompleted(object sender, DragCompletedEventArgs e) {
      MainWindow.LoadingConfig = false;
    }

    private void PowerButtonClicked(object sender, RoutedEventArgs e) {
      if (this.op.Enabled) {
        this.op.Enabled = false;
        this.powerButton.Content = "Go";
      } else {
        this.op.Enabled = true;
        this.powerButton.Content = "Stop";
      }
    }

    private void RefreshAudioDevices(object sender, RoutedEventArgs e) {
      this.op.Enabled = false;
      this.powerButton.Content = "Go";

      var audioDevices = AudioInput.AudioDevices;

      this.audioDevices.Items.Clear();
      foreach (var audioDevice in audioDevices) {
        this.audioDevices.Items.Add(audioDevice);
      }

      this.audioDevices.SelectedIndex = audioDevices.FindIndex(
        device => device.id == this.config.audioDeviceID
      );
    }

    private void AudioInputDeviceChanged(
      object sender,
      SelectionChangedEventArgs e
    ) {
      if (this.audioDevices.SelectedIndex == -1) {
        return;
      }
      this.config.audioDeviceID = ((AudioDevice)this.audioDevices.SelectedItem).id;
      this.op.Reboot();
    }

    // Refresh the list of available devices for the "Add device" panel
    private void RefreshMidiDevices(object sender, RoutedEventArgs e) {
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
          PresetName = this.config.midiPresets[pair.Value].Name,
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
        this.midiPresetIndices.Add(midiPreset.id);
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
      if (String.Equals(name, "New preset name")) {
        this.midiNewDevicePresetName.Text = "";
      }
    }

    private void ClearMidiNewDevicePresetName() {
      this.midiNewDevicePresetName.Text = "New preset name";
      this.midiNewDevicePresetName.Foreground = new SolidColorBrush(Colors.Gray);
      this.midiNewDevicePresetName.FontStyle = FontStyles.Italic;
    }

    private string ValidateNewMidiPresetName(string presetName) {
      var newPresetName = presetName.Trim();
      if (
        String.IsNullOrEmpty(newPresetName) ||
        String.Equals(newPresetName, "New preset name") ||
        this.MidiPresetNameExists(newPresetName)
      ) {
        return null;
      }
      return newPresetName;
    }

    private MidiPreset AddNewMidiPresetWithName(string presetName) {
      var newPresetName = this.ValidateNewMidiPresetName(presetName);
      if (newPresetName == null) {
        return null;
      }
      int newID = this.getNextMidiPresetID();
      var newPreset = new MidiPreset() { id = newID, Name = newPresetName };
      this.AddMidiPreset(newPreset);
      return newPreset;
    }

    private void AddMidiPreset(MidiPreset preset) {
      var newMidiPresets = new Dictionary<int, MidiPreset>(this.config.midiPresets);
      newMidiPresets[preset.id] = preset;
      this.config.midiPresets = newMidiPresets;
      this.midiNewDevicePreset.Items.Insert(
        this.midiNewDevicePreset.Items.Count - 1,
        preset.Name
      );
      this.midiPresetIndices.Add(preset.id);

      this.midiPresetList.Items.Add(preset.Name);
    }

    private int getNextMidiPresetID() {
      int newID = -1;
      foreach (var pair in this.config.midiPresets) {
        if (pair.Key > newID) {
          newID = pair.Key;
        }
      }
      return newID + 1;
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
        }
        presetID = result.id;
        presetName = result.Name;
      } else {
        presetID = this.midiPresetIndices[this.midiNewDevicePreset.SelectedIndex];
        presetName = this.config.midiPresets[presetID].Name;
      }
      var deviceID = this.midiDeviceIndices[this.midiDevices.SelectedIndex];
      this.midiDeviceList.Items.Add(new MidiDeviceEntry {
        DeviceID = deviceID,
        PresetName = presetName,
      });
      var newDevices = new Dictionary<int, int>(this.config.midiDevices);
      newDevices.Add(deviceID, presetID);
      this.config.midiDevices = newDevices;
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
      var newDevices = new Dictionary<int, int>(this.config.midiDevices);
      MidiDeviceEntry item = (MidiDeviceEntry)this.midiDeviceList.SelectedItem;
      var presetIndex = newDevices[item.DeviceID];
      newDevices.Remove(item.DeviceID);
      this.config.midiDevices = newDevices;
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
        var newPresetName = this.ValidateNewMidiPresetName(this.midiNewPresetName.Text);
        if (newPresetName == null) {
          this.midiNewPresetName.Focus();
          return;
        }
        // We don't need to reset the whole midiPresets var to trigger observers since nobody
        // cares what presets are named
        this.config.midiPresets[this.currentlyEditingPreset.Value].Name = newPresetName;
        this.SaveConfig();
        var presetIndex = this.midiPresetIndices[this.currentlyEditingPreset.Value];
        this.LoadMidiDevices();
        this.midiPresetList.Items[presetIndex] = newPresetName;
        this.midiNewDevicePreset.Items[presetIndex] = newPresetName;
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
      var presetID = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
      var newPresets = new Dictionary<int, MidiPreset>(this.config.midiPresets);
      newPresets.Remove(presetID);
      this.config.midiPresets = newPresets;
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
      this.midiDeletePreset.IsEnabled = !this.config.midiDevices.ContainsValue(presetID);
      this.midiClonePreset.IsEnabled = true;
      this.midiRenamePreset.IsEnabled = true;
      this.midiAddBinding.IsEnabled = true;
      foreach (var binding in this.config.midiPresets[presetID].Bindings) {
        ComboBoxItem item = (ComboBoxItem)this.midiBindingType.Items[binding.BindingType];
        this.midiBindingList.Items.Add(new MidiBindingEntry() {
          BindingName = binding.BindingName,
          BindingTypeName = (string)(item.Content),
        });
      }
    }

    private bool MidiPresetNameExists(string name) {
      foreach (var pair in this.config.midiPresets) {
        if (pair.Value.Name == name) {
          return true;
        }
      }
      return false;
    }

    private void MidiClonePresetClicked(object sender, RoutedEventArgs e) {
      if (this.midiPresetList.SelectedIndex < 0) {
        return;
      }
      var presetID = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
      MidiPreset clonedPreset = (MidiPreset)this.config.midiPresets[presetID].Clone();
      clonedPreset.id = this.getNextMidiPresetID();

      string newName = clonedPreset.Name + " (clone)";
      int i = 1;
      while (true) {
        if (!this.MidiPresetNameExists(newName)) {
          break;
        }
        newName = clonedPreset.Name + " (clone " + ++i + ")";
      }
      clonedPreset.Name = newName;

      this.AddMidiPreset(clonedPreset);
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
      this.midiNewPresetName.Text = this.config.midiPresets[presetID].Name;
      this.midiNewPresetName.Focus();
      this.midiNewPresetName.SelectionStart = this.midiNewPresetName.Text.Length;
      this.midiNewPresetName.SelectionLength = 0;
    }

    private void MidiCancelEditPresetClicked(object sender, RoutedEventArgs e) {
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
      if (String.Equals(name, "New preset name")) {
        this.midiNewPresetName.Text = "";
      }
    }

    private void ClearMidiNewPresetName() {
      this.midiNewPresetName.Text = "New preset name";
      this.midiNewPresetName.Foreground = new SolidColorBrush(Colors.Gray);
      this.midiNewPresetName.FontStyle = FontStyles.Italic;
    }

    private void MidiBindingTypeSelectionChanged(object sender, SelectionChangedEventArgs e) {
      this.midiChangeColorBindingPanel.Visibility = this.midiBindingType.SelectedIndex == 0
        ? Visibility.Visible
        : Visibility.Collapsed;
      this.midiTapTempoBindingPanel.Visibility = this.midiBindingType.SelectedIndex == 1
        ? Visibility.Visible
        : Visibility.Collapsed;
      this.midiContinuousKnobBindingPanel.Visibility = this.midiBindingType.SelectedIndex == 2
        ? Visibility.Visible
        : Visibility.Collapsed;
      this.midiDiscreteKnobBindingPanel.Visibility = this.midiBindingType.SelectedIndex == 3
        ? Visibility.Visible
        : Visibility.Collapsed;
      this.midiLogarithmicKnobBindingPanel.Visibility = this.midiBindingType.SelectedIndex == 4
        ? Visibility.Visible
        : Visibility.Collapsed;
      this.midiAdsrLevelDriverBindingPanel.Visibility = this.midiBindingType.SelectedIndex == 5
        ? Visibility.Visible
        : Visibility.Collapsed;
    }

    private static MidiCommandType commandTypeFromIndex(int index) {
      if (index == 0) {
        return MidiCommandType.Knob;
      } else if (index == 1) {
        return MidiCommandType.Program;
      } else if (index == 2) {
        return MidiCommandType.Note;
      }
      throw new Exception("invalid CommandType index " + index);
    }

    private static int indexFromCommandType(MidiCommandType commandType) {
      if (commandType == MidiCommandType.Knob) {
        return 0;
      } else if (commandType == MidiCommandType.Program) {
        return 1;
      } else if (commandType == MidiCommandType.Note) {
        return 2;
      }
      throw new Exception("invalid CommandType " + commandType.ToString());
    }

    private void MidiAddBindingClicked(object sender, RoutedEventArgs e) {
      if (this.midiPresetList.SelectedIndex == -1) {
        this.midiPresetList.Focus();
        return;
      }
      if (this.midiBindingType.SelectedIndex == -1) {
        this.midiBindingType.Focus();
        return;
      }
      var newName = this.midiNewBindingName.Text.Trim();
      if (String.IsNullOrEmpty(newName)) {
        this.midiNewBindingName.Text = "";
        this.midiNewBindingName.Focus();
        return;
      }

      bool editing = this.currentlyEditingBinding.HasValue;
      IMidiBindingConfig newBinding;
      if (this.midiBindingType.SelectedIndex == 0) {
        int indexRangeStart;
        try {
          indexRangeStart = Convert.ToInt32(this.midiChangeColorIndexRangeStart.Text.Trim());
        } catch (Exception) {
          this.midiChangeColorIndexRangeStart.Text = "";
          this.midiChangeColorIndexRangeStart.Focus();
          return;
        }
        newBinding = new ColorPaletteMidiBindingConfig() {
          BindingName = newName,
          indexRangeStart = indexRangeStart,
        };
      } else if (this.midiBindingType.SelectedIndex == 1) {
        if (this.midiTapTempoButtonType.SelectedIndex == -1) {
          this.midiTapTempoButtonType.Focus();
          return;
        }
        var buttonType = commandTypeFromIndex(this.midiTapTempoButtonType.SelectedIndex);
        int buttonIndex;
        try {
          buttonIndex = Convert.ToInt32(this.midiTapTempoButtonIndex.Text.Trim());
        } catch (Exception) {
          this.midiTapTempoButtonIndex.Text = "";
          this.midiTapTempoButtonIndex.Focus();
          return;
        }
        newBinding = new TapTempoMidiBindingConfig() {
          BindingName = newName,
          buttonType = buttonType,
          buttonIndex = buttonIndex,
        };
      } else if (this.midiBindingType.SelectedIndex == 2) {
        string configPropertyName = this.midiContinuousKnobPropertyName.Text.Trim();
        if (String.IsNullOrEmpty(configPropertyName)) {
          this.midiContinuousKnobPropertyName.Text = "";
          this.midiContinuousKnobPropertyName.Focus();
          return;
        }
        int knobIndex;
        try {
          knobIndex = Convert.ToInt32(this.midiContinuousKnobIndex.Text.Trim());
        } catch (Exception) {
          this.midiContinuousKnobIndex.Text = "";
          this.midiContinuousKnobIndex.Focus();
          return;
        }
        double startValue, endValue;
        try {
          startValue = Convert.ToDouble(this.midiContinuousKnobStartValue.Text.Trim());
        } catch (Exception) {
          this.midiContinuousKnobStartValue.Text = "";
          this.midiContinuousKnobStartValue.Focus();
          return;
        }
        try {
          endValue = Convert.ToDouble(this.midiContinuousKnobEndValue.Text.Trim());
        } catch (Exception) {
          this.midiContinuousKnobEndValue.Text = "";
          this.midiContinuousKnobEndValue.Focus();
          return;
        }
        if (endValue < startValue) {
          this.midiContinuousKnobEndValue.Text = "";
          this.midiContinuousKnobEndValue.Focus();
          return;
        }
        newBinding = new ContinuousKnobMidiBindingConfig() {
          BindingName = newName,
          knobIndex = knobIndex,
          configPropertyName = configPropertyName,
          startValue = startValue,
          endValue = endValue,
        };
      } else if (this.midiBindingType.SelectedIndex == 3) {
        string configPropertyName = this.midiDiscreteKnobPropertyName.Text.Trim();
        if (String.IsNullOrEmpty(configPropertyName)) {
          this.midiDiscreteKnobPropertyName.Text = "";
          this.midiDiscreteKnobPropertyName.Focus();
          return;
        }
        int knobIndex, numPossibleValues;
        try {
          knobIndex = Convert.ToInt32(this.midiDiscreteKnobIndex.Text.Trim());
        } catch (Exception) {
          this.midiDiscreteKnobIndex.Text = "";
          this.midiDiscreteKnobIndex.Focus();
          return;
        }
        try {
          numPossibleValues = Convert.ToInt32(this.midiDiscreteKnobNumPossibleValues.Text.Trim());
        } catch (Exception) {
          this.midiDiscreteKnobNumPossibleValues.Text = "";
          this.midiDiscreteKnobNumPossibleValues.Focus();
          return;
        }
        newBinding = new DiscreteKnobMidiBindingConfig() {
          BindingName = newName,
          knobIndex = knobIndex,
          configPropertyName = configPropertyName,
          numPossibleValues = numPossibleValues,
        };
      } else if (this.midiBindingType.SelectedIndex == 4) {
        string configPropertyName = this.midiLogarithmicKnobPropertyName.Text.Trim();
        if (String.IsNullOrEmpty(configPropertyName)) {
          this.midiLogarithmicKnobPropertyName.Text = "";
          this.midiLogarithmicKnobPropertyName.Focus();
          return;
        }
        int knobIndex, numPossibleValues;
        try {
          knobIndex = Convert.ToInt32(this.midiLogarithmicKnobIndex.Text.Trim());
        } catch (Exception) {
          this.midiLogarithmicKnobIndex.Text = "";
          this.midiLogarithmicKnobIndex.Focus();
          return;
        }
        try {
          numPossibleValues = Convert.ToInt32(this.midiLogarithmicKnobNumPossibleValues.Text.Trim());
        } catch (Exception) {
          this.midiLogarithmicKnobNumPossibleValues.Text = "";
          this.midiLogarithmicKnobNumPossibleValues.Focus();
          return;
        }
        double startValue;
        try {
          startValue = Convert.ToDouble(this.midiLogarithmicKnobStartValue.Text.Trim());
        } catch (Exception) {
          this.midiLogarithmicKnobStartValue.Text = "";
          this.midiLogarithmicKnobStartValue.Focus();
          return;
        }
        newBinding = new DiscreteLogarithmicKnobMidiBindingConfig() {
          BindingName = newName,
          knobIndex = knobIndex,
          configPropertyName = configPropertyName,
          numPossibleValues = numPossibleValues,
          startValue = startValue,
        };
      } else if (this.midiBindingType.SelectedIndex == 5) {
        int indexRangeStart;
        try {
          indexRangeStart = Convert.ToInt32(this.midiAdsrLevelDriverIndexRangeStart.Text.Trim());
        } catch (Exception) {
          this.midiAdsrLevelDriverIndexRangeStart.Text = "";
          this.midiAdsrLevelDriverIndexRangeStart.Focus();
          return;
        }
        newBinding = new AdsrLevelDriverMidiBindingConfig() {
          BindingName = newName,
          indexRangeStart = indexRangeStart,
        };
      } else {
        return;
      }

      var newMidiPresets = new Dictionary<int, MidiPreset>(this.config.midiPresets);
      var midiPreset = newMidiPresets[this.midiPresetIndices[this.midiPresetList.SelectedIndex]];
      if (editing) {
        midiPreset.Bindings[this.currentlyEditingBinding.Value] = newBinding;
      } else {
        midiPreset.Bindings.Add(newBinding);
      }
      this.config.midiPresets = newMidiPresets;

      if (this.midiBindingType.SelectedIndex == 0) {
        this.midiChangeColorIndexRangeStart.Text = "";
      } else if (this.midiBindingType.SelectedIndex == 1) {
        this.midiTapTempoButtonType.SelectedIndex = -1;
        this.midiTapTempoButtonIndex.Text = "";
      } else if (this.midiBindingType.SelectedIndex == 2) {
        this.midiContinuousKnobIndex.Text = "";
        this.midiContinuousKnobPropertyName.Text = "";
        this.midiContinuousKnobStartValue.Text = "";
        this.midiContinuousKnobEndValue.Text = "";
      } else if (this.midiBindingType.SelectedIndex == 3) {
        this.midiDiscreteKnobIndex.Text = "";
        this.midiDiscreteKnobPropertyName.Text = "";
        this.midiDiscreteKnobNumPossibleValues.Text = "";
      } else if (this.midiBindingType.SelectedIndex == 4) {
        this.midiLogarithmicKnobIndex.Text = "";
        this.midiLogarithmicKnobPropertyName.Text = "";
        this.midiLogarithmicKnobNumPossibleValues.Text = "";
        this.midiLogarithmicKnobStartValue.Text = "";
      } else if (this.midiBindingType.SelectedIndex == 5) {
        this.midiAdsrLevelDriverIndexRangeStart.Text = "";
      }

      ComboBoxItem item = (ComboBoxItem)this.midiBindingType.SelectedItem;
      string bindingTypeName = (string)item.Content;
      if (editing) {
        int bindingIndex = this.currentlyEditingBinding.Value;
        var entry = (MidiBindingEntry)this.midiBindingList.Items[bindingIndex];
        this.midiBindingList.Items[bindingIndex] = new MidiBindingEntry() {
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
      var bindingID = this.midiBindingList.SelectedIndex;
      var newMidiPresets = new Dictionary<int, MidiPreset>(this.config.midiPresets);
      newMidiPresets[presetID].Bindings.RemoveAt(bindingID);
      this.config.midiPresets = newMidiPresets;
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
      this.midiNewBindingName.Text = bindingConfig.BindingName;
      this.midiNewBindingName.Focus();
      this.midiNewBindingName.SelectionStart = this.midiNewBindingName.Text.Length;
      this.midiNewBindingName.SelectionLength = 0;

      this.midiBindingType.SelectedIndex = bindingConfig.BindingType;
      if (bindingConfig.BindingType == 0) {
        var config = (ColorPaletteMidiBindingConfig)bindingConfig;
        this.midiChangeColorIndexRangeStart.Text = config.indexRangeStart.ToString();
      } else if (this.midiBindingType.SelectedIndex == 1) {
        var config = (TapTempoMidiBindingConfig)bindingConfig;
        this.midiTapTempoButtonType.SelectedIndex = indexFromCommandType(config.buttonType);
        this.midiTapTempoButtonIndex.Text = config.buttonIndex.ToString();
      } else if (this.midiBindingType.SelectedIndex == 2) {
        var config = (ContinuousKnobMidiBindingConfig)bindingConfig;
        this.midiContinuousKnobIndex.Text = config.knobIndex.ToString();
        this.midiContinuousKnobPropertyName.Text = config.configPropertyName;
        this.midiContinuousKnobStartValue.Text = config.startValue.ToString();
        this.midiContinuousKnobEndValue.Text = config.endValue.ToString();
      } else if (this.midiBindingType.SelectedIndex == 3) {
        var config = (DiscreteKnobMidiBindingConfig)bindingConfig;
        this.midiDiscreteKnobIndex.Text = config.knobIndex.ToString();
        this.midiDiscreteKnobPropertyName.Text = config.configPropertyName;
        this.midiDiscreteKnobNumPossibleValues.Text = config.numPossibleValues.ToString();
      } else if (this.midiBindingType.SelectedIndex == 4) {
        var config = (DiscreteLogarithmicKnobMidiBindingConfig)bindingConfig;
        this.midiLogarithmicKnobIndex.Text = config.knobIndex.ToString();
        this.midiLogarithmicKnobPropertyName.Text = config.configPropertyName;
        this.midiLogarithmicKnobNumPossibleValues.Text = config.numPossibleValues.ToString();
        this.midiLogarithmicKnobStartValue.Text = config.startValue.ToString();
      } else if (this.midiBindingType.SelectedIndex == 5) {
        var config = (AdsrLevelDriverMidiBindingConfig)bindingConfig;
        this.midiAdsrLevelDriverIndexRangeStart.Text = config.indexRangeStart.ToString();
      }
    }

    private void MidiCancelEditBindingClicked(object sender, RoutedEventArgs e) {
      if (!this.currentlyEditingBinding.HasValue) {
        return;
      }

      this.currentlyEditingBinding = null;
      this.midiBindingEditLabel.Content = "Add binding";
      this.midiAddBinding.Content = "Add binding";
      this.midiCancelEditBinding.Visibility = Visibility.Collapsed;
      this.midiNewBindingName.Text = "";
      this.midiBindingType.SelectedIndex = -1;

      this.midiChangeColorIndexRangeStart.Text = "";

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
    }

    private void MidiChangeColorIndexRangeStartLostFocus(object sender, RoutedEventArgs e) {
      try {
        Convert.ToInt32(this.midiChangeColorIndexRangeStart.Text.Trim());
      } catch (Exception) {
        this.midiChangeColorIndexRangeStart.Text = "";
        return;
      }
    }

    private void MidiContinuousKnobIndexLostFocus(object sender, RoutedEventArgs e) {
      try {
        Convert.ToInt32(this.midiContinuousKnobIndex.Text.Trim());
      } catch (Exception) {
        this.midiContinuousKnobIndex.Text = "";
      }
    }

    private void MidiContinuousKnobPropertyNameLostFocus(object sender, RoutedEventArgs e) {
      var configPropertyName = this.midiContinuousKnobPropertyName.Text;
      if (typeof(Configuration).GetProperty(configPropertyName) == null) {
        this.midiContinuousKnobPropertyName.Text = "";
      }
    }

    private void MidiContinuousKnobStartValueLostFocus(object sender, RoutedEventArgs e) {
      double startNumber;
      try {
        startNumber = Convert.ToDouble(this.midiContinuousKnobStartValue.Text.Trim());
      } catch (Exception) {
        this.midiContinuousKnobStartValue.Text = "";
        return;
      }
      try {
        double endNumber = Convert.ToDouble(this.midiContinuousKnobEndValue.Text.Trim());
        if (endNumber < startNumber) {
          this.midiContinuousKnobEndValue.Text = "";
          this.midiContinuousKnobEndValue.Focus();
        }
      } catch (Exception) {
        this.midiContinuousKnobEndValue.Text = "";
        this.midiContinuousKnobEndValue.Focus();
      }
    }

    private void MidiContinuousKnobEndValueLostFocus(object sender, RoutedEventArgs e) {
      double endNumber;
      try {
        endNumber = Convert.ToDouble(this.midiContinuousKnobEndValue.Text.Trim());
      } catch (Exception) {
        this.midiContinuousKnobEndValue.Text = "";
        return;
      }
      try {
        double startNumber = Convert.ToDouble(this.midiContinuousKnobStartValue.Text.Trim());
        if (endNumber < startNumber) {
          this.midiContinuousKnobStartValue.Text = "";
          this.midiContinuousKnobStartValue.Focus();
        }
      } catch (Exception) {
        this.midiContinuousKnobStartValue.Text = "";
        this.midiContinuousKnobStartValue.Focus();
      }
    }

    private void MidiDiscreteKnobIndexLostFocus(object sender, RoutedEventArgs e) {
      try {
        Convert.ToInt32(this.midiDiscreteKnobIndex.Text.Trim());
      } catch (Exception) {
        this.midiDiscreteKnobIndex.Text = "";
      }
    }

    private void MidiDiscreteKnobPropertyNameLostFocus(object sender, RoutedEventArgs e) {
      var configPropertyName = this.midiDiscreteKnobPropertyName.Text;
      if (typeof(Configuration).GetProperty(configPropertyName) == null) {
        this.midiDiscreteKnobPropertyName.Text = "";
      }
    }

    private void MidiDiscreteKnobNumPossibleValuesLostFocus(object sender, RoutedEventArgs e) {
      try {
        Convert.ToInt32(this.midiDiscreteKnobNumPossibleValues.Text.Trim());
      } catch (Exception) {
        this.midiDiscreteKnobNumPossibleValues.Text = "";
      }
    }

    private void MidiLogarithmicKnobIndexLostFocus(object sender, RoutedEventArgs e) {
      try {
        Convert.ToInt32(this.midiLogarithmicKnobIndex.Text.Trim());
      } catch (Exception) {
        this.midiLogarithmicKnobIndex.Text = "";
      }
    }

    private void MidiLogarithmicKnobPropertyNameLostFocus(object sender, RoutedEventArgs e) {
      var configPropertyName = this.midiLogarithmicKnobPropertyName.Text;
      if (typeof(Configuration).GetProperty(configPropertyName) == null) {
        this.midiLogarithmicKnobPropertyName.Text = "";
      }
    }

    private void MidiLogarithmicKnobNumPossibleValuesLostFocus(object sender, RoutedEventArgs e) {
      try {
        Convert.ToInt32(this.midiLogarithmicKnobNumPossibleValues.Text.Trim());
      } catch (Exception) {
        this.midiLogarithmicKnobNumPossibleValues.Text = "";
      }
    }

    private void MidiLogarithmicKnobStartValueLostFocus(object sender, RoutedEventArgs e) {
      try {
        Convert.ToDouble(this.midiLogarithmicKnobStartValue.Text.Trim());
      } catch (Exception) {
        this.midiLogarithmicKnobStartValue.Text = "";
      }
    }

    private void MidiAdsrLevelDriverIndexRangeStartLostFocus(object sender, RoutedEventArgs e) {
      try {
        Convert.ToInt32(this.midiAdsrLevelDriverIndexRangeStart.Text.Trim());
      } catch (Exception) {
        this.midiAdsrLevelDriverIndexRangeStart.Text = "";
        return;
      }
    }

    private void OpenVJHUD(object sender, RoutedEventArgs e) {
      this.vjHUDWindow = new VJHUDWindow(this.config);
      this.vjHUDWindow.Closed += VJHUDClosed;
      this.vjHUDWindow.Show();
    }

    private void CloseVJHUD(object sender, RoutedEventArgs e) {
      this.vjHUDWindow.Close();
      this.vjHUDWindow = null;
    }

    private void VJHUDClosed(object sender, EventArgs e) {
      this.config.vjHUDEnabled = false;
    }

    private void OpenDomeSimulator(object sender, RoutedEventArgs e) {
      this.domeSimulatorWindow = new DomeSimulatorWindow(this.config);
      this.domeSimulatorWindow.Closed += DomeSimulatorClosed;
      this.domeSimulatorWindow.Show();
    }

    private void CloseDomeSimulator(object sender, RoutedEventArgs e) {
      this.domeSimulatorWindow.Close();
      this.domeSimulatorWindow = null;
    }

    private void DomeSimulatorClosed(object sender, EventArgs e) {
      this.config.domeSimulationEnabled = false;
    }

    private void OpenDomeMapping(object sender, RoutedEventArgs e) {
      if (this.domeMappingWindow != null) {
        this.domeMappingWindow.Activate();
        return;
      }
      this.domeMappingWindow = new DomeMappingWindow(this.config);
      this.domeMappingWindow.Closed += DomeMappingClosed;
      this.domeMappingWindow.Show();
    }

    private void DomeMappingClosed(object sender, EventArgs e) {
      this.domeMappingWindow = null;
    }

    private void InitWandSerialUI() {
      this.RepopulateWandSerialPorts();
      // The receiver's status fields are computed from stored timestamps at
      // snapshot time, so polling on a UI timer is enough to keep the indicator
      // fresh without any push machinery.
      this.wandReceiverStatusTimer = new System.Windows.Threading.DispatcherTimer {
        Interval = TimeSpan.FromMilliseconds(500),
      };
      this.wandReceiverStatusTimer.Tick += this.UpdateWandReceiverStatus;
      this.wandReceiverStatusTimer.Start();
    }

    // Rebuilds the port combo: (none) + live ports, plus the configured value as
    // a "(missing)" item if it isn't present (so an unplugged receiver's saved
    // port is never dropped and stays preselected). Guarded so it never writes
    // config back. Preselect matches on item Value, not the display label.
    private void RepopulateWandSerialPorts() {
      this.repopulatingWandPorts = true;
      try {
        var selector = this.wandSerialPortSelector;
        selector.Items.Clear();
        selector.Items.Add(new PortItem { Display = "(none)", Value = "" });

        string configured = this.config.wandSerialPort ?? "";
        bool configuredPresent = false;
        foreach (var port in WandSerialReceiver.AvailablePorts()) {
          selector.Items.Add(new PortItem { Display = port, Value = port });
          if (port == configured) {
            configuredPresent = true;
          }
        }
        if (!string.IsNullOrEmpty(configured) && !configuredPresent) {
          selector.Items.Add(new PortItem {
            Display = configured + " (missing)",
            Value = configured,
          });
        }

        foreach (PortItem item in selector.Items) {
          if (item.Value == configured) {
            selector.SelectedItem = item;
            break;
          }
        }
      } finally {
        this.repopulatingWandPorts = false;
      }
    }

    private void WandSerialPortDropDownOpened(object sender, EventArgs e) {
      this.RepopulateWandSerialPorts();
    }

    private void WandSerialPortSelectionChanged(
      object sender, SelectionChangedEventArgs e
    ) {
      // Only a genuine user pick writes config; programmatic repopulate is
      // guarded. The value ("" only when the user picks (none)) is the real
      // port, never the display label.
      if (this.repopulatingWandPorts) {
        return;
      }
      if (this.wandSerialPortSelector.SelectedItem is PortItem item) {
        this.config.wandSerialPort = item.Value;
      }
    }

    private void UpdateWandReceiverStatus(object sender, EventArgs e) {
      var status = this.op.OrientationInput.WandSerial.StatusSnapshot();
      string text;
      Brush color;
      if (string.IsNullOrEmpty(this.config.wandSerialPort)) {
        text = "No port selected";
        color = Brushes.Gray;
      } else if (status.LastError != null) {
        text = "Error: " + status.LastError;
        color = Brushes.OrangeRed;
      } else if (!status.PortOpen) {
        text = "Opening…";
        color = Brushes.Gray;
      } else {
        double since = Math.Min(
          status.MillisSinceLastHeartbeat, status.MillisSinceLastFrame);
        if (since < WandSerialReceiver.RECEIVER_ALIVE_MS) {
          text = "Receiver connected (" +
            (since / 1000.0).ToString("0.0") + " s ago)";
          color = Brushes.ForestGreen;
        } else {
          text = "Port open — no data";
          color = Brushes.OrangeRed;
        }
      }
      this.wandReceiverStatus.Text = text;
      this.wandReceiverStatus.Foreground = color;
    }

    private void OpenWandStatus(object sender, RoutedEventArgs e) {
      if (this.wandStatusWindow != null) {
        this.wandStatusWindow.Activate();
        return;
      }
      this.wandStatusWindow = new WandStatusWindow(
        this.config,
        this.op.OrientationInput
      );
      this.wandStatusWindow.Closed += WandStatusClosed;
      this.wandStatusWindow.Show();
    }

    private void WandStatusClosed(object sender, EventArgs e) {
      this.wandStatusWindow = null;
    }

  }

}
