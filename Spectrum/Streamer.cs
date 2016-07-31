using System;
using System.Windows;
using System.Windows.Controls;
using System.Threading;
using Spectrum.Base;
using Spectrum.Audio;

namespace Spectrum {

  public class Streamer {

    private Configuration config;
    private AudioInput audio;
    // Reference to the visualizer that receives the processed signal
    public Visualizer visualizer;
    // Reference to the ComboBox containing possible audio input devices
    private ComboBox devicelist;

    // If initialized, the input and visualization threads are running
    private bool initialized = false;
    // Handle for the audio input thread
    private Thread audioProcessingThread;
    // Handle for the light updating thread
    private Thread lightUpdatingThread;

    public Streamer(ComboBox devicelist, Configuration config) {
      this.devicelist = devicelist;
      this.config = config;
      this.audio = new AudioInput(config);

      this.PopulateDeviceList();

      this.visualizer = new Visualizer(config);
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
      this.audio.Enabled = true;

      this.devicelist.IsEnabled = false;
      this.visualizer.Start();
      this.audioProcessingThread = new Thread(AudioProcessingThread);
      this.audioProcessingThread.Start();
      this.lightUpdatingThread = new Thread(LightProcessingThread);
      this.lightUpdatingThread.Start();
    }

    private void AudioProcessingThread() {
      while (true) {
        if (
          !this.config.controlLights ||
          this.config.lightsOff ||
          this.config.redAlert
        ) {
          continue;
        }
        this.visualizer.process(this.audio.AudioData, this.audio.Volume);
      }
    }

    private void LightProcessingThread() {
      while (true) {
        this.visualizer.updateHues();
        // Hue API limits 10/s light changes
        Thread.Sleep(100);
      }
    }

    public void CleanUp() {
      this.visualizer.CleanUp();
      this.audio.Enabled = false;
      if (this.audioProcessingThread != null) {
        this.audioProcessingThread.Abort();
        this.audioProcessingThread.Join();
      }
      if (this.lightUpdatingThread != null) {
        this.lightUpdatingThread.Abort();
        this.lightUpdatingThread.Join();
      }
      this.devicelist.IsEnabled = true;
    }
  }

}
