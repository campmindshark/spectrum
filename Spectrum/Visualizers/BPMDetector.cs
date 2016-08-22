using System;
using System.Collections.Generic;
using System.Linq;
using Spectrum.LEDs;
using Spectrum.Base;
using Spectrum.Hues;
using Spectrum.Audio;
using System.Threading;

namespace Spectrum
{

    class BPMDetector : Visualizer
    {

        private Configuration config;
        private AudioInput audio;

        private BPMDetect.BPMDetection bpmd; //http://adionsoft.net/bpm/index.php?module=docs

        public BPMDetector(
          Configuration config,
          AudioInput audio
        )
        {
            this.config = config;
            this.audio = audio;
            bpmd = new BPMDetect.BPMDetection();
        }

        public int Priority
        {
            get
            {
                return 1;
            }
        }

        // We don't actually care about this
        public bool Enabled { get; set; } = false;

        public Input[] GetInputs()
        {
            return new Input[] { this.audio };
        }

        public void Visualize()
        {
            this.process(this.audio.SampleData);
            Console.WriteLine(bpmd.getParameter(BPMDetect.BPMDetection.BPMParam.BPMFOUNDBPM));
        }

        // BPM Detector method use
        private void process(float[] samples)
        {
            if (samples.Length == 0)
            {
                return;
            }
            for (int i = 0; i < samples.Length; i++)
            {
                bpmd.AddSample(samples[i]);
            }
        }
    }
}