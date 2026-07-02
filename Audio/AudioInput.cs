using System;
using System.Collections.Generic;
using Spectrum.Base;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Spectrum.Audio {

  public class AudioInput : Input {

    private int audioFormatSampleFrequency = 44100;

    private readonly Configuration config;

    private MMDevice recordingDevice;
    private WasapiCapture captureStream;

    public float Volume { get; private set; } = 0.0f;

    private readonly MadmomHandler madmomHandler;
    private readonly ProDjLinkHandler proDjLinkHandler;

    public AudioInput(Configuration config) {
      this.config = config;
      this.madmomHandler = new MadmomHandler(config, this);
      this.proDjLinkHandler = new ProDjLinkHandler(config);
    }

    private bool active;
    public bool Active {
      get {
        return this.active;
      }
      set {
        if (this.active == value) {
          return;
        }
        if (value) {
          this.InitializeAudio();
        } else {
          this.TerminateAudio();
        }
        this.active = value;
        this.madmomHandler.Active = value;
        this.proDjLinkHandler.Active = value;
      }
    }

    public bool AlwaysActive {
      get {
        return true;
      }
    }

    public bool Enabled {
      get {
        return true;
      }
    }

    private void InitializeAudio() {
      if (this.config.audioDeviceID == null) {
        throw new Exception("audioDeviceID not set!");
      }
      MMDevice device = null;
      using (var enumerator = new MMDeviceEnumerator()) {
        var iterator = enumerator.EnumerateAudioEndPoints(
          DataFlow.Capture,
          DeviceState.Active
        );
        foreach (var audioDevice in iterator) {
          if (this.config.audioDeviceID == audioDevice.ID) {
            device = audioDevice;
            break;
          }
        }
      }
      if (device == null) {
        throw new Exception(
          "Configured audioDeviceID not found among active capture devices!"
        );
      }
      this.recordingDevice = device;
      var bitrate = device.AudioClient.MixFormat.BitsPerSample;
      this.captureStream = new WasapiCapture(device, false, bitrate);
      audioFormatSampleFrequency = device.AudioClient.MixFormat.SampleRate;
      this.captureStream.WaveFormat = new WaveFormat(
        audioFormatSampleFrequency,
        bitrate,
        device.AudioClient.MixFormat.Channels
      );
      this.captureStream.DataAvailable += Update;
      this.captureStream.StartRecording();
    }

    private void TerminateAudio() {
      if (this.captureStream != null) {
        // Detach the handler before stopping: StopRecording can raise a final
        // DataAvailable, and we don't want it running against a device we're
        // about to dispose/null.
        this.captureStream.DataAvailable -= Update;
        this.captureStream.StopRecording();
        this.captureStream.Dispose();
        this.captureStream = null;
      }
      if (this.recordingDevice != null) {
        this.recordingDevice.Dispose();
        this.recordingDevice = null;
      }
      // Don't leave a stale peak reading behind once capture has stopped.
      this.Volume = 0.0f;
    }

    private void Update(object sender, NAudio.Wave.WaveInEventArgs args) {
      // The capture thread can still deliver a buffer as TerminateAudio nulls
      // recordingDevice; read it into a local and bail if it's already gone.
      var device = this.recordingDevice;
      if (device == null) {
        return;
      }
      this.Volume = device.AudioMeterInformation.MasterPeakValue;
    }

    public void OperatorUpdate() {
    }

    public static List<AudioDevice> AudioDevices {
      get {
        var audioDeviceList = new List<AudioDevice>();
        var iterator = new MMDeviceEnumerator().EnumerateAudioEndPoints(
          // We avoid filtering in the call here so we can get the right device
          // index, which we need in our call to madmom. madmom uses PyAudio,
          // which in turn uses PortAudio, which doesn't support the use of the
          // "Endpoint ID string" (audioDevice.ID) to identify an audio device.
          DataFlow.All,
          DeviceState.Active
        );
        int i = 0;
        foreach (var audioDevice in iterator) {
          int index = i;
          i++;
          if (audioDevice.DataFlow != DataFlow.Capture) {
            continue;
          }
          audioDeviceList.Add(new AudioDevice() {
            id = audioDevice.ID,
            name = audioDevice.FriendlyName,
            index = index,
          });
        }
        return audioDeviceList;
      }
    }

    public int CurrentAudioDeviceIndex {
      get {
        if (this.config.audioDeviceID == null) {
          return -1;
        }
        foreach (var audioDevice in AudioInput.AudioDevices) {
          if (audioDevice.id == this.config.audioDeviceID) {
            return audioDevice.index;
          }
        }
        return -1;
      }
    }
  }
}
