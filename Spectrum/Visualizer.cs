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
        private List<double> levelsHistory;
        private List<double> bassHistory; // defined as all frequencies from 20 hz to 60 hz (i.e. fft[1, 2, 3])
        private List<int> intervals;
        private List<double> derivatives;
        private int primary;
        private int secondary;
        private String hubaddress;
        private String status1;
        private Random rnd = new Random();
        private Stopwatch timer;
        private int p; // Lp space exponent
        private long lastBassTime; // last bass detection time
        private double beatTol;
        private double bassTol;

        // analysis variables
        private int analysisWindow = 8000000; // ticks (10000 per millisecond) (this corresponds to 75 bpm, the longest possible window)
        private float lastlevel;
        private int lastTime;
        
        // musical property estimates
        private bool kick;
        private double bpm;

        public Visualizer()
        {
            lights = new List<int>(); // these are the light addresses, as fetched from the hue hub, from left to right
            lights.Add(5);
            lights.Add(3);
            lights.Add(2);
            lights.Add(7);
            lights.Add(4);

            levelsHistory = new List<double>();
            bassHistory = new List<double>();
            intervals = new List<int>();
            derivatives = new List<double>();

            // necessary to support differentation & a cheap hack to avoid out of bounds errors on long startup times
            bassHistory.Add(0);
            levelsHistory.Add(0);
            intervals.Add(1);

            beatTol = .1;
            lastBassTime = 0;
            bassTol = .1;
            bpm = 126;

            p = 2;

            primary = lights[rnd.Next(4)];
            secondary = lights[rnd.Next(4)];
            hubaddress = "http://192.168.1.26/api/23ef4e6e60bfc672548018214333a8b/lights/";
            timer = new Stopwatch();
            timer.Start();

            lastlevel = 0;

            kick = false;

        }
        
        public void process(float[] spectrum, float level)
        {
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
                    else if (bassInterval < 4761904) // bass detection faster than 180 bpm
                    {
                        bassTol += .01;
                    }
                    lastBassTime = timer.ElapsedTicks;
                }

                if (level > 1.3 * gainNormalize / levelsHistory.Count) // consider instead of using a flat constant of using some envelope
                {
                    //Console.WriteLine("kick" + rnd.Next(10));
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
        }
        
        private String jsonMake(int bri, int hue, int sat, int transitiontime, String alert)
        {
            return "{\"bri\":" + bri + ",\"hue\":" + hue + ",\"sat\":" + sat + ",\"transitiontime\":" + transitiontime + ",\"alert\":\"" + alert + "\"}";
        }

        private String laddressHelper(int address)
        {
            return address + "/state/";
        }
        
        public void updateHues()
        {
            status1 = jsonMake(rnd.Next(255), rnd.Next(65536), rnd.Next(255), 0, "select");
            // Console.WriteLine(status1);
            //new System.Net.WebClient().UploadStringAsync(new Uri(hubaddress + laddressHelper(primary)), "PUT",status1);
        }
    }
}
