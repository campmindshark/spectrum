using System;
using System.Collections.Generic;
using System.Linq;
using LEDome;

namespace Spectrum {
  public class Visualizer {
    // FFT analysis dicts
    private Dictionary<String, double[]> bins;
    private Dictionary<String, float[]> energyHistory;
    private Dictionary<String, float> energyLevels;
    private int historyLength = 16;
    private int processCount;

    // analysis/history variables
    private bool silence = true;
    private int silentCounter = 0;
    public bool silentMode = true;
    private bool silentModeAlternatingFlag = false;
    private int silentModeHueIndex = 0;
    private int silentModeSatIndex = 254;
    private bool silentModeSatFall = false;
    private int silentModeLightIndex = 0;
    private bool kickCounted = false;
    private bool snareCounted = false;
    private bool kickMaxPossible = false;
    private bool kickMax = false;
    private bool kickPending = false;
    private bool snarePending = false;
    private bool snareMaxPossible = false;
    private bool snareMax = false;
    private bool totalMaxPossible = false;
    private bool totalMax = false;
    private int idleCounter = 0;
    private bool lightPending = false;
    private bool drop = false;
    private bool dropPossible = false;
    private int dropDuration = 0;
    private int target = 0;
    public bool controlLights;
    public int brighten = 0;
    public int colorslide = 0;
    public int sat = 0;
    public int needupdate = 0;
    private float vol = 0;

    private SquareAPI api;

    public Visualizer(bool controlLights) {
      this.api = new SquareAPI("COM3", 30, 5);
      this.api.Open();

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

    // music pattern detection
    public void process(float[] spectrum, float level, float peakChange, float dropQuiet, float dropThreshold, float kickQuiet, float kickChange, float snareQuiet, float snareChange) {
      vol = level;
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
          if (current >= history.Max() && current > avg + peakChange * sd) {
            // was: avg < .08
            if (current > 3 * avg && avg < dropQuiet && change > dropThreshold && current > .26) {
              System.Diagnostics.Debug.WriteLine(probe(band, current, avg, sd, change));
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
            kickCounted = false;
          }
          // was: avg < .1, current > avg + 2 * sd
          if (current > avg + kickChange * sd && avg < kickQuiet && current > .001) // !kickcounted here
          {
            if (totalMax) {
              System.Diagnostics.Debug.WriteLine(probe(band, current, avg, sd, change));
            }
            kickCounted = true;
            kickPending = true;
          }
        }
        if (band == "snareattack") {
          if (current < avg || change < 0) {
            snareCounted = false;
          }
          if (current > avg + snareChange * sd && avg < snareQuiet && current > .001) // !snarecounted here
          {
            if (totalMax && current > .001) {
              System.Diagnostics.Debug.WriteLine(probe(band, current, avg, sd, change));
            }
            snareCounted = true;
            snarePending = true;
          }
        }
      }
      foreach (String band in energyHistory.Keys.ToList()) {
        energyHistory[band][processCount] = energyLevels[band];
        energyLevels[band] = 0;
      }
      silence = (level < .01) && silence;
    }

    // status update for hues
    public void updateHues() {
      int numColumnsToLight = (int)(vol * 30);
      for (int j = 0; j < 40; j++) {
        for (int i = 0; i < 30; i++) {
          int color = numColumnsToLight > i ? 0xFFFFFF : 0x000000;
          this.api.SetPixel(i, j, color);
        }
      }
      this.api.Flush();

      // run every tick of the timer
      if (silence) {
        System.Diagnostics.Debug.WriteLine("silence");
      }
      System.Diagnostics.Debug.WriteLine(vol);
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