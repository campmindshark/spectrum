using System;
using System.Collections.Generic;
using System.Linq;
using Spectrum.LEDs;
using Spectrum.Base;
using Spectrum.Hues;
using Spectrum.Audio;
using System.Threading;

namespace Spectrum {

  class HueAudioVisualizer : Visualizer {

    private Configuration config;
    private AudioInput audio;
    private HueOutput hue;

    private Random random;

    // FFT analysis dicts
    private Dictionary<String, double[]> bins;
    private Dictionary<String, float[]> energyHistory;
    private Dictionary<String, float> energyLevels;
    private int historyLength = 16;
    private int processCount;

    // analysis/history variables
    private bool kickPending = false;
    private bool snarePending = false;
    private bool totalMaxPossible = false;
    private bool totalMax = false;
    private int idleCounter = 0;
    private bool lightPending = false;
    private bool drop = false;
    private bool dropPossible = false;
    private int dropDuration = 0;
    private int target = 0;

    public HueAudioVisualizer(
      Configuration config,
      AudioInput audio,
      HueOutput hue
    ) {
      this.config = config;
      this.audio = audio;
      this.hue = hue;
      this.hue.RegisterVisualizer(this);

      this.random = new Random();
      bins = new Dictionary<String, double[]>();
      energyHistory = new Dictionary<String, float[]>();
      energyLevels = new Dictionary<String, float>();

      // frequency detection bands
      // format: { bottom freq, top freq, activation level (delta)}
      bins.Add("midrange", new double[] { 250, 2000, .025 });
      bins.Add("total", new double[] { 60, 2000, .05 });
      // specific instruments
      bins.Add("kick", new double[] { 40, 50, .001 });
      bins.Add("snareattack", new double[] { 1500, 2500, .001 });
      foreach (String band in bins.Keys) {
        energyLevels.Add(band, 0);
        energyHistory.Add(band, Enumerable.Repeat((float)0, historyLength).ToArray());
      }
    }

    public int Priority {
      get {
        return 1;
      }
    }

    // We don't actually care about this
    public bool Enabled { get; set; } = false;

    public Input[] GetInputs() {
      return new Input[] { this.audio };
    }

    public void Visualize() {
      this.process(this.audio.AudioData, this.audio.Volume);
      if (this.hue.BufferSize == 0) {
        this.updateHues();
      }
    }

    // music pattern detection
    private void process(float[] spectrum, float level) {
      processCount++;
      processCount = processCount % historyLength;
      for (int i = 1; i < spectrum.Length / 2; i++) {
        foreach (KeyValuePair<String, double[]> band in bins) {
          String name = band.Key;
          double[] window = band.Value;
          if (windowContains(window, i)) {
            energyLevels[name] += (spectrum[i] * spectrum[i]);
          }
        }
      }
      foreach (String band in energyHistory.Keys.ToList()) {
        float current = energyLevels[band];
        float[] history = energyHistory[band];
        float previous = history[(processCount + historyLength - 1) % historyLength];
        float change = current - previous;
        float avg = history.Average();
        float ssd = history.Select(val => (val - avg) * (val - avg)).Sum();
        float sd = (float)Math.Sqrt(ssd / historyLength);
        float threshold = (float)bins[band][2];
        bool signal = change > threshold;
        if (band == "total") {
          if (totalMaxPossible && change < 0) {
            totalMax = true;
            totalMaxPossible = false;
            if (dropPossible) {
              drop = true;
              dropPossible = false;
            }
          }
          if (current >= history.Max() && current > avg + this.config.peakC * sd) {
            // was: avg < .08
            if (current > 3 * avg && avg < this.config.dropQ && change > this.config.dropT && current > .26) {
              //System.Diagnostics.Debug.WriteLine(probe(band, current, avg, sd, change));
              dropPossible = true;
            }
            totalMaxPossible = true;
          } else {
            dropPossible = false;
            totalMaxPossible = false;
          }
        }
        if (band == "kick") {
          if (current < avg || change < 0) {
          }
          // was: avg < .1, current > avg + 2 * sd
          if (current > avg + this.config.kickT * sd && avg < this.config.kickQ && current > .001) // !kickcounted here
          {
            if (totalMax) {
              //System.Diagnostics.Debug.WriteLine(probe(band, current, avg, sd, change));
            }
            kickPending = true;
          }
        }
        if (band == "snareattack") {
          if (current > avg + this.config.snareT * sd && avg < this.config.snareQ && current > .001) // !snarecounted here
          {
            if (totalMax && current > .001) {
              //System.Diagnostics.Debug.WriteLine(probe(band, current, avg, sd, change));
            }
            snarePending = true;
          }
        }
      }
      foreach (String band in energyHistory.Keys.ToList()) {
        energyHistory[band][processCount] = energyLevels[band];
        energyLevels[band] = 0;
      }
    }

    // status update for hues
    private void updateHues() {
      if (!lightPending) {
        kickPending = kickPending && totalMax;
        snarePending = snarePending && totalMax;
        target = this.random.Next(this.config.hueIndices.Length);
      }
      if (drop) {
        if (dropDuration == 0) {
          //System.Diagnostics.Debug.WriteLine("dropOn");
          this.hue.SendGroupCommand(
            0,
            new HueCommand() {
              /*on = true,
              bri = 254,
              hue = this.random.Next(1, 65535),
              sat = 254,
              transitiontime = 0,
              effect = "colorloop",*/
              alert = "select",
            }
          );
        }
        // was: dropDuration == 1
        else if (dropDuration == 4) {
          /*this.hue.SendGroupCommand(
            0,
            new HueCommand() {
              bri = 1,
              effect = "colorloop",
              transitiontime = 2,
            }
          );*/
        } else if (dropDuration > 8) {
          //System.Diagnostics.Debug.WriteLine("dropOff");
          drop = false;
          dropDuration = -1;
        }
        dropDuration++;
      } else if (kickPending) {
        if (lightPending) {
          this.hue.SendLightCommand(
            target,
            new HueCommand() {
              on = true,
              bri = 1,
              hue = 300,
              sat = 254,
              transitiontime = 2,
              alert = "none",
            }
          );
          lightPending = false;
          kickPending = false;
        } else {
          lightPending = true;
          //System.Diagnostics.Debug.WriteLine("kickOn");
          this.hue.SendLightCommand(
            target,
            new HueCommand() {
              on = true,
              bri = 254,
              hue = 300,
              sat = 254,
              transitiontime = 1,
              alert = "none",
            }
          );
        }
      } else if (snarePending) { // second highest priority: snare hit (?)
        if (lightPending) {
          this.hue.SendLightCommand(
            target,
            new HueCommand() {
              on = true,
              bri = 1,
              hue = 43000,
              sat = 254,
              transitiontime = 2,
              alert = "none",
            }
          );
          snarePending = false;
          lightPending = false;
        } else {
          lightPending = true;
          //System.Diagnostics.Debug.WriteLine("snareOn");
          this.hue.SendLightCommand(
            target,
            new HueCommand() {
              on = true,
              bri = 254,
              hue = 43000,
              sat = 254,
              transitiontime = 1,
              alert = "none",
            }
          );
        }
      } else {
        idleCounter++;
        // was: idlecounter > 4
        if (idleCounter > 2) {
          this.hue.SendLightCommand(
            target,
            new HueCommand() {
              on = false,
              bri = 0,
              sat = 254,
              transitiontime = 20,
              alert = "none",
              effect = "colorloop",
            }
          );
          idleCounter = 0;
        }
      }
      totalMax = false;
    }

    // math helper functions
    private bool windowContains(double[] window, int index) {
      return (freqToFFTBin(window[0]) <= index && freqToFFTBin(window[1]) >= index);
    }

    private int freqToFFTBin(double freq) {
      return (int)(freq / 2.69);
    }

    private int binWidth(String bin) {
      double[] window = bins[bin];
      return freqToFFTBin(window[1]) - freqToFFTBin(window[0]);
    }

    private String probe(String band, float current, float avg, float sd, float change) {
      return "Band:" + band + " cur:" + Math.Round(current * 10000) / 10000 + " avg:" + Math.Round(avg * 10000) / 10000 + " sd:" + Math.Round(sd * 10000) / 10000 + " delta:" + Math.Round(change * 10000) / 10000;
    }

  }

}