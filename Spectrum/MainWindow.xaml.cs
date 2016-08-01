﻿using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Spectrum.Audio;

namespace Spectrum {

  public partial class MainWindow : Window {

    Operator st;
    SpectrumConfiguration config;
    private bool dragStarted = true;
    private bool boxInitialized = false;

    public MainWindow() {
      InitializeComponent();
      this.config = new SpectrumConfiguration();
      this.st = new Operator(this.config);

      this.boxInitialized = true;
      HotKey white_toggle = new HotKey(Key.Q, KeyModifier.Alt, this.OnHotKeyHandler);
      HotKey off_toggle = new HotKey(Key.OemTilde, KeyModifier.Alt, this.OnHotKeyHandler);
      HotKey red_alert = new HotKey(Key.R, KeyModifier.Alt, this.OnHotKeyHandler);
      HotKey bri_up = new HotKey(Key.OemPeriod, KeyModifier.Alt, this.OnHotKeyHandler);
      HotKey bri_down = new HotKey(Key.OemComma, KeyModifier.Alt, this.OnHotKeyHandler);
      HotKey hue_left = new HotKey(Key.Left, KeyModifier.Alt, this.OnHotKeyHandler);
      HotKey hue_right = new HotKey(Key.Right, KeyModifier.Alt, this.OnHotKeyHandler);
      HotKey sat_up = new HotKey(Key.Up, KeyModifier.Alt, this.OnHotKeyHandler);
      HotKey sat_down = new HotKey(Key.Down, KeyModifier.Alt, this.OnHotKeyHandler);
      this.Closing += this.HandleClose;

      for (int i = 0; i < AudioInput.DeviceCount; i++) {
        if (AudioInput.IsEnabledLoopbackDevice(i)) {
          this.devices.Items.Add(string.Format(
            "{0} - {1}",
            i,
            AudioInput.GetDeviceName(i)
          ));
        }
      }
      this.devices.SelectedIndex = 0;
      this.InputDeviceChanged(null, null);
      this.devices.SelectionChanged += this.InputDeviceChanged;
    }

    private void OnHotKeyHandler(HotKey hotKey) {
      if (hotKey.Key.Equals(Key.Q)) {
        checkBox.IsChecked = !checkBox.IsChecked;
        config.brighten = 0;
        config.colorslide = 0;
        config.sat = 0;
      }
      if (hotKey.Key.Equals(Key.OemTilde)) {
        config.lightsOff = !config.lightsOff;
        System.Diagnostics.Debug.WriteLine("off");
      }
      if (hotKey.Key.Equals(Key.R)) {
        config.redAlert = !config.redAlert;
        System.Diagnostics.Debug.WriteLine("red");
      }
      if (hotKey.Key.Equals(Key.OemPeriod)) {
        config.brighten = Math.Min(config.brighten + 1, 0);
      }
      if (hotKey.Key.Equals(Key.OemComma)) {
        config.brighten = Math.Max(config.brighten - 1, -4);
      }
      if (hotKey.Key.Equals(Key.Left)) {
        config.colorslide -= 1;
      }
      if (hotKey.Key.Equals(Key.Right)) {
        config.colorslide += 1;
      }
      //config.colorslide = (config.colorslide + 4 + 16) % 16 - 4;??
      if (hotKey.Key.Equals(Key.Up)) {
        config.sat = Math.Min(config.sat + 1, 2);
      }
      if (hotKey.Key.Equals(Key.Down)) {
        config.sat = Math.Max(config.sat - 1, -2);
      }
    }

    private void InputDeviceChanged(
      object sender,
      SelectionChangedEventArgs e
    ) {
      var str = (this.devices.Items[this.devices.SelectedIndex] as string);
      var deviceName = str.Split(' ');
      this.config.audioDeviceIndex = Convert.ToInt32(deviceName[0]);
    }

    private void button_Click(object sender, RoutedEventArgs e) {
      config.controlLights = (bool)checkBox.IsChecked;
      dropQuietS.IsEnabled = !dropQuietS.IsEnabled;
      dropChangeS.IsEnabled = !dropChangeS.IsEnabled;
      kickQuietS.IsEnabled = !kickQuietS.IsEnabled;
      kickChangeS.IsEnabled = !kickChangeS.IsEnabled;
      snareQuietS.IsEnabled = !snareQuietS.IsEnabled;
      snareChangeS.IsEnabled = !snareChangeS.IsEnabled;
      peakChangeS.IsEnabled = !peakChangeS.IsEnabled;
      st.Enabled = !st.Enabled;
      this.devices.IsEnabled = !st.Enabled;
      dragStarted = false;
    }

    private void Slider_DragStarted(object sender, DragStartedEventArgs e) {
      dragStarted = true;
    }
    private void Slider_DragCompleted(object sender, DragCompletedEventArgs e) {
      set(((Slider)sender).Name, ((Slider)sender).Value);
      dragStarted = false;
    }
    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
      if (!dragStarted)
        set(((Slider)sender).Name, e.NewValue);
    }
    private void HandleCheck(object sender, RoutedEventArgs e) {
      if (boxInitialized)
        config.controlLights = true;
    }

    private void HandleUnchecked(object sender, RoutedEventArgs e) {
      config.controlLights = false;
    }

    private void set(String name, double val) {
      if (name == "dropQuietS") {
        dropQuietL.Content = val.ToString("F3");
        config.dropQ = (float)val;
      }
      if (name == "dropChangeS") {
        dropChangeL.Content = val.ToString("F3");
        config.dropT = (float)val;
      }
      if (name == "kickQuietS") {
        kickQuietL.Content = val.ToString("F3");
        config.kickQ = (float)val;
      }
      if (name == "kickChangeS") {
        kickChangeL.Content = val.ToString("F3");
        config.kickT = (float)val;
      }
      if (name == "snareQuietS") {
        snareQuietL.Content = val.ToString("F3");
        config.snareQ = (float)val;
      }
      if (name == "snareChangeS") {
        snareChangeL.Content = val.ToString("F3");
        config.snareT = (float)val;
      }
      if (name == "peakChangeS") {
        peakChangeL.Content = val.ToString("F3");
        config.peakC = (float)val;
      }
    }

    private void HandleClose(object sender, EventArgs e) {
      this.st.Enabled = false;
    }
  }
}
