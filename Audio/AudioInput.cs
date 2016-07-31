using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Spectrum.Base;
using Un4seen.Bass;
using Un4seen.BassWasapi;

namespace Spectrum.Audio {

  public class AudioInput : Input {

    private Configuration config;

    // Needed for memory management reasons. More details in MagicIncantations()
    private WASAPIPROC process;

    // These values get continuously updated by the internal thread
    public float[] AudioData { get; private set; } = new float[8192];
    public float Volume { get; private set; } = 0.0f;

    public AudioInput(Configuration config) {
      this.config = config;
      this.MagicIncantations();
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
      this.process = new WASAPIPROC(NoOp);
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

    private bool enabled;
    private Thread inputThread;
    public bool Enabled {
      get {
        lock (this.process) {
          return this.enabled;
        }
      }
      set {
        lock (this.process) {
          if (this.enabled == value) {
            return;
          }
          if (value) {
            if (this.deviceIndex == -1) {
              throw new Exception("DeviceIndex not set!");
            }
            bool result = BassWasapi.BASS_WASAPI_Init(
              this.deviceIndex,
              0,
              0,
              BASSWASAPIInit.BASS_WASAPI_BUFFER,
              1f,
              0,
              this.process,
              IntPtr.Zero
            );
            if (!result) {
              throw new Exception(
                "Couldn't initialize BassWasapi: " +
                  Bass.BASS_ErrorGetCode().ToString()
              );
            }
            this.inputThread = new Thread(AudioProcessingThread);
            this.inputThread.Start();
          } else {
            this.inputThread.Abort();
            this.inputThread.Join();
            BassWasapi.BASS_WASAPI_Free();
            Bass.BASS_Free();
          }
          this.enabled = value;
        }
      }
    }

    private int deviceIndex = -1;
    public int DeviceIndex {
      get {
        lock (this.process) {
          return this.deviceIndex;
        }
      }
      set {
        lock (this.process) {
          bool wasEnabled = this.enabled;
          if (wasEnabled) {
            this.enabled = false;
          }
          this.deviceIndex = value;
          if (wasEnabled) {
            this.enabled = true;
          }
        }
      }
    }

    /**
     * The Un4seen libraries need some function to call.
     */
    private int NoOp(IntPtr buffer, int length, IntPtr user) {
      return 1;
    }

    private void AudioProcessingThread() {
      BassWasapi.BASS_WASAPI_Start();
      float[] tempAudioData = new float[8192];
      while (true) {
        if (
          !this.config.controlLights ||
          this.config.lightsOff ||
          this.config.redAlert
        ) {
          continue;
        }
        // get fft data. Return value is -1 on error
        // type: 1/8192 of the channel sample rate
        // (here, 44100 hz; so the bin size is roughly 2.69 Hz)
        BassWasapi.BASS_WASAPI_GetData(
          tempAudioData,
          (int)BASSData.BASS_DATA_FFT16384
        );
        this.AudioData = tempAudioData;
        this.Volume = BassWasapi.BASS_WASAPI_GetDeviceLevel(
          this.deviceIndex,
          -1
        );
      }
    }

    public static int DeviceCount {
      get {
        return BassWasapi.BASS_WASAPI_GetDeviceCount();
      }
    }

    public static bool IsEnabledLoopbackDevice(int deviceIndex) {
      var device = BassWasapi.BASS_WASAPI_GetDeviceInfo(deviceIndex);
      return device.IsEnabled && device.IsLoopback;
    }

    public static string GetDeviceName(int deviceIndex) {
      var device = BassWasapi.BASS_WASAPI_GetDeviceInfo(deviceIndex);
      return device.name;
    }

  }

}