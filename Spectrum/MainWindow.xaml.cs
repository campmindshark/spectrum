using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Spectrum {
  public partial class MainWindow : Window {
    Streamer st;
    private bool dragStarted = true;
    private bool boxInitialized = false;
    public MainWindow() {
      InitializeComponent();
      st = new Streamer(devices);
      boxInitialized = true;
      HotKey white_toggle = new HotKey(Key.Q, KeyModifier.Alt, OnHotKeyHandler);
      HotKey off_toggle = new HotKey(Key.OemTilde, KeyModifier.Alt, OnHotKeyHandler);
      HotKey red_alert = new HotKey(Key.R, KeyModifier.Alt, OnHotKeyHandler);
      HotKey bri_up = new HotKey(Key.OemPeriod, KeyModifier.Alt, OnHotKeyHandler);
      HotKey bri_down = new HotKey(Key.OemComma, KeyModifier.Alt, OnHotKeyHandler);
      HotKey hue_left = new HotKey(Key.Left, KeyModifier.Alt, OnHotKeyHandler);
      HotKey hue_right = new HotKey(Key.Right, KeyModifier.Alt, OnHotKeyHandler);
      HotKey sat_up = new HotKey(Key.Up, KeyModifier.Alt, OnHotKeyHandler);
      HotKey sat_down = new HotKey(Key.Down, KeyModifier.Alt, OnHotKeyHandler);
    }

    private void OnHotKeyHandler(HotKey hotKey) {
      if (hotKey.Key.Equals(Key.Q)) {
        checkBox.IsChecked = !checkBox.IsChecked;
        st.brighten = 0;
        st.colorslide = 0;
        st.sat = 0;
      }
      if (hotKey.Key.Equals(Key.OemTilde)) {
        st.lightsOff = !st.lightsOff;
      }
      if (hotKey.Key.Equals(Key.R)) {
        st.redAlert = !st.redAlert;
      }
      if (hotKey.Key.Equals(Key.OemPeriod)) {
        st.brighten = Math.Min(st.brighten + 1, 0);
      }
      if (hotKey.Key.Equals(Key.OemComma)) {
        st.brighten = Math.Max(st.brighten - 1, -4);
      }
      if (hotKey.Key.Equals(Key.Left)) {
        st.colorslide -= 1;
      }
      if (hotKey.Key.Equals(Key.Right)) {
        st.colorslide += 1;
      }
      st.colorslide = (st.colorslide + 4 + 16) % 16 - 4;
      if (hotKey.Key.Equals(Key.Up)) {
        st.sat = Math.Min(st.sat + 1, 2);
      }
      if (hotKey.Key.Equals(Key.Down)) {
        st.sat = Math.Max(st.sat - 1, -2);
      }
      st.forceUpdate();
    }

    private void button_Click(object sender, RoutedEventArgs e) {
      st.controlLights = (bool)checkBox.IsChecked;
      dropQuietS.IsEnabled = !dropQuietS.IsEnabled;
      dropChangeS.IsEnabled = !dropChangeS.IsEnabled;
      kickQuietS.IsEnabled = !kickQuietS.IsEnabled;
      kickChangeS.IsEnabled = !kickChangeS.IsEnabled;
      snareQuietS.IsEnabled = !snareQuietS.IsEnabled;
      snareChangeS.IsEnabled = !snareChangeS.IsEnabled;
      peakChangeS.IsEnabled = !peakChangeS.IsEnabled;
      st.ToggleState();
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
        set("controlLights", 1);
    }

    private void HandleUnchecked(object sender, RoutedEventArgs e) {
      set("controlLights", 0);
    }
    private void set(String name, double val) {
      st.updateConstants(name, (float)val);
      if (name == "dropQuietS") {
        dropQuietL.Content = val.ToString("F3");
      }
      if (name == "dropChangeS") {
        dropChangeL.Content = val.ToString("F3");
      }
      if (name == "kickQuietS") {
        kickQuietL.Content = val.ToString("F3");
      }
      if (name == "kickChangeS") {
        kickChangeL.Content = val.ToString("F3");
      }
      if (name == "snareQuietS") {
        snareQuietL.Content = val.ToString("F3");
      }
      if (name == "snareChangeS") {
        snareChangeL.Content = val.ToString("F3");
      }
      if (name == "peakChangeS") {
        peakChangeL.Content = val.ToString("F3");
      }
      st.forceUpdate();
    }
  }
}
