using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Spectrum.Base;
using System.Diagnostics;
using System.Collections.Concurrent;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Dsp;

namespace Spectrum.Audio {

  public enum AudioDetectorType : byte { Kick, Snare }

  public class AudioEvent {
    public AudioDetectorType type;
    public double significance;
  }

  public class AudioInput : Input {

    private static readonly int fftSize = 32768;
    // Read this value from the sound card later.
    private static int audioFormatSampleFrequency = -1;
    private static readonly Dictionary<AudioDetectorType, double[]> bins =
      new Dictionary<AudioDetectorType, double[]>() {
        { AudioDetectorType.Kick, new double[] { 40, 50 } },
        { AudioDetectorType.Snare, new double[] { 1500, 2500 } },
      };


    private readonly Configuration config;

    private MMDevice recordingDevice;
    private WasapiCapture captureStream;
    private readonly List<short> unanalyzedValues = new List<short>();

    // These values get continuously updated by the internal thread
    public float[] AudioData { get; private set; } = new float[fftSize];
    private readonly ConcurrentDictionary<string, double> maxAudioDataLevels = new ConcurrentDictionary<string, double>();
    public float Volume { get; private set; } = 0.0f;

    // We loop around the history array based on this offset
    private int currentHistoryOffset = 0;

    public double BPM { get; private set; } = 0.0;

    private static readonly int historyLength = 32;
    private readonly Dictionary<AudioDetectorType, double[]> energyHistory;
    private readonly ConcurrentDictionary<AudioDetectorType, AudioEvent> eventBuffer;
    private List<AudioEvent> eventsSinceLastTick;
    private readonly Dictionary<AudioDetectorType, long> lastEventTime;
    private long quietSince = -1;

    private readonly MadmomHandler madmomHandler;
    private readonly CarabinerHandler carabinerHandler;

    public AudioInput(Configuration config) {
      this.config = config;

      this.energyHistory = new Dictionary<AudioDetectorType, double[]>();
      this.eventBuffer =
        new ConcurrentDictionary<AudioDetectorType, AudioEvent>();
      this.eventsSinceLastTick = new List<AudioEvent>();
      this.lastEventTime = new Dictionary<AudioDetectorType, long>();
      foreach (var key in bins.Keys) {
        this.energyHistory[key] = new double[historyLength];
        this.lastEventTime[key] = 0;
      }

      this.madmomHandler = new MadmomHandler(config, this);
      this.carabinerHandler = new CarabinerHandler(config);
    }

    private bool active;
    public bool Active {
      get {
        lock (this.maxAudioDataLevels) {
          return this.active;
        }
      }
      set {
        lock (this.maxAudioDataLevels) {
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
          this.carabinerHandler.Active = value;
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
      if (this.config.audioDeviceID == null) {
        throw new Exception("audioDeviceID not set!");
      }
      var iterator = new MMDeviceEnumerator().EnumerateAudioEndPoints(
        DataFlow.Capture,
        DeviceState.Active
      );
      MMDevice device = null;
      foreach (var audioDevice in iterator) {
        if (this.config.audioDeviceID == audioDevice.ID) {
          device = audioDevice;
          break;
        }
      }
      if (device == null) {
        throw new Exception("audioDeviceID not set!");
      }
      this.recordingDevice = device;
      var bitrate = device.AudioClient.MixFormat.BitsPerSample;
      audioFormatSampleFrequency = device.AudioClient.MixFormat.SampleRate;
      this.captureStream = new WasapiCapture(device, false, bitrate);
      this.captureStream.WaveFormat = new WaveFormat(
        audioFormatSampleFrequency,
        bitrate,
        device.AudioClient.MixFormat.Channels
      );
      this.captureStream.DataAvailable += Update;
      this.captureStream.StartRecording();
      this.quietSince = -1;
    }

    private void TerminateAudio() {
      this.captureStream.StopRecording();
      this.captureStream = null;
    }

    private void Update(object sender, NAudio.Wave.WaveInEventArgs args) {
      this.Volume = recordingDevice.AudioMeterInformation.MasterPeakValue;
      lock (this.maxAudioDataLevels) {
        short[] values = new short[args.Buffer.Length / 2];
        for (int i = 0; i < args.BytesRecorded; i += 2) {
          values[i / 2] = (short)((args.Buffer[i + 1] << 8) | args.Buffer[i]);
        }
        this.unanalyzedValues.AddRange(values);

        if (this.unanalyzedValues.Count >= fftSize) {
          this.GenerateAudioData();
          this.unanalyzedValues.Clear();
        }

        foreach (var pair in this.config.levelDriverPresets) {
          if (pair.Value.Source != LevelDriverSource.Audio) {
            continue;
          }
          AudioLevelDriverPreset preset = (AudioLevelDriverPreset)pair.Value;
          double filteredMax = AudioInput.GetFilteredMax(
            preset.FilterRangeStart,
            preset.FilterRangeEnd,
            this.AudioData
          );
          if (this.maxAudioDataLevels.ContainsKey(preset.Name)) {
            this.maxAudioDataLevels[preset.Name] = Math.Max(
              this.maxAudioDataLevels[preset.Name],
              filteredMax
            );
          } else {
            this.maxAudioDataLevels[preset.Name] = filteredMax;
          }
        }
        this.UpdateEnergyHistory();
      }
    }

    private void GenerateAudioData() {
      // Q: make sure that fft_data can be 'filled in' by values[]
      // Ideally buffer should have exactly as much data as we need for fft
      // Possibly tweak latency or the thread sleep duration? Alternatively increase FFT reso.
      Complex[] fft_data = new Complex[fftSize];
      int i = 0;
      foreach (var value in this.unanalyzedValues.GetRange(0, fftSize)) {
        fft_data[i].X = (float)(value * FastFourierTransform.BlackmannHarrisWindow(i, fftSize));
        fft_data[i].Y = 0;
        i++;
      }
      FastFourierTransform.FFT(true, (int)Math.Log(fftSize, 2.0), fft_data);

      // FFT results are Complex
      // Now we want the magnitude of each band
      // Note: cumulative power after transformation is slightly higher than original. Precision errors?
      // In any case we're scaling so that total power is 1.
      // TODO: scaling is all over the place in terms of where and when it's done. this should be fixed:
      // Possible places where we could or already do re-scale:
      // - Scaling on input (ex. "auto-gain")
      // - Scaling on FFT results (numerical precision concern?)
      //   This one is required by FFT theory - cumulative power 'in' and 'out' of the transform is preserved
      // - Scaling on processing (statistical methods might require values in a specific domain)

      float[] fft_results = new float[fftSize];

      float maxBinValue = 0;
      for (int j = 0; j < fftSize; j++) {
        fft_results[j] = Magnitude(fft_data[j].X, fft_data[j].Y) / fftSize;
        if (fft_results[j] > maxBinValue) maxBinValue = fft_results[j];
      }

      // Here we arbitrarily re-scale so that the max is 1
      for (int j = 0; j < fftSize; j++) {
        fft_results[j] = fft_results[j] / maxBinValue;
      }
      this.AudioData = fft_results;
    }

    private float Magnitude(float x, float y) {
      return (float)Math.Sqrt((float)Math.Pow(x, 2) + (float)Math.Pow(y, 2));
    }

    private void UpdateEnergyHistory() {
      var energyLevels = new Dictionary<AudioDetectorType, double>();
      foreach (var key in bins.Keys) {
        energyLevels[key] = 0.0;
      }
      for (int i = 1; i < this.AudioData.Length / 2; i++) {
        foreach (var pair in bins) {
          AudioDetectorType type = pair.Key;
          double[] window = pair.Value;
          if (WindowContains(window, i)) {
            energyLevels[type] += this.AudioData[i] * this.AudioData[i];
          }
        }
      }

      foreach (var type in bins.Keys) {
        double current = energyLevels[type];
        double[] history = this.energyHistory[type];
        double previous = history[
          (this.currentHistoryOffset + historyLength - 1) % historyLength
        ];
        double change = current - previous;
        double avg = history.Average();
        double max = history.Max();
        double ssd = history.Select(val => (val - avg) * (val - avg)).Sum();
        double sd = Math.Sqrt(ssd / historyLength);
        double stdsFromAverage = (current - avg) / sd;

        if (type == AudioDetectorType.Kick) {
          if (
            current > max &&
            stdsFromAverage > this.config.kickT &&
            avg < this.config.kickQ &&
            current > .001
          ) {
            double significance = Math.Atan(
              stdsFromAverage / (this.config.kickT + 0.001)
            ) * 2 / Math.PI;
            this.UpdateEvent(type, significance);
          }
        } else if (type == AudioDetectorType.Snare) {
          if (
            current > max &&
            stdsFromAverage > this.config.snareT &&
            avg < this.config.snareQ &&
            current > .001
          ) {
            double significance = Math.Atan(
              stdsFromAverage / (this.config.snareT + 0.001)
            ) * 2 / Math.PI;
            this.UpdateEvent(type, significance);
          }
        }
      }

      foreach (var type in bins.Keys) {
        this.energyHistory[type][this.currentHistoryOffset] =
          energyLevels[type];
      }
      this.currentHistoryOffset = (this.currentHistoryOffset + 1)
        % historyLength;
    }

    private void UpdateEvent(AudioDetectorType type, double significance) {
      this.eventBuffer.AddOrUpdate(
        type,
        audioDetectorType => {
          return new AudioEvent() {
            type = audioDetectorType,
            significance = significance,
          };
        },
        (audioDetectorType, existingAudioEvent) => {
          existingAudioEvent.significance = Math.Max(
            existingAudioEvent.significance,
            significance
          );
          return existingAudioEvent;
        }
      );
    }

    public void OperatorUpdate() {
      long timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      this.eventsSinceLastTick = new List<AudioEvent>(
        bins.Keys.Select(type => {
          var earliestNextEventTime =
            this.lastEventTime[type] + this.config.domeAutoFlashDelay;
          if (timestamp < earliestNextEventTime) {
            return null;
          }
          AudioEvent audioEvent;
          this.eventBuffer.TryRemove(type, out audioEvent);
          if (audioEvent != null) {
            this.lastEventTime[type] = timestamp;
          }
          return audioEvent;
        }).Where(audioEvent => audioEvent != null)
      );

      if (this.Volume > 0.01) {
        this.quietSince = -1;
      } else if (this.quietSince == -1) {
        this.quietSince = timestamp;
      }
    }

    public bool isQuiet {
      get {
        if (this.quietSince == -1) {
          return false;
        }
        long timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        return timestamp - this.quietSince > 7500;
      }
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

    public List<AudioEvent> GetEventsSinceLastTick() {
      return this.eventsSinceLastTick;
    }

    public double LevelForChannel(int channelIndex) {
      double? midiLevel =
        this.config.beatBroadcaster.CurrentMidiLevelDriverValueForChannel(
          channelIndex
        );
      if (midiLevel.HasValue) {
        return midiLevel.Value;
      }
      if (!this.config.channelToAudioLevelDriverPreset.ContainsKey(channelIndex)) {
        return this.Volume;
      }
      string audioPreset =
        this.config.channelToAudioLevelDriverPreset[channelIndex];
      if (
        audioPreset == null ||
        !this.config.levelDriverPresets.ContainsKey(audioPreset) ||
        !(this.config.levelDriverPresets[audioPreset] is AudioLevelDriverPreset)
      ) {
        return 0.0;
      }
      AudioLevelDriverPreset preset =
        (AudioLevelDriverPreset)this.config.levelDriverPresets[audioPreset];
      if (preset.FilterRangeStart == 0.0 && preset.FilterRangeEnd == 1.0) {
        return this.Volume;
      }
      var maxLevel = this.maxAudioDataLevels.ContainsKey(preset.Name)
        ? this.maxAudioDataLevels[preset.Name]
        : 1.0;

      return AudioInput.GetFilteredMax(
        preset.FilterRangeStart,
        preset.FilterRangeEnd,
        this.AudioData
      ) / maxLevel;
    }

    private static double GetFilteredMax(
      double low,
      double high,
      float[] audioData
    ) {
      double lowFreq = AudioInput.EstimateFreq(low, 144.4505);
      int lowBinIndex = (int)AudioInput.GetFrequencyBinUnrounded(lowFreq);
      double highFreq = AudioInput.EstimateFreq(high, 144.4505);
      int highBinIndex = (int)Math.Ceiling(AudioInput.GetFrequencyBinUnrounded(highFreq));
      return audioData.Skip(lowBinIndex).Take(highBinIndex - lowBinIndex + 1).Max();
    }

    // x is a number between 0 and 1
    private static double EstimateFreq(double x, double freqScale) {
      // Changing freq_scale will lower resolution at the lower frequencies in exchange for more coverage of higher frequencies
      // 119.65 corresponds to a tuning that covers the human voice
      // 123.52 corresponds to a tuning that covers the top of a Soprano sax
      // 144.45 corresponds to a tuning that includes the top end of an 88-key piano
      // 150.69 corresponds to a tuning that exceeds a piccolo
      return 15.0 + Math.Exp(.05773259 * freqScale * x);
    }

    private static double GetFrequencyBinUnrounded(double frequency) {
      return fftSize * frequency / (audioFormatSampleFrequency * 2);
    }

    private static int FreqToFFTBin(double frequency) {
      return (int)GetFrequencyBinUnrounded(frequency);
    }

    private static bool WindowContains(double[] window, int index) {
      return (FreqToFFTBin(window[0]) <= index
        && FreqToFFTBin(window[1]) >= index);
    }

  }

}
