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
using System.Collections.Concurrent;

namespace Spectrum.Audio {

  public enum AudioDetectorType : byte { Kick, Snare }

  public class AudioEvent {
    public AudioDetectorType type;
    public double significance;
  }

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

    private static Dictionary<AudioDetectorType, double[]> bins =
      new Dictionary<AudioDetectorType, double[]>() {
        { AudioDetectorType.Kick, new double[] { 40, 50 } },
        { AudioDetectorType.Snare, new double[] { 1500, 2500 } },
      };

    private static bool WindowContains(double[] window, int index) {
      return (FreqToFFTBin(window[0]) <= index
        && FreqToFFTBin(window[1]) >= index);
    }

    private static int FreqToFFTBin(double freq) {
      return (int)(freq / 2.69);
    }

    private Configuration config;

    // Needed for memory management reasons. More details in MagicIncantations()
    private WASAPIPROC process;

    // These values get continuously updated by the internal thread
    public float[] AudioData { get; private set; } = new float[8192];
    public float Volume { get; private set; } = 0.0f;

    // We loop around the history array based on this offset
    private int currentHistoryOffset = 0;

    public double BPM { get; private set; } = 0.0;

    private static int historyLength = 32;
    private Dictionary<AudioDetectorType, double[]> energyHistory;
    private ConcurrentDictionary<AudioDetectorType, AudioEvent> eventBuffer;
    private List<AudioEvent> eventsSinceLastTick;
    private Dictionary<AudioDetectorType, long> lastEventTime;

    public AudioInput(Configuration config) {
      this.config = config;

      BassNet.Registration("larry.fenn@gmail.com", "2X531420152222");

      // This is being initialized here because we need to pass this variable
      // into some scary old C-style API and it doesn't get refcounted there.
      // We declare the variable as a member to avoid garbage collection.
      this.process = new WASAPIPROC(this.NoOp);

      this.energyHistory = new Dictionary<AudioDetectorType, double[]>();
      this.eventBuffer =
        new ConcurrentDictionary<AudioDetectorType, AudioEvent>();
      this.eventsSinceLastTick = new List<AudioEvent>();
      this.lastEventTime = new Dictionary<AudioDetectorType, long>();
      foreach (var key in bins.Keys) {
        this.energyHistory[key] = new double[historyLength];
        this.lastEventTime[key] = 0;
      }
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
        BASSWASAPIInit.BASS_WASAPI_BUFFER | BASSWASAPIInit.BASS_WASAPI_AUTOFORMAT,
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
        this.UpdateEnergyHistory();
      }
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
      return AudioInput.GetBinsEnergy(
        preset.FilterRangeStart,
        preset.FilterRangeEnd,
        this.AudioData
      );
    }

    private static double GetBinsEnergy(
      double low,
      double high,
      float[] audioData
    ) {
      double lowFreq = AudioInput.EstimateFreq(low, 144.4505);
      int lowBinIndex = (int)AudioInput.GetFrequencyBinUnrounded(lowFreq);
      //double highFreq = AudioInput.EstimateFreq(high, 144.4505);
      //int highBinIndex = (int)Math.Ceiling(AudioInput.GetFrequencyBinUnrounded(highFreq));
      //return audioData.Skip(lowBinIndex).Take(highBinIndex - lowBinIndex + 1).Max();
      if (high == 1.0) {
        // Special case: we want to scoop up all the high frequencies.
        // So we take the maximum from the low frequency cutoff all the way
        // to the highest audible frequency.
        // This will cause a lot more signal to be detected, so "low" should
        // be increased to cut down on false positives
        return audioData.Skip(lowBinIndex).Max();
      } else {
        // Otherwise, our target range covers a specific number of bins
        // Take the root-mean-square to get a measurement from 0 to 1 of
        // 'sound energy' in those bins.
        double highFreq = AudioInput.EstimateFreq(high, 144.4505);
        int highBinIndex =
          (int)Math.Ceiling(AudioInput.GetFrequencyBinUnrounded(highFreq));
        int nBins = highBinIndex - lowBinIndex + 1;
        double energyAccumulate = 0.0;
        // each bin should have a value between 0 and 1 for amplitude
        int maxPossibleEnergy = nBins;
        for (int bin = lowBinIndex; bin <= highBinIndex; bin++) {
          energyAccumulate += (audioData[bin] * audioData[bin]);
        }
        return Math.Sqrt(energyAccumulate / nBins);
      }
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
      int fftSampleN = 8192;
      int streamRate = 44100;
      return fftSampleN * frequency / streamRate;
    }

  }

}