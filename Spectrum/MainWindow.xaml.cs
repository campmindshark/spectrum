using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Spectrum.Audio;

namespace Spectrum {

  public partial class MainWindow : Window {

    Operator op;
    SpectrumConfiguration config;
    private int[] audioDeviceIndices;

    public MainWindow() {
      InitializeComponent();
      this.config = new SpectrumConfiguration();
      this.op = new Operator(this.config);

      new HotKey(Key.Q, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.OemTilde, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.R, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.OemPeriod, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.OemComma, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.Left, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.Right, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.Up, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.Down, KeyModifier.Alt, this.OnHotKeyHandler);

      this.RefreshAudioDevices(null, null);

      this.hueHubAddress.Text = this.config.hueURL;
      this.hueLightIndices.Text = String.Join(",", this.config.hueIndices);
      this.hueCommandDelay.Text = this.config.hueDelay.ToString();
    }

    private void HandleClose(object sender, EventArgs e) {
      this.op.Enabled = false;
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
      this.audioDevices.SelectedIndex = 0;
      this.AudioInputDeviceChanged(null, null);
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
      if (this.op.Enabled) {
        this.op.Enabled = false;
        this.op.Enabled = true;
      }
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
    }

    private void HueHubAddress(object sender, TextChangedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.hueURL = this.hueHubAddress.Text;
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
      } catch (FormatException) { }
    }

    private void HueCommandDelay(object sender, TextChangedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.hueDelay = Convert.ToInt32(this.hueCommandDelay.Text);
    }

    private void HueAudioSliderChanged(
      object sender,
      RoutedPropertyChangedEventArgs<double> e
    ) {
      if (this.config == null) {
        return;
      }
      Slider slider = (Slider)sender;
      if (slider.Name == "dropQuietS") {
        this.dropQuietL.Content = slider.Value.ToString("F3");
        this.config.dropQ = (float)slider.Value;
      } else if (slider.Name == "dropChangeS") {
        this.dropChangeL.Content = slider.Value.ToString("F3");
        this.config.dropT = (float)slider.Value;
      } else if (slider.Name == "kickQuietS") {
        this.kickQuietL.Content = slider.Value.ToString("F3");
        this.config.kickQ = (float)slider.Value;
      } else if (slider.Name == "kickChangeS") {
        this.kickChangeL.Content = slider.Value.ToString("F3");
        this.config.kickT = (float)slider.Value;
      } else if (slider.Name == "snareQuietS") {
        this.snareQuietL.Content = slider.Value.ToString("F3");
        this.config.snareQ = (float)slider.Value;
      } else if (slider.Name == "snareChangeS") {
        this.snareChangeL.Content = slider.Value.ToString("F3");
        this.config.snareT = (float)slider.Value;
      } else if (slider.Name == "peakChangeS") {
        this.peakChangeL.Content = slider.Value.ToString("F3");
        this.config.peakC = (float)slider.Value;
      }
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
    }

    private void HueCustomBrightness(object sender, TextChangedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.brighten = Convert.ToInt32(this.hueCustomBrightness.Text);
    }

    private void HueCustomSaturation(object sender, TextChangedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.sat = Convert.ToInt32(this.hueCustomBrightness.Text);
    }

    private void HueCustomHue(object sender, TextChangedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.colorslide = Convert.ToInt32(this.hueCustomBrightness.Text);
    }

    private void HueIdleWhileSilent(object sender, RoutedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.hueIdleOnSilent = this.hueIdleOnSilent.IsChecked == true;
    }

    private void HueSeparateThreadChanged(object sender, RoutedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.huesOutputInSeparateThread =
        this.hueThreadCheckbox.IsChecked == true;
      this.op.Reboot();
    }

    private void LEDBoardEnabled(object sender, RoutedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.ledBoardEnabled = this.ledBoardEnabled.IsChecked == true;
    }

    private void LEDBoardSeparateThreadChanged(object sender, RoutedEventArgs e) {
      if (this.config == null) {
        return;
      }
      this.config.ledBoardOutputInSeparateThread =
        this.ledBoardThreadCheckbox.IsChecked == true;
      this.op.Reboot();
    }

  }

}
