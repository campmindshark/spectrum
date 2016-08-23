using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Spectrum.Base;
using Un4seen.Bass;
using Un4seen.BassWasapi;
using Un4seen.Bass.Misc;
using System.Diagnostics;
using Un4seen.Bass.AddOn.Mix;

namespace Spectrum.Audio {

  /**
   * There are two ways to use AudioInput. In one way, AudioInput will
   * initialize its own thread, and will automatically update the AudioData and
   * Volume properties. Your thread will check them as desired. In the other way
   * of using AudioInput, you will have to manually poll the UpdateAudio method
   * in order to update AudioData and Volume.
   *
   * Choose between them with config.audioInputInSeparateThread. Note that
   * either way, you'll need to set the Active property to true in order to get
   * updates. Setting it to false will disable any running threads.
   */
   public class AudioInput : Input {

    private Configuration config;

    // Needed for memory management reasons. More details in MagicIncantations()
    private WASAPIPROC process;
    private STREAMPROC StreamPoc;

    // These values get continuously updated by the internal thread
    public float[] AudioData { get; private set; } = new float[8192];
    public float Volume { get; private set; } = 0.0f;
    private Timer analysisTimer;
    private Stopwatch stopwatch = new Stopwatch();
    private int handle;
    private int outstr;
    private int timeInterval = 20;
    private BPMCounter bpmCounter;
        private WASAPIPROC outProcess;
    public double BPM { get; private set; } = 0.0;

    public AudioInput(Configuration config) {
      this.config = config;

      BassNet.Registration("larry.fenn@gmail.com", "2X531420152222");

      // This is being initialized here because we need to pass this variable
      // into some scary old C-style API and it doesn't get refcounted there.
      // We declare the variable as a member to avoid garbage collection.
      this.process = new WASAPIPROC(this.NoOp);
      this.StreamPoc = this.streamproc;
            outProcess = new WASAPIPROC(outWasapiProc);
    }

    /**
     * Strange incantations required to make the Un4seen libraries work are
     * quarantined here.
     */
    private void MagicIncantations() {
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

    private bool active;
    private Thread inputThread;
    public bool Active {
      get {
        lock (this.process) {
          return this.active;
        }
      }
      set {
        lock (this.process) {
          if (this.active == value) {
            return;
          }
          if (value) {
            if (this.config.audioInputInSeparateThread) {
              this.inputThread = new Thread(AudioProcessingThread);
              this.inputThread.Start();
            } else {
              this.InitializeAudio();
            }
          } else {
            if (this.inputThread != null) {
              this.inputThread.Abort();
              this.inputThread.Join();
              this.inputThread = null;
            } else {
              this.TerminateAudio();
            }
          }
          this.active = value;
        }
      }
    }

    public bool AlwaysActive {
      get {
        return false;
      }
    }

    public bool Enabled {
      get {
        return true;
      }
    }

    /**
     * The Un4seen libraries need some function to call.
     */
    private int NoOp(IntPtr buffer, int length, IntPtr user) {
      return 1;
    }

    private void InitializeAudio() {
      this.MagicIncantations();
      if (this.config.audioDeviceIndex == -1) {
        throw new Exception("audioDeviceIndex not set!");
      }
      bool result = BassWasapi.BASS_WASAPI_Init(
        this.config.audioDeviceIndex,
        44100,
        0,
        BASSWASAPIInit.BASS_WASAPI_BUFFER,
        1,
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
      BassWasapi.BASS_WASAPI_Start();

      handle = Bass.BASS_StreamCreate(44100, 2, BASSFlag.BASS_MUSIC_DECODE | BASSFlag.BASS_MUSIC_FLOAT, this.StreamPoc, IntPtr.Zero);
      Bass.BASS_ChannelPlay(handle, false);
      bpmCounter = new BPMCounter(timeInterval, 44100);
      bpmCounter.BPMHistorySize = 50;
      analysisTimer = new Timer(timerCallback, null, 0, timeInterval);
            BassWasapi.BASS_WASAPI_Init(12, 44100, 0, 0, 0, 0, outProcess, IntPtr.Zero);
            BassWasapi.BASS_WASAPI_SetDevice(12);
            outstr = BassMix.BASS_Mixer_StreamCreate(44100, 2, BASSFlag.BASS_STREAM_DECODE | BASSFlag.BASS_SAMPLE_FLOAT);
            BassMix.BASS_Mixer_StreamAddChannel(outstr, handle, 0);
            BassWasapi.BASS_WASAPI_Start();
        }

    private void timerCallback(object o) {
      if (bpmCounter.ProcessAudio(handle, true)) { 
        //System.Diagnostics.Debug.WriteLine(stopwatch.ElapsedMilliseconds);
        stopwatch.Restart();
      }
    }

    private void TerminateAudio() {
      BassWasapi.BASS_WASAPI_Free();
      Bass.BASS_Free();
    }

    private void Update() {
      lock (this.process) {
        // get fft data. Return value is -1 on error
        // type: 1/8192 of the channel sample rate
        // (here, 44100 hz; so the bin size is roughly 2.69 Hz)
        float[] tempAudioData = new float[8192];
        BassWasapi.BASS_WASAPI_GetData(
          tempAudioData,
          (int)BASSData.BASS_DATA_FFT16384
        );
        float tempVolume = BassWasapi.BASS_WASAPI_GetDeviceLevel(
          this.config.audioDeviceIndex,
          -1
        );
        this.AudioData = tempAudioData;
        this.Volume = tempVolume;
        this.BPM = bpmCounter.BPM;
        //System.Diagnostics.Debug.WriteLine(this.BPM);
      }
    }

    private void AudioProcessingThread() {
      this.InitializeAudio();
      try {
        while (true) {
          this.Update();
        }
      } catch (ThreadAbortException) {
        this.TerminateAudio();
      }
    }

    public void OperatorUpdate() {
      if (!this.config.audioInputInSeparateThread) {
        this.Update();
      }
    }

    public static int DeviceCount {
      get {
        return BassWasapi.BASS_WASAPI_GetDeviceCount();
      }
    }

    public static bool IsEnabledInputDevice(int deviceIndex) {
      var device = BassWasapi.BASS_WASAPI_GetDeviceInfo(deviceIndex);
      return device.IsEnabled && device.IsInput;
    }

    public static string GetDeviceName(int deviceIndex) {
      var device = BassWasapi.BASS_WASAPI_GetDeviceInfo(deviceIndex);
      return device.name;
    }
    private int streamproc(int handle, IntPtr buffer, int length, IntPtr user) {
      BassWasapi.BASS_WASAPI_GetData(buffer, length | (int)BASSData.BASS_DATA_FLOAT);
      return length;
    }
        private int outWasapiProc(IntPtr buffer, int length, IntPtr user)
        {
            Debug.WriteLine(length);
            return Bass.BASS_ChannelGetData(outstr, buffer, length);
        }
    }
}