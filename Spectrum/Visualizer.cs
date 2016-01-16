using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.Linq;

namespace Spectrum
{
    public class Visualizer
    {
        /**
        Sample points a_0, a_1, a_2... are defined at times t_0, t_1, t_2... with a window size
            derivatives are computed d_i := (a_{i+1} - a_i) / t_i
        **/
        
        private List<int> lights;
        private Random rnd;
        private String hubaddress;
        private int buckets;

        // Updates
        private String[] updates;
        private bool switchcolors;
        private bool readyflag;
        private bool cyclecolors;
        private bool lightChanged;

        private int[] binHits;
        private int[] cutoffs;

        // analysis variables
        private float lastlevel;
        private int lastUpdated;
        private double lastTrigger;

        // algorithm parameters
        private double repeatThreshold = 2; // threshold multiplier for repeating a light flash
        private double triggerDecay = .5; // decay multiplier for trigger level
        private double lightPersistence = 10;

        // debug quantities
        private List<double> timervariance;

        public Visualizer()
        {
            rnd = new Random();
            lights = new List<int>(); // these are the light addresses, as fetched from the hue hub, from left to right
            lights.Add(5);
            lights.Add(3);
            lights.Add(2);
            lights.Add(7);
            lights.Add(4);
            
            hubaddress = "http://192.168.1.26/api/23ef4e6e60bfc672548018214333a8b/lights/";

            lastlevel = 0;
            lastUpdated = 2;
            lastTrigger = 0;
            
            updates = new String[5];
            updates[0] = "";
            updates[1] = "";
            updates[2] = "";
            updates[3] = "";
            updates[4] = "";

            buckets = 5;
            binHits = new int[buckets];
            binHits[0] = 0;
            binHits[1] = 0;
            binHits[2] = 0;
            binHits[3] = 0;
            binHits[4] = 0;
            cutoffs = new int[buckets];
            cutoffs[0] = 0;
            // | 0th bin
            cutoffs[1] = 1;
            // | 1st bin
            cutoffs[2] = 2;
            // | 2nd bin
            cutoffs[3] = 3;
            // | 3rd bin
            cutoffs[4] = 4;
            // | 4th bin

            lightChanged = false;
            readyflag = true;

        }
        
        public void process(float[] spectrum, float level)
        {
            switchcolors = (level == 0 && lastlevel == 0);
            cyclecolors = true;
            lastlevel = level;
            if (level == 0)
            {
                binHits[0] = 0;
                binHits[1] = 0;
                binHits[2] = 0;
                binHits[3] = 0;
                binHits[4] = 0;
                return;
            }
            // debug code:
            // dump spectrum contents:
            // Console.WriteLine(String.Join(",", spectrum.Select(p => p.ToString()).ToArray()));
            //timervariance.Add(timer.ElapsedTicks - lastTime);
            //double average = timervariance.Average();
            //double sumOfSquaresOfDifferences = timervariance.Select(val => (val - average) * (val - average)).Sum();
            //double sd = Math.Sqrt(sumOfSquaresOfDifferences / timervariance.Count);
            //Console.WriteLine(sd);
            // code here to process signal.
            // relevant objects: spectrum[] is an array of 1024 buckets of 21.53 Hz each.
            // level is the volume level.
            // timer is a running timer that has timer.ElapsedTicks
            // rnd is a random number generator with rnd.Next(%)
            // Mode: Spectrogram
            // Motivation: Group together spectrum into 5 buckets (using some norm; maximum for now)
            // Use the 5 buckets as a brightness index for the lights
            // Lights should be arranged in some line across hue space.
            // First: Get the maximum of the five lights
            float overallMax = 0;
            int targetLight = 0;
            for (int i = 0; i < spectrum.Length; i++)
            {
                if (spectrum[i] > overallMax)
                {
                    float tempMax = spectrum[i];
                    targetLight = 0;
                    for (int j = 0; j < buckets; j++)
                    {
                        if (i > cutoffs[j])
                        {
                            targetLight = j;
                        }
                    }
                    if (targetLight != lastUpdated || tempMax > repeatThreshold*lastTrigger)
                    {
                        overallMax = spectrum[i];
                    }
                }
            }
            lastTrigger *= triggerDecay;
            updates[lastUpdated] = jsonMake(0, -1, -1, (int)(lightPersistence*lastlevel), "none");
            if(overallMax > lastTrigger)
            {
                //updates[targetLight] = jsonMake((int)(254 * overallMax), -1, -1, 1, "select");
                updates[targetLight] = jsonMake(-1, -1, -1, -1, "select");
                lastTrigger = overallMax;
                lastUpdated = targetLight;
                binHits[targetLight]++;
            }
            if (overallMax == 0)
            {
                updates[lastUpdated] = jsonMake(0, -1, -1, 0, "none");
            }
        }

        private String jsonMake(int bri, int hue, int sat, int transitiontime, String alert)
        {
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
            } else
            {
                if (switchcolors) {
                    result += "\"hue\":" + rnd.Next(65536) + ",";
                }
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
            result = result.TrimEnd(',');
            result += "}";
            return result;
        }

        private String laddressHelper(int address)
        {
            return address + "/state/";
        }
        
        public void updateHues()
        {
            if (switchcolors && readyflag)
            {
                Console.WriteLine("BANG");
                // randomly switch up all the lights
                for (int i = 0; i < 5; i++)
                {
                    new System.Net.WebClient().UploadStringAsync(new Uri(hubaddress + laddressHelper(lights[i])), "PUT", jsonMake(0, rnd.Next(65536), 254, -1, "none"));
                }
                readyflag = false;
                switchcolors = false;
            } else {
                for (int i = 0; i < 5; i++)
                {
                    if (updates[i] != "")
                    {
                        new System.Net.WebClient().UploadStringAsync(new Uri(hubaddress + laddressHelper(lights[i])), "PUT", updates[i]);
                        updates[i] = "";
                        readyflag = true;
                        lightChanged = true;
                    }
                }
            }
            //status1 = jsonMake(rnd.Next(255), rnd.Next(65536), rnd.Next(255), 0, "select");
            postUpdate();
        }

        // put stuff that takes time here; while lights update this will occur
        private void postUpdate()
        {
            if (lightChanged)
            {
                int mostHit = 0;
                int leastHit = 0;
                for (int i = 0; i < binHits.Length; i++)
                {
                    if (binHits[i] == binHits.Max())
                    {
                        mostHit = i;
                    }
                    if (binHits[i] == binHits.Min())
                    {
                        leastHit = i;
                    }
                }
                growBin(leastHit);
                shrinkBin(mostHit);
                fixCutoffs();
                Console.WriteLine(String.Join(",", binHits.Select(p => p.ToString()).ToArray()));
                Console.WriteLine(String.Join(",", cutoffs.Select(p => p.ToString()).ToArray()));
            }
        }
        private void growBin(int index)
        {
            if (index != 0 && index != (buckets-1))
            {
                cutoffs[index]--;
                if (cutoffs[index - 1] == cutoffs[index])
                {
                    cutoffs[index - 1]--;
                }
                cutoffs[index + 1]++;
            }
            if (index == 0)
            {
                cutoffs[index + 1]++;
            }
            if (index == (buckets-1))
            {
                cutoffs[index]--;
            }
        }
        private void shrinkBin(int index)
        {
            if (index != 0 && index != (buckets - 1))
            {
                if (index != 1)
                {
                    cutoffs[index]++;
                }
                cutoffs[index + 1]--;
            }
            if (index == 0)
            {
                cutoffs[1]--;
                cutoffs[2]--;
            }
            if (index == (buckets-1))
            {
                cutoffs[buckets - 1]+= 2;
                cutoffs[buckets - 2]++;
            }
        }
        private void fixCutoffs()
        {
            for (int i = 0; i < cutoffs.Length; i++)
            {
                if (cutoffs[i] < 0)
                {
                    cutoffs[i] = 0;
                    i = 1;
                }
                if (i != 0 && cutoffs[i] <= cutoffs[i - 1])
                {
                    cutoffs[i]++;
                    i = 1;
                }
            }
        }
    }
    /** old process code here
                level = (float)Math.Pow(level, p);
            if (level != lastlevel)
            {
                int elapsed = (int)timer.ElapsedTicks - lastTime;
                lastTime = (int)timer.ElapsedTicks;
                double gainNormalize = levelsHistory.Sum() / levelsHistory.Count;
                double bassNormalize = bassHistory.Sum() / bassHistory.Count;

                double currentBass = Math.Pow(spectrum[1], p) + Math.Pow(spectrum[2], p) + Math.Pow(spectrum[3], p) + Math.Pow(spectrum[4], p);
                if (currentBass > bassTol * bassNormalize)
                {
                    Console.WriteLine("bass hit" + rnd.Next(10));
                    long bassInterval = timer.ElapsedTicks - lastBassTime;
                    if (bassInterval > 4761905) // bass detection slower than 75 bpm
                    {
                        bassTol -= .01;
                    }
            // bass detection faster than 180 bpm
                    else if (bassInterval < 4761904) 
                    {
                        bassTol += .01;
                    }
                    lastBassTime = timer.ElapsedTicks;
                }

                if (level > 1.3 * gainNormalize / levelsHistory.Count) // consider instead of using a flat constant of using some envelope
                {
                    Console.WriteLine("kick" + rnd.Next(10));
                }

                levelsHistory.Add(Math.Pow(level, p));
                bassHistory.Add(currentBass);
                intervals.Add(elapsed);
                derivatives.Add((levelsHistory[intervals.Count - 1] - levelsHistory[intervals.Count - 2])/intervals[intervals.Count - 1]);
                if (derivatives[derivatives.Count - 1] > 0 && derivatives[derivatives.Count - 1] < beatTol) {
                    //Console.WriteLine("kick" + rnd.Next(10));
                }
                while (intervals.Sum() > analysisWindow)
                {
                    levelsHistory.RemoveAt(0);
                    intervals.RemoveAt(0);
                    derivatives.RemoveAt(0);
                }
                lastlevel = level;
                lastTime = (int)timer.ElapsedTicks;
            }
    **/
}
