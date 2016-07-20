using System;
using System.Collections.Generic;
using System.Linq;
using LEDome;

namespace Spectrum {
  public class Visualizer {

    private List<int> lights;
    private Random rnd;
    private String hubaddress = "http://192.168.1.26/api/161d04c425fa45e293386cf241a26bf/";

    // state variables
    public bool controlLights = true;
    public bool lightsOff = false;
    public bool redAlert = false;

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
    public int brighten = 0;
    public int colorslide = 0;
    public int sat = 0;
    public int needupdate = 0;
    private float vol = 0;

    private SquareAPI api;

    public Visualizer() {
      
      /*this.api = new SquareAPI("COM3", 30, 5);
      this.api.Open();*/
      
      rnd = new Random();
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
      lights = new List<int>(); // light IDs in left to right order
      lights.Add(2);
      lights.Add(1);
      lights.Add(4);
      lights.Add(5);
      lights.Add(6);
    }

    public void init(bool controlLights) {
      this.controlLights = controlLights;
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
      if (!lightPending) {
        kickPending = kickPending && totalMax;
        snarePending = snarePending && totalMax;
        target = rnd.Next(5);
      }
      if (silentMode || !controlLights || lightsOff || redAlert) {
        if (silentMode || needupdate > 0) // not idling & nonzero lights need to be updated.
        {
          silentModeAlternatingFlag = !silentModeAlternatingFlag;
          if (!silentMode || (silentMode && silentModeAlternatingFlag) || !controlLights || lightsOff || redAlert) {
            silentModeHueIndex = (silentModeHueIndex + 10000) % 65535;
            silentModeLightIndex = (silentModeLightIndex + 1) % 5;
            new System.Net.WebClient().UploadStringAsync(new Uri(hubaddress + laddressHelper(lights[silentModeLightIndex])), "PUT", silent(silentModeHueIndex));
            needupdate--;
            System.Diagnostics.Debug.WriteLine("Updating.." + needupdate);
            System.Diagnostics.Debug.WriteLine("silentMode.." + silentMode);
          }
        }
      } else if (drop) {
        if (dropDuration == 0) {
          System.Diagnostics.Debug.WriteLine("dropOn");
          new System.Net.WebClient().UploadStringAsync(new Uri(hubaddress + "groups/0/action/"), "PUT", dropEffect(true));
        }
        // was: dropDuration == 1
        else if (dropDuration == 4) {
          //new System.Net.WebClient().UploadStringAsync(new Uri(hubaddress + "groups/0/action/"), "PUT", dropEffect(false));
        } else if (dropDuration > 8) {
          System.Diagnostics.Debug.WriteLine("dropOff");
          drop = false;
          dropDuration = -1;
        }
        dropDuration++;
      } else if (kickPending) {
        if (lightPending) {
          new System.Net.WebClient().UploadStringAsync(new Uri(hubaddress + laddressHelper(lights[target])), "PUT", kickEffect(false));
          lightPending = false;
          kickPending = false;
        } else {
          lightPending = true;
          System.Diagnostics.Debug.WriteLine("kickOn");
          new System.Net.WebClient().UploadStringAsync(new Uri(hubaddress + laddressHelper(lights[target])), "PUT", kickEffect(true));
        }
      } else if (snarePending) // second highest priority: snare hit (?)
        {
        if (lightPending) {
          new System.Net.WebClient().UploadStringAsync(new Uri(hubaddress + laddressHelper(lights[target])), "PUT", snareEffect(false));
          snarePending = false;
          lightPending = false;
        } else {
          lightPending = true;
          System.Diagnostics.Debug.WriteLine("snareOn");
          new System.Net.WebClient().UploadStringAsync(new Uri(hubaddress + laddressHelper(lights[target])), "PUT", snareEffect(true));
        }
      } else {
        idleCounter++;
        // was: idlecounter > 4
        if (idleCounter > 2) {
          new System.Net.WebClient().UploadStringAsync(new Uri(hubaddress + laddressHelper(lights[target])), "PUT", idle());
          idleCounter = 0;
        }
      }
      postUpdate();
      
      /*int numColumnsToLight = (int)(vol * 30);
      for (int j = 0; j < 40; j++) {
        for (int i = 0; i < 30; i++) {
          int color = numColumnsToLight > i ? 0xFFFFFF : 0x000000;
          this.api.SetPixel(i, j, color);
        }
      }
      this.api.Flush();*/
      
      // run every tick of the timer
      if (silence) {
        System.Diagnostics.Debug.WriteLine("silence");
      }
      System.Diagnostics.Debug.WriteLine(vol);
    }
    private void postUpdate() {
      if (controlLights && silence && silentCounter > 40 && !silentMode) {
        System.Diagnostics.Debug.WriteLine("Silence detected.");
        silentMode = true;
      } else if (silence) {
        silentCounter++;
      }
      if (!silence) {
        silentCounter = 0;
        silentMode = false;
      }
      if (silentModeAlternatingFlag) {
        if (silentModeSatIndex < 127) {
          silentModeSatFall = false;
        }
        if (silentModeSatIndex > 380) {
          silentModeSatFall = true;
        }
        if (silentModeSatFall) {
          silentModeSatIndex--;
        } else {
          silentModeSatIndex++;
        }
      }
      // this will be changed in process() UNLESS level < .1 for the duration of process()
      silence = true;
      totalMax = false;
      kickMax = false;
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
    private String laddressHelper(int address) {
      return "lights/" + address + "/state/";
    }
    private String idle() {
      return jsonMake(0, -1, 254, 20, "none");
    }
    private String silent(int index) {
      if (lightsOff) {
        return "{\"on\": false}";
      }
      if (redAlert) {
        return "{\"on\": true,\"hue\":1,\"effect\":\"none\",\"sat\":254,\"bri\":1}";
      } else if (controlLights) {
        return "{\"on\": true,\"hue\":" + (index + 1) + ",\"effect\":\"none\",\"bri\":1,\"sat\":" + Math.Min(silentModeSatIndex, 254) + ",\"transitiontime\":12}";
      } else {
        int newbri = Math.Min(Math.Max(254 + 64 * brighten, 1), 254);
        int newsat = Math.Min(Math.Max(126 + 63 * sat, 0), 254);
        int newhue = Math.Min(Math.Max(16384 + colorslide * 4096, 0), 65535);
        return "{\"on\": true,\"hue\":" + newhue + ",\"sat\":" + newsat + ",\"bri\":" + newbri + ",\"effect\":\"none\"}";
      }
    }

    private String jsonMake(int bri, int hue, int sat, int transitiontime, String alert) {
      // bri: 1-254 brightness, -1 to do nothing, 0 to turn off
      // hue: 0-65535 hue, actual color reproduction hardware-dependent
      // sat: 0-254 saturation, 0 white
      // transitiontime: ms, -1 to use defaults
      // alert: none - no effect
      //        select - a breath cycle (aka flash)
      //        lselect - breath cycles for 15 seconds
      String result = "{";
      if (bri == 0) {
        result += "\"on\":" + "false" + ",";
      } else {
        result += "\"on\":" + "true" + ",";
      }
      if (bri != -1) {
        result += "\"bri\":" + bri + ",";
      }
      if (hue != -1) {
        result += "\"hue\":" + hue + ",";
      }
      if (sat != -1) {
        result += "\"sat\":" + sat + ",";
      }
      if (transitiontime != -1) {
        result += "\"transitiontime\":" + transitiontime + ",";
      }
      if (alert != "") {
        result += "\"alert\":\"" + alert + "\",";
      }
      if (bri == 0) {
        result += "\"effect\":\"colorloop\",";
      }
      result = result.TrimEnd(',');
      result += "}";
      return result;
    }
    private String dropEffect(bool dropOn) {
      if (dropOn) {
        int dropColor = rnd.Next(1, 65535);
        //return "{\"on\": true, \"hue\":" + dropColor + ",\"bri\": 254, \"effect\":\"colorloop\",\"sat\":254,\"transitiontime\":0, \"alert\":\"select\"}";
        return "{\"alert\":\"select\"}";
      } else {
        return "{\"bri\":1,\"effect\":\"colorloop\",\"transitiontime\":2}";
      }
    }
    private String kickEffect(bool on) {
      if (on) {// used to be hue 300, now -1
        return jsonMake(254, 300, 254, 1, "none");
      } else {
        return jsonMake(1, 300, 254, 2, "none");
      }
    }
    private String snareEffect(bool on) {
      if (on) {
        // used to be hue 43000
        return jsonMake(254, 43000, 254, 1, "none");
      } else {
        return jsonMake(1, 43000, 254, 2, "none");
      }
    }
  }
}
