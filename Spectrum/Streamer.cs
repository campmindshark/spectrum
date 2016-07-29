using System;
using System.Windows;
using System.Windows.Controls;
using System.Threading;
using Un4seen.Bass;
using Un4seen.BassWasapi;
using Spectrum.Base;

namespace Spectrum {

  public class Streamer {

    private Configuration config;
    // Reference to the visualizer that receives the processed signal
    public Visualizer visualizer;
    // Reference to the ComboBox containing possible audio input devices
    private ComboBox devicelist;
    // Index identifying the audio input device
    private int devindex;

    // If initialized, the input and visualization threads are running
    private bool initialized = false;
    // Handle for the audio input thread
    private Thread audioProcessingThread;
    // Handle for the light updating thread
    private Thread lightUpdatingThread;

    // Needed for memory management reasons. More details in MagicIncantations()
    private WASAPIPROC process;

    public Streamer(ComboBox devicelist, Configuration config) {
      this.devicelist = devicelist;
      this.config = config;

      this.MagicIncantations();
      this.PopulateDeviceList();

      this.visualizer = new Visualizer(config);
    }

    /**
     * Strange incantations required to make the Un4seen libraries work are
     * quarantined here.
     */
    private void MagicIncantations() {
      BassNet.Registration("larry.fenn@gmail.com", "2X531420152222");
      // This is being initialized here because we need to pass this variable
      // into some scary old C-style API and it doesn't get refcounted there.
      // We declare the variable as a member to avoid garbage collection.
      process = new WASAPIPROC(Process);
      // A common usage pattern of the Un4seen libraries involves setting up an
      // auto-updating HSTREAM object and passing it around to various functions
      // that will extract data off of it. To accomplish the auto-updating
      // behavior, the libraries set up a background thread by default. We
      // disable that functionality here because we bypass all that by using
      // BASS_WASAPI_GetData instead of BASS_ChannelGetData. We give no fucks.
      Bass.BASS_SetConfig(BASSConfig.BASS_CONFIG_UPDATETHREADS, 0);
      // So we set up "double buffering" below with the BASS_WASAPI_BUFFER flag.
      // Apparently "double buffering" requires that we set up this "no sound"
      // device first. Ashoat has no idea why we need "double buffering", but
      // surmises it might be a requirement for the BASS_WASAPI_GetLevel call?
      // Larry probably knows.
      bool result = Bass.BASS_Init(
        0,
        44100,
        BASSInit.BASS_DEVICE_DEFAULT,
        IntPtr.Zero
      );
      if (!result) {
        throw new Exception(
          "Failed to set up \"no sound\" device for \"double buffering\""
        );
      }
    }

    private void PopulateDeviceList() {
      this.devicelist.Items.Clear();
      for (int i = 0; i < BassWasapi.BASS_WASAPI_GetDeviceCount(); i++) {
        var device = BassWasapi.BASS_WASAPI_GetDeviceInfo(i);
        if (device.IsEnabled && device.IsLoopback) {
          this.devicelist.Items.Add(string.Format("{0} - {1}", i, device.name));
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
      this.devindex = Convert.ToInt32(deviceName[0]);
      bool result = BassWasapi.BASS_WASAPI_Init(
        devindex,
        0,
        0,
        BASSWASAPIInit.BASS_WASAPI_BUFFER,
        1f,
        0,
        process,
        IntPtr.Zero
      );
      if (!result) {
        MessageBox.Show(Bass.BASS_ErrorGetCode().ToString());
        this.initialized = false;
        return;
      }
      this.devicelist.IsEnabled = false;
      this.visualizer.init(this.config.controlLights);
      this.audioProcessingThread = new Thread(AudioProcessingThread);
      this.audioProcessingThread.Start();
      this.lightUpdatingThread = new Thread(LightProcessingThread);
      this.lightUpdatingThread.Start();
    }

    private void AudioProcessingThread() {
      BassWasapi.BASS_WASAPI_Start();
      float[] fft = new float[8192];
      while (true) {
        if (
          !this.config.controlLights ||
          this.config.lightsOff ||
          this.config.redAlert
        ) {
          continue;
        }
        // get fft data. Return value is -1 on error
        // type: 1/8192 of the channel sample rate (here, 44100 hz; so the bin size is roughly 2.69 Hz)
        BassWasapi.BASS_WASAPI_GetData(fft, (int)BASSData.BASS_DATA_FFT16384);
        this.visualizer.process(
          fft,
          BassWasapi.BASS_WASAPI_GetDeviceLevel(devindex, -1)
        );
      }
    }

    private void LightProcessingThread() {
      while (true) {
        this.visualizer.updateHues();
        // Hue API limits 10/s light changes
        Thread.Sleep(100);
      }
    }

    // WASAPI callback, required for continuous recording
    private int Process(IntPtr buffer, int length, IntPtr user) {
      return 1;
    }

    public void CleanUp() {
      this.visualizer.CleanUp();
      if (this.audioProcessingThread != null) {
        this.audioProcessingThread.Abort();
        this.audioProcessingThread.Join();
      }
      if (this.lightUpdatingThread != null) {
        this.lightUpdatingThread.Abort();
        this.lightUpdatingThread.Join();
      }
      BassWasapi.BASS_WASAPI_Free();
      Bass.BASS_Free();
      this.devicelist.IsEnabled = true;
    }
  }

}
