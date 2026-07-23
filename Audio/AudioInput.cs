using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Spectrum.Base;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Spectrum.Audio {

  public class AudioInput : IAudioLevelInput, IAudioDeviceProvider {

    private int audioFormatSampleFrequency = 44100;

    private readonly Configuration config;
    private readonly IRuntimeSettingsConfiguration runtimeSettings;
    private readonly bool connectHardware;

    private MMDevice? recordingDevice;
    private WasapiCapture? captureStream;

    private volatile float volume;
    public float Volume => this.volume;
    private string? lastError;
    public string BackendName => "WASAPI";
    public string? LastError => Volatile.Read(ref this.lastError);

    private readonly MadmomHandler madmomHandler;
    // `beat` is the tempo service the beat detector reports into (owned by the
    // Operator, not part of Configuration). Pro DJ Link is owned directly by
    // Operator now so its portable UDP input is also available to Linux.
    public AudioInput(Configuration config, BeatBroadcaster beat) : this(
      config, beat, true) {
    }

    // The application host uses the public constructor above. The internal
    // switch lets Spectrum's integrated operator test run the complete input
    // schedule without opening a Windows audio endpoint or helper process.
    internal AudioInput(
      Configuration config, BeatBroadcaster beat, bool connectHardware
    ) {
      this.config = config;
      this.runtimeSettings = config as IRuntimeSettingsConfiguration ??
        throw new ArgumentException(
          "AudioInput requires immutable runtime settings.", nameof(config));
      this.connectHardware = connectHardware;
      this.madmomHandler = new MadmomHandler(config, this, beat);
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
          if (this.connectHardware) {
            try {
              this.InitializeAudio();
              Volatile.Write(ref this.lastError, null);
            } catch (Exception error) {
              Volatile.Write(ref this.lastError, error.Message);
              throw;
            }
          }
        } else {
          this.TerminateAudio();
        }
        this.active = value;
        if (this.connectHardware) {
          this.madmomHandler.Active = value;
        }
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
      AudioSettingsSnapshot settings =
        this.runtimeSettings.AudioSettingsSnapshot;
      if (settings.DeviceId == null) {
        throw new Exception("audioDeviceID not set!");
      }
      MMDevice? device = null;
      using (var enumerator = new MMDeviceEnumerator()) {
        var iterator = enumerator.EnumerateAudioEndPoints(
          DataFlow.Capture,
          DeviceState.Active
        );
        foreach (var audioDevice in iterator) {
          if (settings.DeviceId == audioDevice.ID) {
            device = audioDevice;
            break;
          }
          audioDevice.Dispose();
        }
      }
      if (device == null) {
        throw new Exception(
          "Configured audioDeviceID not found among active capture devices!"
        );
      }
      WasapiCapture? capture = null;
      try {
        var bitrate = device.AudioClient.MixFormat.BitsPerSample;
        capture = new WasapiCapture(device, false, bitrate);
        audioFormatSampleFrequency = device.AudioClient.MixFormat.SampleRate;
        capture.WaveFormat = new WaveFormat(
          audioFormatSampleFrequency,
          bitrate,
          device.AudioClient.MixFormat.Channels
        );
        capture.DataAvailable += Update;
        capture.StartRecording();
        this.recordingDevice = device;
        this.captureStream = capture;
      } catch {
        if (capture != null) {
          capture.DataAvailable -= Update;
          try {
            capture.Dispose();
          } catch (Exception e) {
            Debug.WriteLine(
              "AudioInput: error rolling back failed capture: " + e);
          }
        }
        try {
          device.Dispose();
        } catch (Exception e) {
          Debug.WriteLine(
            "AudioInput: error rolling back failed device: " + e);
        }
        throw;
      }
    }

    private void TerminateAudio() {
      WasapiCapture? capture = this.captureStream;
      this.captureStream = null;
      if (capture != null) {
        // Detach the handler before stopping: StopRecording can raise a final
        // DataAvailable, and we don't want it running against a device we're
        // about to dispose/null.
        capture.DataAvailable -= Update;
        try {
          capture.StopRecording();
        } catch (Exception e) {
          Debug.WriteLine("AudioInput: error stopping capture: " + e);
        }
        try {
          capture.Dispose();
        } catch (Exception e) {
          Debug.WriteLine("AudioInput: error disposing capture: " + e);
        }
      }
      MMDevice? device = this.recordingDevice;
      this.recordingDevice = null;
      if (device != null) {
        try {
          device.Dispose();
        } catch (Exception e) {
          Debug.WriteLine("AudioInput: error disposing recording device: " + e);
        }
      }
      // Don't leave a stale peak reading behind once capture has stopped.
      this.volume = 0.0f;
    }

    private void Update(object? sender, NAudio.Wave.WaveInEventArgs args) {
      // The capture thread can still deliver a buffer as TerminateAudio nulls
      // recordingDevice; read it into a local and bail if it's already gone.
      var device = this.recordingDevice;
      if (device == null) {
        return;
      }
      this.volume = device.AudioMeterInformation.MasterPeakValue;
    }

    public void OperatorUpdate() {
    }

    public static List<AudioDevice> AudioDevices {
      get {
        var audioDeviceList = new List<AudioDevice>();
        using (var enumerator = new MMDeviceEnumerator()) {
          var iterator = enumerator.EnumerateAudioEndPoints(
            // We avoid filtering in the call here so we can get the right device
            // index, which we need in our call to madmom. madmom uses PyAudio,
            // which in turn uses PortAudio, which doesn't support the use of the
            // "Endpoint ID string" (audioDevice.ID) to identify an audio device.
            DataFlow.All,
            DeviceState.Active
          );
          int i = 0;
          foreach (var audioDevice in iterator) {
            using (audioDevice) {
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
          }
        }
        return audioDeviceList;
      }
    }

    public IReadOnlyList<AudioCaptureDevice> GetAvailableDevices() =>
      AudioDevices.ConvertAll(device =>
        new AudioCaptureDevice(device.id, device.name));

    public int CurrentAudioDeviceIndex {
      get {
        string? deviceId =
          this.runtimeSettings.AudioSettingsSnapshot.DeviceId;
        if (deviceId == null) {
          return -1;
        }
        foreach (var audioDevice in AudioInput.AudioDevices) {
          if (audioDevice.id == deviceId) {
            return audioDevice.index;
          }
        }
        return -1;
      }
    }
  }
}
