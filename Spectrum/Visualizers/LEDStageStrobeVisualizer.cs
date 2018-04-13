using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Diagnostics;

namespace Spectrum
{

    class LEDStageStrobeVisualizer : Visualizer
    {

        private Configuration config;
        private LEDStageOutput stage;
        private Stopwatch stopwatch;

        public LEDStageStrobeVisualizer(
          Configuration config,
          LEDStageOutput stage
        )
        {
            this.config = config;
            this.stage = stage;
            this.stage.RegisterVisualizer(this);
            stopwatch = new Stopwatch();
            stopwatch.Start();
        }

        public int Priority
        {
            get
            {
                return 2;
            }
        }

        private bool enabled = false;
        public bool Enabled
        {
            get
            {
                return enabled;
            }
            set
            {
                if (value == enabled)
                {
                    return;
                }
                enabled = value;
            }
        }

        public Input[] GetInputs()
        {
            return new Input[] { };
        }

        public void Visualize()
        {
            if (stopwatch.ElapsedMilliseconds <= 10)
            {
                return;
            }
            stopwatch.Restart();

            int triangles = config.stageSideLengths.Length / 3;
            for (int i = 0; i < triangles; i++)
            {
                int tracerIndex = TracerLEDIndex(
                  config,
                  i
                );
                int triangleCounter = 0;
                for (int j = 0; j < 3; j++)
                {
                    for (
                      int k = 0;
                      k < config.stageSideLengths[i * 3 + j];
                      k++, triangleCounter++
                    )
                    {
                        int color = triangleCounter == tracerIndex
                          ? stage.GetSingleColor(0)
                          : stage.GetSingleColor(1);
                        for (int l = 0; l < 3; l++)
                        {
                            stage.SetPixel(i * 3 + j, k, l, color);
                        }
                    }
                }
            }
            stage.Flush();
        }

        public static int TracerLEDIndex(
          Configuration config,
          int triangleIndex
        )
        {
            double beatFactor = config.stageTracerSpeed / 3;
            double progress =
              config.beatBroadcaster.ProgressThroughBeat(beatFactor) * 3;
            int tracerLEDIndex;
            if (progress < 1.0)
            {
                tracerLEDIndex = (int)(
                  progress * config.stageSideLengths[triangleIndex * 3]
                );
            }
            else if (progress < 2.0)
            {
                tracerLEDIndex = (int)(
                  config.stageSideLengths[triangleIndex * 3] +
                  (progress - 1.0) * config.stageSideLengths[triangleIndex * 3 + 1]
                );
            }
            else
            {
                tracerLEDIndex = (int)(
                  config.stageSideLengths[triangleIndex * 3] +
                  config.stageSideLengths[triangleIndex * 3 + 1] +
                  (progress - 2.0) * config.stageSideLengths[triangleIndex * 3 + 2]
                );
            }
            return tracerLEDIndex;
        }

    }

}