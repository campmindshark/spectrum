using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Spectrum.Audio;
using System.Xml.Serialization;
using System.IO;
using Spectrum.MIDI;
using System.Windows.Data;
using Xceed.Wpf.Toolkit;

namespace Spectrum {

  public partial class MainWindow : Window {

    private Operator op;
    private SpectrumConfiguration config;
    private bool loadingConfig = false;
    private int[] audioDeviceIndices;
    private int[] midiDeviceIndices;
    private DomeSimulatorWindow domeSimulatorWindow;

    public MainWindow() {
      this.InitializeComponent();

      new HotKey(Key.Q, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.OemTilde, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.R, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.OemPeriod, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.OemComma, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.Left, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.Right, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.Up, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.Down, KeyModifier.Alt, this.OnHotKeyHandler);

      this.LoadConfig();
    }

    private void HandleClose(object sender, EventArgs e) {
      this.op.Enabled = false;
      this.SaveConfig();
    }

    private void SaveConfig() {
      if (this.loadingConfig) {
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
        new XmlSerializer(typeof(SpectrumConfiguration)).Serialize(
          stream,
          this.config
        );
      }
    }

    private void UpdateConfig(
      object sender,
      DataTransferEventArgs eventArgs
    ) {
      if (this.config != null) {
        this.SaveConfig();
      }
    }

    private void UpdateConfigAndReboot(
      object sender,
      DataTransferEventArgs eventArgs
    ) {
      if (this.config != null) {
        this.op.Reboot();
        this.SaveConfig();
      }
    }

    private void LoadConfig() {
      this.loadingConfig = true;

      if (File.Exists("spectrum_config.xml")) {
        using (FileStream stream = File.OpenRead("spectrum_config.xml")) {
          this.config = new XmlSerializer(
            typeof(SpectrumConfiguration)
          ).Deserialize(stream) as SpectrumConfiguration;
        }
      }
      if (this.config == null) {
        this.config = new SpectrumConfiguration();
      }
      this.op = new Operator(this.config);

      this.RefreshAudioDevices(null, null);
      this.RefreshLEDBoardPorts(null, null);
      this.RefreshMidiDevices(null, null);
      this.RefreshDomePorts(null, null);

      this.Bind("huesEnabled", this.hueEnabled, CheckBox.IsCheckedProperty);
      this.Bind("ledBoardEnabled", this.ledBoardEnabled, CheckBox.IsCheckedProperty);
      this.Bind("midiInputEnabled", this.midiEnabled, CheckBox.IsCheckedProperty);
      this.Bind("audioInputInSeparateThread", this.audioThreadCheckbox, CheckBox.IsCheckedProperty, true);
      this.Bind("huesOutputInSeparateThread", this.hueThreadCheckbox, CheckBox.IsCheckedProperty, true);
      this.Bind("ledBoardOutputInSeparateThread", this.ledBoardThreadCheckbox, CheckBox.IsCheckedProperty, true);
      this.Bind("midiInputInSeparateThread", this.midiThreadCheckbox, CheckBox.IsCheckedProperty, true);
      this.Bind("domeOutputInSeparateThread", this.domeThreadCheckbox, CheckBox.IsCheckedProperty, true);
      this.Bind("operatorFPS", this.operatorFPSLabel, Label.ContentProperty);
      this.Bind("operatorFPS", this.operatorFPSLabel, Label.ForegroundProperty, false, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("hueDelay", this.hueCommandDelay, TextBox.TextProperty);
      this.Bind("hueIdleOnSilent", this.hueIdleOnSilent, CheckBox.IsCheckedProperty);
      this.Bind("hueOverrideIndex", this.hueOverride, ComboBox.SelectedIndexProperty);
      this.Bind("hueOverrideIsCustom", this.hueCustomGrid, Grid.VisibilityProperty, false, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("hueOverrideIsDisabled", this.hueIdleOnSilent, Grid.VisibilityProperty, false, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("brighten", this.hueCustomBrightness, TextBox.TextProperty);
      this.Bind("sat", this.hueCustomSaturation, TextBox.TextProperty);
      this.Bind("colorslide", this.hueCustomHue, TextBox.TextProperty);
      this.Bind("peakC", this.peakChangeS, Slider.ValueProperty);
      this.Bind("peakC", this.peakChangeL, Label.ContentProperty);
      this.Bind("dropQ", this.dropQuietS, Slider.ValueProperty);
      this.Bind("dropQ", this.dropQuietL, Label.ContentProperty);
      this.Bind("dropT", this.dropChangeS, Slider.ValueProperty);
      this.Bind("dropT", this.dropChangeL, Label.ContentProperty);
      this.Bind("kickQ", this.kickQuietS, Slider.ValueProperty);
      this.Bind("kickQ", this.kickQuietL, Label.ContentProperty);
      this.Bind("kickT", this.kickChangeS, Slider.ValueProperty);
      this.Bind("kickT", this.kickChangeL, Label.ContentProperty);
      this.Bind("snareQ", this.snareQuietS, Slider.ValueProperty);
      this.Bind("snareQ", this.snareQuietL, Label.ContentProperty);
      this.Bind("snareT", this.snareChangeS, Slider.ValueProperty);
      this.Bind("snareT", this.snareChangeL, Label.ContentProperty);
      this.Bind("hueURL", this.hueHubAddress, TextBox.TextProperty);
      this.Bind("hueIndices", this.hueLightIndices, TextBox.TextProperty, false, BindingMode.TwoWay, new StringJoinConverter());
      this.Bind("teensyRowLength", this.ledBoardRowLength, TextBox.TextProperty);
      this.Bind("teensyRowsPerStrip", this.ledBoardRowsPerStrip, TextBox.TextProperty);
      this.Bind("ledBoardBrightness", this.ledBoardBrightnessSlider, Slider.ValueProperty);
      this.Bind("ledBoardBrightness", this.ledBoardBrightnessLabel, Label.ContentProperty);
      this.Bind("domeEnabled", this.domeEnabled, CheckBox.IsCheckedProperty);
      this.Bind("domeSimulationEnabled", this.domeSimulationEnabled, CheckBox.IsCheckedProperty);
      this.Bind("domeMaxBrightness", this.domeMaxBrightnessSlider, Slider.ValueProperty);
      this.Bind("domeMaxBrightness", this.domeMaxBrightnessLabel, Label.ContentProperty);
      this.Bind("domeBrightness", this.domeBrightnessSlider, Slider.ValueProperty);
      this.Bind("domeBrightness", this.domeBrightnessLabel, Label.ContentProperty);
      this.Bind("domeVolumeAnimationSize", this.domeVolumeAnimationSize, ComboBox.SelectedIndexProperty);
      this.Bind("domeAutoFlashDelay", this.domeAutoFlashDelay, TextBox.TextProperty);
      this.Bind("domeSkipLEDs", this.domeSkipLEDs, TextBox.TextProperty);
      var colorConverter = new ColorConverter();
      this.Bind("[0,0]", this.domeColor0_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[0,1]", this.domeColor0_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[0]", this.domeCC0, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.domeColorPalette.computerEnabledColors);
      this.Bind("[1,0]", this.domeColor1_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[1,1]", this.domeColor1_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[1]", this.domeCC1, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.domeColorPalette.computerEnabledColors);
      this.Bind("[2,0]", this.domeColor2_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[2,1]", this.domeColor2_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[2]", this.domeCC2, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.domeColorPalette.computerEnabledColors);
      this.Bind("[3,0]", this.domeColor3_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[3,1]", this.domeColor3_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[3]", this.domeCC3, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.domeColorPalette.computerEnabledColors);
      this.Bind("[4,0]", this.domeColor4_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[4,1]", this.domeColor4_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[4]", this.domeCC4, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.domeColorPalette.computerEnabledColors);
      this.Bind("[5,0]", this.domeColor5_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[5,1]", this.domeColor5_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[5]", this.domeCC5, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.domeColorPalette.computerEnabledColors);
      this.Bind("[6,0]", this.domeColor6_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[6,1]", this.domeColor6_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[6]", this.domeCC6, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.domeColorPalette.computerEnabledColors);
      this.Bind("[7,0]", this.domeColor7_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[7,1]", this.domeColor7_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[7]", this.domeCC7, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.domeColorPalette.computerEnabledColors);
      this.Bind("[8,0]", this.domeColor8_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[8,1]", this.domeColor8_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[8]", this.domeCC8, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.domeColorPalette.computerEnabledColors);
      this.Bind("[9,0]", this.domeColor9_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[9,1]", this.domeColor9_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[9]", this.domeCC9, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.domeColorPalette.computerEnabledColors);
      this.Bind("[10,0]", this.domeColor10_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[10,1]", this.domeColor10_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[10]", this.domeCC10, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.domeColorPalette.computerEnabledColors);
      this.Bind("[11,0]", this.domeColor11_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[11,1]", this.domeColor11_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[11]", this.domeCC11, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.domeColorPalette.computerEnabledColors);
      this.Bind("[12,0]", this.domeColor12_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[12,1]", this.domeColor12_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[12]", this.domeCC12, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.domeColorPalette.computerEnabledColors);
      this.Bind("[13,0]", this.domeColor13_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[13,1]", this.domeColor13_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[13]", this.domeCC13, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.domeColorPalette.computerEnabledColors);
      this.Bind("[14,0]", this.domeColor14_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[14,1]", this.domeColor14_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[14]", this.domeCC14, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.domeColorPalette.computerEnabledColors);
      this.Bind("[15,0]", this.domeColor15_0, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[15,1]", this.domeColor15_1, ColorPicker.SelectedColorProperty, false, BindingMode.TwoWay, colorConverter, this.config.domeColorPalette);
      this.Bind("[15]", this.domeCC15, CheckBox.IsCheckedProperty, false, BindingMode.TwoWay, null, this.config.domeColorPalette.computerEnabledColors);
      this.Bind("whyFireEnabled", this.whyFireEnabled, CheckBox.IsCheckedProperty);
      this.Bind("whyFireOutputInSeparateThread", this.whyFireThreadCheckbox, CheckBox.IsCheckedProperty, true);
      this.Bind("whyFireURL", this.whyFireAddress, TextBox.TextProperty);

      this.loadingConfig = false;
    }

    private void Bind(
      string configPath,
      FrameworkElement element,
      DependencyProperty property,
      bool rebootOnUpdate = false,
      BindingMode mode = BindingMode.TwoWay,
      IValueConverter converter = null,
      object source = null
    ) {
      var binding = new Binding(configPath);
      binding.Source = source != null ? source : this.config;
      binding.Mode = mode;
      if (converter != null) {
        binding.Converter = converter;
      }
      binding.NotifyOnSourceUpdated = true;
      if (rebootOnUpdate) {
        Binding.AddSourceUpdatedHandler(element, UpdateConfigAndReboot);
      } else {
        Binding.AddSourceUpdatedHandler(element, UpdateConfig);
      }
      element.SetBinding(property, binding);
    }

    private void SliderStarted(object sender, DragStartedEventArgs e) {
      this.loadingConfig = true;
    }

    private void SliderCompleted(object sender, DragCompletedEventArgs e) {
      this.loadingConfig = false;
      this.SaveConfig();
    }

    private void OnHotKeyHandler(HotKey hotKey) {
      if (hotKey.Key.Equals(Key.Q)) {
        this.hueOverride.SelectedItem =
          this.hueOverride.SelectedItem == this.hueOverrideOn
            ? this.hueOverrideDisable
            : this.hueOverrideOn;
      } else if (hotKey.Key.Equals(Key.OemTilde)) {
        this.hueOverride.SelectedItem =
          this.hueOverride.SelectedItem == this.hueOverrideOff
            ? this.hueOverrideDisable
            : this.hueOverrideOff;
      } else if (hotKey.Key.Equals(Key.R)) {
        this.hueOverride.SelectedItem =
          this.hueOverride.SelectedItem == this.hueOverrideRed
            ? this.hueOverrideDisable
            : this.hueOverrideRed;
      } else if (hotKey.Key.Equals(Key.OemPeriod)) {
        this.hueCustomBrightness.Text =
          Math.Min(this.config.brighten + 1, 0).ToString();
      } else if (hotKey.Key.Equals(Key.OemComma)) {
        this.hueCustomBrightness.Text =
          Math.Max(this.config.brighten - 1, -4).ToString();
      } else if (hotKey.Key.Equals(Key.Left)) {
        this.hueCustomHue.Text = (this.config.colorslide - 1).ToString();
      } else if (hotKey.Key.Equals(Key.Right)) {
        this.hueCustomHue.Text = (this.config.colorslide + 1).ToString();
      } else if (hotKey.Key.Equals(Key.Up)) {
        this.hueCustomSaturation.Text =
          Math.Min(this.config.sat + 1, 2).ToString();
      } else if (hotKey.Key.Equals(Key.Down)) {
        this.hueCustomSaturation.Text =
          Math.Max(this.config.sat - 1, -2).ToString();
      }
      //config.colorslide = (config.colorslide + 4 + 16) % 16 - 4;??
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

      int deviceCount = AudioInput.DeviceCount;
      this.audioDeviceIndices = new int[deviceCount];

      this.audioDevices.Items.Clear();
      int itemIndex = 0;
      for (int i = 0; i < deviceCount; i++) {
        if (!AudioInput.IsEnabledInputDevice(i)) {
          continue;
        }
        this.audioDevices.Items.Add(AudioInput.GetDeviceName(i));
        this.audioDeviceIndices[itemIndex++] = i;
      }

      this.audioDevices.SelectedIndex = Array.FindIndex(
        this.audioDeviceIndices,
        i => i == this.config.audioDeviceIndex
      );
    }

    private void AudioInputDeviceChanged(
      object sender,
      SelectionChangedEventArgs e
    ) {
      if (this.audioDevices.SelectedIndex == -1) {
        return;
      }
      this.config.audioDeviceIndex =
        this.audioDeviceIndices[this.audioDevices.SelectedIndex];
      this.op.Reboot();
      this.SaveConfig();
    }

    private void RefreshLEDBoardPorts(object sender, RoutedEventArgs e) {
      this.ledBoardEnabled.IsChecked = false;

      this.ledBoardUSBPorts.Items.Clear();
      foreach (string portName in System.IO.Ports.SerialPort.GetPortNames()) {
        this.ledBoardUSBPorts.Items.Add(portName);
      }

      this.ledBoardUSBPorts.SelectedValue = this.config.teensyUSBPort;
    }

    private void LEDBoardUSBPortsChanged(
      object sender,
      SelectionChangedEventArgs e
    ) {
      if (this.ledBoardUSBPorts.SelectedIndex == -1) {
        return;
      }
      this.config.teensyUSBPort = this.ledBoardUSBPorts.SelectedItem as string;
      this.op.Reboot();
      this.SaveConfig();
    }

    private void RefreshMidiDevices(object sender, RoutedEventArgs e) {
      this.midiEnabled.IsChecked = false;

      this.midiDeviceIndices = new int[MidiInput.DeviceCount];

      this.midiDevices.Items.Clear();
      for (int i = 0; i < MidiInput.DeviceCount; i++) {
        this.midiDevices.Items.Add(MidiInput.GetDeviceName(i));
        this.midiDeviceIndices[i] = i;
      }

      this.midiDevices.SelectedIndex = Array.FindIndex(
        this.midiDeviceIndices,
        i => i == this.config.midiDeviceIndex
      );
    }

    private void MidiDeviceChanged(
      object sender,
      SelectionChangedEventArgs e
    ) {
      if (this.midiDevices.SelectedIndex == -1) {
        return;
      }
      this.config.midiDeviceIndex =
        this.midiDeviceIndices[this.midiDevices.SelectedIndex];
      this.op.Reboot();
      this.SaveConfig();
    }

    private void RefreshDomePorts(object sender, RoutedEventArgs e) {
      this.domeEnabled.IsChecked = false;

      this.domeTeensy1.Items.Clear();
      this.domeTeensy2.Items.Clear();
      this.domeTeensy3.Items.Clear();
      this.domeTeensy4.Items.Clear();
      this.domeTeensy5.Items.Clear();
      foreach (string portName in System.IO.Ports.SerialPort.GetPortNames()) {
        this.domeTeensy1.Items.Add(portName);
        this.domeTeensy2.Items.Add(portName);
        this.domeTeensy3.Items.Add(portName);
        this.domeTeensy4.Items.Add(portName);
        this.domeTeensy5.Items.Add(portName);
      }

      this.domeTeensy1.SelectedValue = this.config.domeTeensyUSBPort1;
      this.domeTeensy2.SelectedValue = this.config.domeTeensyUSBPort2;
      this.domeTeensy3.SelectedValue = this.config.domeTeensyUSBPort3;
      this.domeTeensy4.SelectedValue = this.config.domeTeensyUSBPort4;
      this.domeTeensy5.SelectedValue = this.config.domeTeensyUSBPort5;
    }

    private void DomePortChanged(
      object sender,
      SelectionChangedEventArgs e
    ) {
      if (this.domeTeensy1.SelectedIndex != -1) {
        this.config.domeTeensyUSBPort1 = this.domeTeensy1.SelectedItem as string;
      }
      if (this.domeTeensy2.SelectedIndex != -1) {
        this.config.domeTeensyUSBPort2 = this.domeTeensy2.SelectedItem as string;
      }
      if (this.domeTeensy3.SelectedIndex != -1) {
        this.config.domeTeensyUSBPort3 = this.domeTeensy3.SelectedItem as string;
      }
      if (this.domeTeensy4.SelectedIndex != -1) {
        this.config.domeTeensyUSBPort4 = this.domeTeensy4.SelectedItem as string;
      }
      if (this.domeTeensy5.SelectedIndex != -1) {
        this.config.domeTeensyUSBPort5 = this.domeTeensy5.SelectedItem as string;
      }
      this.op.Reboot();
      this.SaveConfig();
    }

    private void OpenDomeSimulator(object sender, RoutedEventArgs e) {
      this.domeSimulatorWindow = new DomeSimulatorWindow(this.config);
      this.domeSimulatorWindow.Closed += DomeSimulatorClosed;
      this.domeSimulatorWindow.Show();
    }

    private void DomeSimulatorClosed(object sender, EventArgs e) {
      this.config.domeSimulationEnabled = false;
    }

    private void CloseDomeSimulator(object sender, RoutedEventArgs e) {
      this.domeSimulatorWindow.Close();
      this.domeSimulatorWindow = null;
    }

  }

}