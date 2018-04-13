//using Spectrum.Base;
//using Spectrum.LEDs;
//using System;
//using System.Diagnostics;

//namespace Spectrum
//{

//    class LEDStageSwagVisualizer : Visualizer
//    {

//        private Configuration config;
//        private LEDStageOutput stage;
//        private Stopwatch stopwatch;
//        private static int[,] perimeterKeySides = new int[,]{
//            { 1, 7, 6, 9, 11, 2 },
//            { 14, 23, 22, 16, 15, 12 },
//            { 25, 31, 30, 33, 35, 26 },
//            { 37, 43, 42, 45, 47, 38 } };


//        public LEDStageSwagVisualizer(
//          Configuration config,
//          LEDStageOutput stage
//        )
//        {
//            this.config = config;
//            this.stage = stage;
//            this.stage.RegisterVisualizer(this);
//            this.stopwatch = new Stopwatch();
//            this.stopwatch.Start();
//        }

//        public int Priority
//        {
//            get
//            {
//                return 1;
//            }
//        }

//        private bool enabled = false;
//        public bool Enabled
//        {
//            get
//            {
//                return this.enabled;
//            }
//            set
//            {
//                if (value == this.enabled)
//                {
//                    return;
//                }
//                this.enabled = value;
//            }
//        }

//        public static int[,] PerimeterKeySides => PerimeterKeySides1;

//        public static int[,] PerimeterKeySides1 => PerimeterKeySides2;

//        public static int[,] PerimeterKeySides2 => perimeterKeySides;

//        public static int[,] PerimeterKeySides3 => perimeterKeySides;

//        public Input[] GetInputs()
//        {
//            return new Input[] { };
//        }

//        public void Visualize()
//        {
//            if (this.stopwatch.ElapsedMilliseconds <= 10)
//            {
//                return;
//            }
//            this.stopwatch.Restart();

//            int triangles = this.config.stageSideLengths.Length / 3;
//            for (int i = 0; i < triangles; i++)
//            {
//                int tracerIndex = LEDStageTracerVisualizer.TracerLEDIndex(
//                  this.config,
//                  i
//                );
//                int triangleCounter = 0;
//                for (int j = 0; j < 3; j++)
//                {
//                    for (
//                      int k = 0;
//                      k < this.config.stageSideLengths[i * 3 + j];
//                      k++, triangleCounter++
//                    )
//                    {
//                        int color = triangleCounter == tracerIndex
//                          ? this.stage.GetSingleColor(0)
//                          : this.stage.GetSingleColor(1);
//                        for (int l = 0; l < 3; l++)
//                        {
//                            this.stage.SetPixel(i * 3 + j, k, l, color);
//                        }
//                    }
//                }
//            }
//            this.stage.Flush();
//        }

//        public static int SwagLEDIndex(
//          Configuration config,
//          int triforceIndex
//        )
//        {
//            double beatFactor = config.stageTracerSpeed / 6;

//            double progress =
//              config.beatBroadcaster.ProgressThroughBeat(beatFactor) * 6;
//            int tracerLEDIndex;
//            int triangleIndex;
//            int currentSide = (int)Math.Floor(progress);

//            int outerPerimeterKeyIndex = LEDStageSwagVisualizer.PerimeterKeySides1[triforceIndex, currentSide];

//            double progressThroughSide = progress - (double)currentSide;
//            return 0;
//            //tracerLEDIndex = (int)(
//            //    progressTh

//            //);
//            if (progress < 1.0)
//            {
//                tracerLEDIndex = (int)(
//                  progress * config.stageSideLengths[triforceIndex * 3]
//                );
//            }
//            else if (progress < 2.0)
//            {
//                tracerLEDIndex = (int)(
//                  config.stageSideLengths[triforceIndex * 3] +
//                  (progress - 1.0) * config.stageSideLengths[triforceIndex * 3 + 1]
//                );
//            }
//            else
//            {
//                tracerLEDIndex = (int)(
//                  config.stageSideLengths[triforceIndex * 3] +
//                  config.stageSideLengths[triforceIndex * 3 + 1] +
//                  (progress - 2.0) * config.stageSideLengths[triforceIndex * 3 + 2]
//                );
//            }
//            return tracerLEDIndex;
//        }

//    }

//}