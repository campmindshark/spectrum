using System;
using System.Windows;
using System.Windows.Controls;
using System.Threading;
using Spectrum.Base;
using Spectrum.Audio;
using Spectrum.Hues;
using Spectrum.LEDs;

namespace Spectrum {

  public class Streamer {

    private Configuration config;
    private AudioInput audio;
    private HueOutput hue;
    private CartesianTeensyOutput teensy;
    // Reference to the visualizer that receives the processed signal
    public Visualizer visualizer;
    // Reference to the ComboBox containing possible audio input devices
    private ComboBox devicelist;

    // If initialized, the input and visualization threads are running
    private bool initialized = false;

    public Streamer(ComboBox devicelist, Configuration config) {
      this.devicelist = devicelist;

      this.config = config;
      this.audio = new AudioInput(config);
      this.hue = new HueOutput(config);
      this.teensy = new CartesianTeensyOutput(config);
      this.visualizer = new Visualizer(
        config,
        this.audio,
        this.hue,
        this.teensy
      );

      this.PopulateDeviceList();

    }

    private void PopulateDeviceList() {
      this.devicelist.Items.Clear();
      for (int i = 0; i < AudioInput.DeviceCount; i++) {
        if (AudioInput.IsEnabledLoopbackDevice(i)) {
          this.devicelist.Items.Add(string.Format(
            "{0} - {1}",
            i,
            AudioInput.GetDeviceName(i)
          ));
        }
      }
      this.devicelist.SelectedIndex = 0;
    }

    public void ToggleState() {
      this.initialized = !this.initialized;
      if (!this.initialized) {
        this.CleanUp();
        return;
      }

      var str = (this.devicelist.Items[devicelist.SelectedIndex] as string);
      var deviceName = str.Split(' ');
      this.audio.DeviceIndex = Convert.ToInt32(deviceName[0]);

      this.devicelist.IsEnabled = false;

      this.audio.Enabled = true;
      this.hue.Enabled = true;
      this.teensy.Enabled = true;
      this.visualizer.Enabled = true;
    }

    public void CleanUp() {
      this.visualizer.Enabled = false;
      this.teensy.Enabled = false;
      this.hue.Enabled = false;
      this.audio.Enabled = false;

      this.devicelist.IsEnabled = true;
    }
  }

}
