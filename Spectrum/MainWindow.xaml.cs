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

namespace Spectrum {

  class StringJoinConverter : IValueConverter {

    public object Convert(
      object value,
      Type targetType,
      object parameter,
      System.Globalization.CultureInfo culture
    ) {
      try {
        return String.Join(",", (int[])value);
      } catch (FormatException) {
        // Failing to convert will trip a validation rule
        return value;
      }
    }

    public object ConvertBack(
      object value,
      Type targetType,
      object parameter,
      System.Globalization.CultureInfo culture
    ) {
      try {
        return Array.ConvertAll(((string)value).Split(','), int.Parse);
      } catch (FormatException) {
        // Failing to convert will trip a validation rule
        return value;
      }
    }

  }

  public partial class MainWindow : Window {

    Operator op;
    SpectrumConfiguration config;
    private int[] audioDeviceIndices;
    private int[] midiDeviceIndices;

    private bool loadingConfig = false;

    public MainWindow() {
      InitializeComponent();

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

      this.Bind("huesEnabled", this.hueEnabled, CheckBox.IsCheckedProperty);
      this.Bind("ledBoardEnabled", this.ledBoardEnabled, CheckBox.IsCheckedProperty);
      this.Bind("midiInputEnabled", this.midiEnabled, CheckBox.IsCheckedProperty);
      this.Bind("audioInputInSeparateThread", this.audioThreadCheckbox, CheckBox.IsCheckedProperty, true);
      this.Bind("huesOutputInSeparateThread", this.hueThreadCheckbox, CheckBox.IsCheckedProperty, true);
      this.Bind("ledBoardOutputInSeparateThread", this.ledBoardThreadCheckbox, CheckBox.IsCheckedProperty, true);
      this.Bind("midiInputInSeparateThread", this.midiThreadCheckbox, CheckBox.IsCheckedProperty, true);
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

      this.loadingConfig = false;
    }

    private void Bind(
      string configPath,
      FrameworkElement element,
      DependencyProperty property,
      bool rebootOnUpdate = false,
      BindingMode mode = BindingMode.TwoWay,
      IValueConverter converter = null
    ) {
      var binding = new Binding(configPath);
      binding.Source = this.config;
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

      this.audioDeviceIndices = new int[AudioInput.DeviceCount];

      this.audioDevices.Items.Clear();
      int itemIndex = 0;
      for (int i = 0; i < AudioInput.DeviceCount; i++) {
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

  }

}