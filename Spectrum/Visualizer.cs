using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.Linq;

namespace Spectrum
{
    public class Visualizer
    {
        private List<int> lights;
        private Random rnd;
        private String hubaddress;

        // Lighting updates and state flags
        private String update;
        private bool cyclecolors;
        private int targetLight;

        // FFT constants
        private Dictionary<String, double[]> bins;
        private Dictionary<String, float> hits;
        private Dictionary<String, float> prevHits;
        private Dictionary<String, float> diffs;
        private int buckets;
        private int[] binHits;

        // analysis/history variables
        private bool silence;
        private int silentCounter;
        private bool kickCounted;
        private bool snareCounted;
        private bool kickPending;
        private bool snarePending;
        
        // algorithm parameters
        private double repeatThreshold = 2; // threshold multiplier for repeating a light flash
        private double triggerDecay = .5; // decay multiplier for trigger level
        private double lightPersistence = 10; // multiplier for how long lights stay lit

        //debug
        private int processCount = 0;
        private double max = 0;

        public Visualizer()
        {
            bins = new Dictionary<String, double[]>();
            hits = new Dictionary<String, float>();
            prevHits = new Dictionary<String, float>();
            diffs = new Dictionary<String, float>();

            // ranges
            bins.Add("subbass", new double[] { 0, 60, 0 });
            bins.Add("bass", new double[] { 60, 250, 0 });
            bins.Add("midrange", new double[] { 250, 2000, 0 });
            bins.Add("highmids", new double[] { 2000, 6000, 0 });
            bins.Add("highfreqs", new double[] { 6000, 20000, 0 });

            // specific instruments
            bins.Add("kick", new double[] { 0, 100, .007 });
            bins.Add("snareattack", new double[] { 2500, 4000, .00033 });

            // initialize hits
            foreach (String band in bins.Keys)
            {
                hits.Add(band, 0);
                prevHits.Add(band, 0);
            }

            rnd = new Random();
            hubaddress = "http://192.168.1.26/api/23ef4e6e60bfc672548018214333a8b/lights/";
            lights = new List<int>(); // these are the light addresses, as fetched from the hue hub, from left to right
            lights.Add(5);
            lights.Add(3);
            lights.Add(2);
            lights.Add(7);
            lights.Add(4);

            targetLight = -1;
            silence = true;
            silentCounter = 0;

            update = "";

            buckets = 5;
            binHits = new int[buckets];
            binHits[0] = 0;
            binHits[1] = 0;
            binHits[2] = 0;
            binHits[3] = 0;
            binHits[4] = 0;
            cyclecolors = false;
            kickCounted = false;
            snareCounted = false;
            kickPending = false;
            snarePending = false;
        }
        
        public void process(float[] spectrum, float level)
        {
            processCount++;
            
            for (int i = 0; i < spectrum.Length; i++)
            {
                // scan over every detection band and count hits
                foreach (KeyValuePair<String, double[]> band in bins)
                {
                    String name = band.Key;
                    double[] window = band.Value;
                    if (windowContains(window, i))
                    {
                        hits[name]+= spectrum[i];
                    }
                }
            }
            silence = (level < .01) && silence;

            foreach (String band in hits.Keys.ToList())
            {
                diffs[band] = (hits[band] - prevHits[band]) / binWidth(band);
            }

            foreach (String band in hits.Keys.ToList())
            {
                prevHits[band] = hits[band];
                hits[band] = 0;
            }

            // postprocess logic
            if (diffs["kick"] < 0) kickCounted = false;
            if (diffs["snareattack"] < -.0002)
            {
                snareCounted = false;
            }
            if (diffs["snareattack"]> max) max = diffs["snareattack"];
            if (diffs["kick"] > bins["kick"][2] && !kickCounted)
            {
                Console.WriteLine(diffs["kick"]);
                kickCounted = true;
                kickPending = true;
            }
            if ((diffs["snareattack"]) > bins["snareattack"][2] && !snareCounted)
            {
                snareCounted = true;
                snarePending = true;
            }
            update = jsonMake(-1, rnd.Next(1, 65536), -1, 0, "none");
        }

        public void updateHues()
        {
            // highest priority: kick hit
            if (kickPending)
            {
                Console.WriteLine("kick");
                new System.Net.WebClient().UploadStringAsync(new Uri(hubaddress + laddressHelper(lights[0])), "PUT", update);
                kickPending = false;
            }
            else if (snarePending) // second highest priority: snare hit (?)
            {
                Console.WriteLine("snare");
                new System.Net.WebClient().UploadStringAsync(new Uri(hubaddress + laddressHelper(lights[4])), "PUT", update);
                snarePending = false;
            }
            max = 0;
            postUpdate();
        }

        // put stuff that takes time here; while lights update this will occur
        private void postUpdate()
        {
            processCount = 0;
            if (silence && silentCounter == 10)
            {
                Console.WriteLine("Silence detected.");
                silentCounter = 0;
            }
            else if (silence)
            {
                silentCounter++;
            }
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
            return address + "/state/";
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
            if (cyclecolors)
            {
                result += "\"effect\":\"colorloop\"" + ",";
            }
            else
            {
                result += "\"effect\":\"none\"" + ",";
            }
            result = result.TrimEnd(',');
            result += "}";
            return result;
        }
    }
}
