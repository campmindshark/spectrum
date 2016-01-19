using System;
using System.Windows;
using System.Windows.Controls;
using System.Threading;
using Un4seen.Bass;
using Un4seen.BassWasapi;

namespace Spectrum
{
    public class Streamer
    {
        private bool enable;
        private Timer lightTimer;
        private Timer audioProcessTimer;
        private float[] fft;
        private WASAPIPROC process;     //to keep it from being garbage collected
        private Visualizer visualizer;
        private ComboBox devicelist;
        private bool initialized;
        private int devindex;

        public Streamer(ComboBox devicelist)
        {
            BassNet.Registration("larry.fenn@gmail.com", "2X531420152222");
            fft = new float[8192];
            process = new WASAPIPROC(Process);
            this.devicelist = devicelist;
            initialized = false;
            init();
        }
        
        public bool Enable
        {
            get { return enable; }
            set
            {
                enable = value;
                if (value)
                {
                    if (!initialized)
                    {
                        var str = (devicelist.Items[devicelist.SelectedIndex] as string);
                        var array = str.Split(' ');
                        devindex = Convert.ToInt32(array[0]);
                        bool result = BassWasapi.BASS_WASAPI_Init(devindex, 0, 0,
                                                                  BASSWASAPIInit.BASS_WASAPI_BUFFER,
                                                                  1f, 0,
                                                                  process, IntPtr.Zero);
                        if (!result)
                        {
                            var error = Bass.BASS_ErrorGetCode();
                            MessageBox.Show(error.ToString());
                        }
                        else
                        {
                            audioProcessTimer = new Timer(audioTimer_Tick, null, 100, 10);
                            lightTimer = new Timer(lightTimer_Tick, null, 100, 125); // Hue API limits 10/s light changes
                            initialized = true;
                            devicelist.IsEnabled = false;
                        }
                    }
                    BassWasapi.BASS_WASAPI_Start();
                    visualizer = new Visualizer();
                }
                else BassWasapi.BASS_WASAPI_Stop(true);
            }
        }
        
        private void init()
        {
            bool result = false;
            for (int i = 0; i < BassWasapi.BASS_WASAPI_GetDeviceCount(); i++)
            {
                var device = BassWasapi.BASS_WASAPI_GetDeviceInfo(i);
                if (device.IsEnabled && device.IsLoopback)
                {
                    devicelist.Items.Add(string.Format("{0} - {1}", i, device.name));
                }
            }
            devicelist.SelectedIndex = 0;
            Bass.BASS_SetConfig(BASSConfig.BASS_CONFIG_UPDATETHREADS, false);
            // 'No sound' device for double buffering: BASS_WASAPI_BUFFER flag requirement
            result = Bass.BASS_Init(0, 44100, BASSInit.BASS_DEVICE_DEFAULT, IntPtr.Zero);
            if (!result) throw new Exception("Init Error");
        }
        
        private void audioTimer_Tick(object sender)
        {
            // get fft data. Return value is -1 on error
            // type: 1/8192 of the channel sample rate (here, 44100 hz; so the bin size is roughly 2.69 Hz)
            int ret = BassWasapi.BASS_WASAPI_GetData(fft, (int)BASSData.BASS_DATA_FFT16384);
            visualizer.process(fft, BassWasapi.BASS_WASAPI_GetDeviceLevel(devindex, -1));
        }
        private void lightTimer_Tick(object sender)
        {
            visualizer.updateHues();
        }

        // WASAPI callback, required for continuous recording
        private int Process(IntPtr buffer, int length, IntPtr user)
        {
            return 1;
        }
        
        public void Free()
        {
            BassWasapi.BASS_WASAPI_Free();
            Bass.BASS_Free();
        }
    }
}
