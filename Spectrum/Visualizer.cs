using System;
using System.Collections.Generic;
using System.Linq;

namespace Spectrum
{
    public class Visualizer
    {
        private List<int> lights;
        private Random rnd;
        private String hubaddress = "http://192.168.1.26/api/23ef4e6e60bfc672548018214333a8b/";

        // FFT dicts
        private Dictionary<String, double[]> bins;
        private Dictionary<String, float[]> energyHistory;
        private Dictionary<String, float> energyLevels;
        private int historyLength = 16;
        private int processCount;

        // analysis/history variables
        private bool silence = true;
        private int silentCounter = 0;
        private bool silentMode = true;
        private int silentModeHueIndex = 0;
        private int silentModeLightIndex = 0;
        private bool kickCounted = false;
        private bool snareCounted = false;
        private bool kickPending = false;
        private bool snarePending = false;
        private int idleCounter = 0;
        private bool lightPending = false;
        private bool drop = false;
        private int dropDuration = 0;
        private int target = 0;
        
        public Visualizer()
        {
            rnd = new Random();
            bins = new Dictionary<String, double[]>();
            energyHistory = new Dictionary<String, float[]>();
            energyLevels = new Dictionary<String, float>();

            // frequency detection bands
            // format: { bottom freq, top freq, activation level (delta)}
            bins.Add("midrange", new double[] { 250, 2000, .025 });
            bins.Add("total", new double[] { 60, 2000, .05 });
            // specific instruments
            bins.Add("kick", new double[] { 40, 50, .01});
            bins.Add("snareattack", new double[] { 2500, 3000, .001});
            foreach (String band in bins.Keys)
            {
                energyLevels.Add(band, 0);
                energyHistory.Add(band, Enumerable.Repeat((float)0, historyLength).ToArray());
            }

            lights = new List<int>(); // these are the light addresses, as fetched from the hue hub, from left to right
            lights.Add(5);
            lights.Add(3);
            lights.Add(2);
            lights.Add(7);
            lights.Add(4);
            // in the future use the API itself to get the light IDs correctly... also set up the "all lights" group automatically
        }
        
        public void process(float[] spectrum, float level)
        {
            processCount++;
            processCount = processCount % historyLength;
            for (int i = 1; i < spectrum.Length/2; i++)
            {
                foreach (KeyValuePair<String, double[]> band in bins)
                {
                    String name = band.Key;
                    double[] window = band.Value;
                    if (windowContains(window, i))
                    {
                        energyLevels[name] += (spectrum[i] * spectrum[i]);
                    }
                }
            }
            foreach (String band in energyHistory.Keys.ToList())
            {
                float current = energyLevels[band];
                float[] history = energyHistory[band];
                float previous = history[(processCount + historyLength - 1) % historyLength];
                float change = current - previous;
                float avg = history.Average();
                float ssd = history.Select(val => (val - avg) * (val - avg)).Sum();
                float sd = (float)Math.Sqrt(ssd / historyLength);
                float threshold = (float)bins[band][2];
                bool signal = change > threshold;

                if (band == "total")
                {
                    if (current > avg + 2*sd && avg < .08 && signal && sd < .03)
                    {
                        drop = true;
                    }
                }
                if (band == "kick")
                {
                    if (current < avg)
                    {
                        kickCounted = false;
                    }
                    if (signal && current > avg + 2 * sd && avg < .1 && !kickCounted)
                    {

                        kickCounted = true;
                        kickPending = true;
                    }
                }
                if (band == "snareattack")
                {
                    if (current < avg)
                    {
                        snareCounted = false;
                    }
                    if (signal && current > avg + 2 * sd && avg < .1 && !snareCounted)
                    {
                        snareCounted = true;
                        snarePending = true;
                    }
                }
            }
            foreach (String band in energyHistory.Keys.ToList())
            {
                energyHistory[band][processCount] = energyLevels[band];
                energyLevels[band] = 0;
            }
            silence = (level < .01) && silence;
        }

        public void updateHues()
        {
            if (!lightPending)
            {
                target = rnd.Next(5);
            }
            if (silentMode)
            {
                silentModeLightIndex = (silentModeLightIndex + 1) % 5;
                new System.Net.WebClient().UploadStringAsync(new Uri(hubaddress + laddressHelper(lights[silentModeLightIndex])), "PUT", silent(silentModeHueIndex));
                silentModeHueIndex = (silentModeHueIndex + 10000) % 65535;
            }
            else if (drop)
            {
                if (dropDuration == 0)
                {
                    Console.WriteLine("dropOn");
                    new System.Net.WebClient().UploadStringAsync(new Uri(hubaddress + "groups/2/action/"), "PUT", dropEffect(true));
                }
                else if (dropDuration == 1)
                {
                    new System.Net.WebClient().UploadStringAsync(new Uri(hubaddress + "groups/2/action/"), "PUT", dropEffect(false));
                }
                else if (dropDuration > 9)
                {
                    Console.WriteLine("dropOff");
                    drop = false;
                    dropDuration = -1;
                }
                dropDuration++;
            }
            else if (kickPending)
            {
                if (lightPending)
                {
                    new System.Net.WebClient().UploadStringAsync(new Uri(hubaddress + laddressHelper(lights[target])), "PUT", kickEffect(false));
                    lightPending = false;
                    kickPending = false;
                }
                else
                {
                    lightPending = true;
                    Console.WriteLine("kickOn");
                    new System.Net.WebClient().UploadStringAsync(new Uri(hubaddress + laddressHelper(lights[target])), "PUT", kickEffect(true));
                }
            }
            else if (snarePending) // second highest priority: snare hit (?)
            {
                if (lightPending)
                {
                    new System.Net.WebClient().UploadStringAsync(new Uri(hubaddress + laddressHelper(lights[target])), "PUT", snareEffect(false));
                    snarePending = false;
                    lightPending = false;
                }
                else
                {
                    lightPending = true;
                    Console.WriteLine("snareOn");
                    new System.Net.WebClient().UploadStringAsync(new Uri(hubaddress + laddressHelper(lights[target])), "PUT", snareEffect(true));
                }
            }
            else
            {
                idleCounter++;
                if (idleCounter > 4)
                {
                    new System.Net.WebClient().UploadStringAsync(new Uri(hubaddress + laddressHelper(lights[target])), "PUT", idle());
                    idleCounter = 0;
                }
            }
            postUpdate();
        }

        private void postUpdate()
        {
            if (silence && silentCounter == 24 && !silentMode)
            {
                Console.WriteLine("Silence detected.");
                silentMode = true;
            }
            else if (silence)
            {
                silentCounter++;
            }
            if (!silence)
            {
                silentCounter = 0;
                silentMode = false;
            }
            // this will be changed in process() UNLESS level < .1 for the duration of process()
            silence = true;
        }
        private bool windowContains(double[] window, int index)
        {
            return (freqToFFTBin(window[0]) <= index && freqToFFTBin(window[1]) >= index);
        }
        private int freqToFFTBin(double freq)
        {
            return (int)(freq / 2.69);
        }
        private int binWidth(String bin)
        {
            double[] window = bins[bin];
            return freqToFFTBin(window[1]) - freqToFFTBin(window[0]);
        }
        private String laddressHelper(int address)
        {
            return "lights/" + address + "/state/";
        }
        private String jsonMake(int bri, int hue, int sat, int transitiontime, String alert)
        {
            // bri: 1-254 brightness, -1 to do nothing, 0 to turn off
            // hue: 0-65535 hue, actual color reproduction hardware-dependent
            // sat: 0-254 saturation, 0 white
            // transitiontime: ms, -1 to use defaults
            // alert: none - no effect
            //        select - a breath cycle (aka flash)
            //        lselect - breath cycles for 15 seconds
            String result = "{";
            if (bri == 0)
            {
                result += "\"on\":" + "false" + ",";
            }
            else
            {
                result += "\"on\":" + "true" + ",";
            }
            if (bri != -1)
            {
                result += "\"bri\":" + bri + ",";
            }
            if (hue != -1)
            {
                result += "\"hue\":" + hue + ",";
            }
            if (sat != -1)
            {
                result += "\"sat\":" + sat + ",";
            }
            if (transitiontime != -1)
            {
                result += "\"transitiontime\":" + transitiontime + ",";
            }
            if (alert != "")
            {
                result += "\"alert\":\"" + alert + "\",";
            }
            result = result.TrimEnd(',');
            result += "}";
            return result;
        }
        private String dropEffect(bool dropOn)
        {
            if (dropOn)
            {
                return "{\"on\": true, \"hue\":" + rnd.Next(1, 65535) + ",\"bri\": 254, \"effect\":\"colorloop\",\"sat\":254,\"transitiontime\":0}";
            }
            else
            {
                return "{\"on\": true, \"bri\":1,\"effect\":\"colorloop\",\"sat\":254,\"transitiontime\":4}";
            }
        }
        private String kickEffect(bool on)
        {
            if (on)
            {
                return jsonMake(254, 300, 254, 1, "none");
            }
            else
            {
                return jsonMake(1, 300, 254, 2, "none");
            }
        }
        private String snareEffect(bool on)
        {
            if (on)
            {
                return jsonMake(254, 43000, 254, 1, "none");
            }
            else
            {
                return jsonMake(1, 43000, 254, 2, "none");
            }
        }
        private String idle()
        {
            return jsonMake(0, -1, 254, 20, "none");
        }
        private String silent(int index)
        {
            return "{\"on\": true,\"hue\":" + (index + 1) + ",\"effect\":\"none\",\"bri\":1,\"sat\":254,\"transitiontime\":10}";
        }
    }
}
