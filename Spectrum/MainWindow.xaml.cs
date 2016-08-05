using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Spectrum.Audio;
using System.Xml.Serialization;
using System.IO;
using Spectrum.MIDI;

namespace Spectrum {

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

      this.hueEnabled.IsChecked = this.config.huesEnabled;
      this.ledBoardEnabled.IsChecked = this.config.ledBoardEnabled;
      this.midiEnabled.IsChecked = this.config.midiInputEnabled;
      this.audioThreadCheckbox.IsChecked =
        this.config.audioInputInSeparateThread;
      this.hueThreadCheckbox.IsChecked = this.config.huesOutputInSeparateThread;
      this.ledBoardThreadCheckbox.IsChecked =
        this.config.ledBoardOutputInSeparateThread;
      this.midiThreadCheckbox.IsChecked = this.config.midiInputInSeparateThread;
      this.hueCommandDelay.Text = this.config.hueDelay.ToString();
      this.hueIdleOnSilent.IsChecked = this.config.hueIdleOnSilent;

      if (!this.config.controlLights) {
        if (
          this.config.brighten == 0 &&
          this.config.colorslide == 0 &&
          this.config.sat == 0
        ) {
          this.hueOverride.SelectedItem = this.hueOverrideCustom;
        } else {
          this.hueOverride.SelectedItem = this.hueOverrideOn;
        }
      } else if (this.config.lightsOff) {
        this.hueOverride.SelectedItem = this.hueOverrideOff;
      } else if (this.config.redAlert) {
        this.hueOverride.SelectedItem = this.hueOverrideRed;
      }
      this.hueCustomBrightness.Text = this.config.brighten.ToString();
      this.hueCustomSaturation.Text = this.config.sat.ToString();
      this.hueCustomHue.Text = this.config.colorslide.ToString();

      this.peakChangeS.Value = this.config.peakC;
      this.peakChangeL.Content = this.config.peakC.ToString("F3");
      this.dropQuietS.Value = this.config.dropQ;
      this.dropQuietL.Content = this.config.dropQ.ToString("F3");
      this.dropChangeS.Value = this.config.dropT;
      this.dropChangeL.Content = this.config.dropT.ToString("F3");
      this.kickQuietS.Value = this.config.kickQ;
      this.kickQuietL.Content = this.config.kickQ.ToString("F3");
      this.kickChangeS.Value = this.config.kickT;
      this.kickChangeL.Content = this.config.kickT.ToString("F3");
      this.snareQuietS.Value = this.config.snareQ;
      this.snareQuietL.Content = this.config.snareQ.ToString("F3");
      this.snareChangeS.Value = this.config.snareT;
      this.snareChangeL.Content = this.config.snareT.ToString("F3");

      this.hueHubAddress.Text = this.config.hueURL;
      this.hueLightIndices.Text = String.Join(",", this.config.hueIndices);
      this.ledBoardRowLength.Text = this.config.teensyRowLength.ToString();
      this.ledBoardRowsPerStrip.Text =
        this.config.teensyRowsPerStrip.ToString();
      this.ledBoardBrightnessSlider.Value = this.config.ledBoardBrightness;
      this.ledBoardBrightnessLabel.Content =
        this.config.ledBoardBrightness.ToString("F3");

      this.loadingConfig = false;
    }

    private void SliderStarted(object sender, DragStartedEventArgs e) {
      this.loadingConfig = true;
    }

    private void SliderCompleted(object sender, DragCompletedEventArgs e) {
      this.loadingConfig = false;
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

    private void AudioSeparateThreadChanged(object sender, RoutedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.audioInputInSeparateThread =
        this.audioThreadCheckbox.IsChecked == true;
      this.op.Reboot();
      this.SaveConfig();
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

    private void HueEnabled(object sender, RoutedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.huesEnabled = this.hueEnabled.IsChecked == true;
      this.SaveConfig();
    }

    private void HueHubAddress(object sender, TextChangedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.hueURL = this.hueHubAddress.Text;
      this.SaveConfig();
    }

    private void HueLightIndices(object sender, TextChangedEventArgs e) {
      if (this.config == null) {
        return;
      }
      try {
        this.config.hueIndices = Array.ConvertAll(
          this.hueLightIndices.Text.Split(','),
          int.Parse
        );
        this.SaveConfig();
      } catch (FormatException) { }
    }

    private void HueCommandDelay(object sender, TextChangedEventArgs e) {
      if (this.config == null) {
        return;
      }
      try {
        this.config.hueDelay = Convert.ToInt32(this.hueCommandDelay.Text);
        this.SaveConfig();
      } catch (FormatException) { }
    }

    private void HueAudioSliderChanged(
      object sender,
      RoutedPropertyChangedEventArgs<double> e
    ) {
      if (this.config == null) {
        return;
      }
      Slider slider = (Slider)sender;
      if (slider.Name == "peakChangeS") {
        this.config.peakC = slider.Value;
        this.peakChangeL.Content = this.config.peakC.ToString("F3");
      } else if (slider.Name == "dropQuietS") {
        this.config.dropQ = slider.Value;
        this.dropQuietL.Content = this.config.dropQ.ToString("F3");
      } else if (slider.Name == "dropChangeS") {
        this.config.dropT = slider.Value;
        this.dropChangeL.Content = this.config.dropT.ToString("F3");
      } else if (slider.Name == "kickQuietS") {
        this.config.kickQ = slider.Value;
        this.kickQuietL.Content = this.config.kickQ.ToString("F3");
      } else if (slider.Name == "kickChangeS") {
        this.config.kickT = slider.Value;
        this.kickChangeL.Content = this.config.kickT.ToString("F3");
      } else if (slider.Name == "snareQuietS") {
        this.config.snareQ = slider.Value;
        this.snareQuietL.Content = this.config.snareQ.ToString("F3");
      } else if (slider.Name == "snareChangeS") {
        this.config.snareT = slider.Value;
        this.snareChangeL.Content = this.config.snareT.ToString("F3");
      }
      this.SaveConfig();
    }

    private void HueOverrideDisabled(object sender, RoutedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.controlLights = true;
      this.config.lightsOff = false;
      this.config.redAlert = false;
      this.hueCustomGrid.Visibility = Visibility.Collapsed;
      this.hueIdleOnSilent.Visibility = Visibility.Visible;
      this.SaveConfig();
    }

    private void HueOverrideOn(object sender, RoutedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.controlLights = false;
      this.config.lightsOff = false;
      this.config.redAlert = false;
      this.hueCustomBrightness.Text = "0";
      this.hueCustomHue.Text = "0";
      this.hueCustomSaturation.Text = "0";
      this.hueCustomGrid.Visibility = Visibility.Collapsed;
      this.hueIdleOnSilent.Visibility = Visibility.Collapsed;
      this.SaveConfig();
    }

    private void HueOverrideOff(object sender, RoutedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.controlLights = true;
      this.config.lightsOff = true;
      this.config.redAlert = false;
      this.hueCustomGrid.Visibility = Visibility.Collapsed;
      this.hueIdleOnSilent.Visibility = Visibility.Collapsed;
      this.SaveConfig();
    }

    private void HueOverrideRed(object sender, RoutedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.controlLights = true;
      this.config.lightsOff = false;
      this.config.redAlert = true;
      this.hueCustomGrid.Visibility = Visibility.Collapsed;
      this.hueIdleOnSilent.Visibility = Visibility.Collapsed;
      this.SaveConfig();
    }

    private void HueOverrideCustom(object sender, RoutedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.controlLights = false;
      this.config.lightsOff = false;
      this.config.redAlert = false;
      this.hueCustomGrid.Visibility = Visibility.Visible;
      this.hueIdleOnSilent.Visibility = Visibility.Collapsed;
      this.SaveConfig();
    }

    private void HueCustomBrightness(object sender, TextChangedEventArgs e) {
      if (this.config == null) {
        return;
      }
      try {
        this.config.brighten = Convert.ToInt32(this.hueCustomBrightness.Text);
        this.SaveConfig();
      } catch (FormatException) { }
    }

    private void HueCustomSaturation(object sender, TextChangedEventArgs e) {
      if (this.config == null) {
        return;
      }
      try {
        this.config.sat = Convert.ToInt32(this.hueCustomBrightness.Text);
        this.SaveConfig();
      } catch (FormatException) { }
    }

    private void HueCustomHue(object sender, TextChangedEventArgs e) {
      if (this.config == null) {
        return;
      }
      try {
        this.config.colorslide = Convert.ToInt32(this.hueCustomBrightness.Text);
        this.SaveConfig();
      } catch (FormatException) { }
    }

    private void HueIdleWhileSilent(object sender, RoutedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.hueIdleOnSilent = this.hueIdleOnSilent.IsChecked == true;
      this.SaveConfig();
    }

    private void HueSeparateThreadChanged(object sender, RoutedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.huesOutputInSeparateThread =
        this.hueThreadCheckbox.IsChecked == true;
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

    private void LEDBoardEnabled(object sender, RoutedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.ledBoardEnabled = this.ledBoardEnabled.IsChecked == true;
      this.SaveConfig();
    }

    private void LEDBoardRowLength(object sender, TextChangedEventArgs e) {
      if (this.config == null) {
        return;
      }
      try {
        this.config.teensyRowLength =
          Convert.ToInt32(this.ledBoardRowLength.Text);
        this.SaveConfig();
      } catch (FormatException) { }
    }

    private void LEDBoardRowsPerStrip(object sender, TextChangedEventArgs e) {
      if (this.config == null) {
        return;
      }
      try {
        this.config.teensyRowsPerStrip =
          Convert.ToInt32(this.ledBoardRowsPerStrip.Text);
        this.SaveConfig();
      } catch (FormatException) { }
    }

    private void LEDBoardBrightnessChanged(
      object sender,
      RoutedPropertyChangedEventArgs<double> e
    ) {
      if (this.config == null) {
        return;
      }
      this.config.ledBoardBrightness = this.ledBoardBrightnessSlider.Value;
      this.ledBoardBrightnessLabel.Content =
        this.config.ledBoardBrightness.ToString("F3");
      this.SaveConfig();
    }

    private void LEDBoardSeparateThreadChanged(object sender, RoutedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.ledBoardOutputInSeparateThread =
        this.ledBoardThreadCheckbox.IsChecked == true;
      this.op.Reboot();
      this.SaveConfig();
    }

    private void MidiEnabled(object sender, RoutedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.midiInputEnabled = this.midiEnabled.IsChecked == true;
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

    private void MidiSeparateThreadChanged(object sender, RoutedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.midiInputInSeparateThread =
        this.midiThreadCheckbox.IsChecked == true;
      this.op.Reboot();
      this.SaveConfig();
    }

  }

}
