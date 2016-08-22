/*===============================================================================================
 Adion's BPM Detection Library Example
 Copyright (c), adionSoft 2007.
 See http://adionsoft.net/bpm for more information
===============================================================================================*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

namespace BPMDetect
{
    public class BPMDetection
    {
        #region BPMDetectDLL_Declarations
        public enum BPMParam {
            BPMFOUNDBPM,
            BPMNROFBEATS,
            BPMBEATLOOP,
            BPMCURRENTBEATTIME,
            BPMRESET,
            BPMWAVELIST,
            BPM_RANGE_MINBPM,
            BPM_RANGE_MAXBPM
        }

        public enum BPMFFTParam {
            BPMFFT_WINDOWSIZE,
            BPMFFT_NBWINDOWS,
            BPMFFT_WINDOWTYPE
        }

        [DllImport("bpmDetect.dll")]
        private extern static IntPtr BPM_Create();
        [DllImport("bpmDetect.dll")]
        private extern static void BPM_Destroy(IntPtr bpm);
        [DllImport("bpmDetect.dll")]
        private extern static void BPM_AddSample(IntPtr bpm, float sample);
        [DllImport("bpmDetect.dll")]
        private extern static double BPM_getParameter(IntPtr bpm, BPMParam param);
        [DllImport("bpmDetect.dll")]
        private extern static void BPM_setParameter(IntPtr bpm, BPMParam param, double value);
        [DllImport("bpmDetect.dll")]
        private extern static double BPM_getFFTParameter(IntPtr bpm, BPMFFTParam param);
        [DllImport("bpmDetect.dll")]
        private extern static void BPM_setFFTParameter(IntPtr bpm, BPMFFTParam param, double value);
        [DllImport("bpmDetect.dll")]
        private extern static void BPM_Register(int[] code);

        #endregion

        private IntPtr bpm = IntPtr.Zero;

        public BPMDetection()
        {
            bpm = BPM_Create();
        }

        ~BPMDetection()
        {
            BPM_Destroy(bpm);
        }

        public void AddSample(float sample)
        {
            BPM_AddSample(bpm, sample);
        }

        public double getParameter(BPMParam param)
        {
            return BPM_getParameter(bpm, param);
        }

        public void setParameter(BPMParam param, double value)
        {
            BPM_setParameter(bpm, param, value);
        }

        public double getFFTParameter(BPMFFTParam param)
        {
            return BPM_getFFTParameter(bpm, param);
        }

        public void setFFTParameter(BPMFFTParam param, double value)
        {
            BPM_setFFTParameter(bpm, param, value);
        }

        public void reset()
        {
            BPM_setParameter(bpm, BPMParam.BPMRESET, 1.0);
        }

        public static void register(int[] code)
        {
            BPM_Register(code);
        }
    }
}
