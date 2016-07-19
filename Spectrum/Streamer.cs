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
        public Visualizer visualizer;
        private ComboBox devicelist;
        private bool initialized;
        public bool controlLights = true;
        private int devindex;
        private float peakC = .800f;
        private float dropQ = .025f;
        private float dropT = .075f;
        private float kickQ = 1;
        private float kickT = 0;
        private float snareQ = 1;
        private float snareT = .5f;
        public bool lightsOff = false;
        public bool redAlert = false;
        public int brighten = 0;
        public int colorslide = 0;
        public int sat = 0;

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
                else
                {
                    BassWasapi.BASS_WASAPI_Free();
                    initialized = false;
                    devicelist.IsEnabled = true;
                }
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
            if (controlLights && !lightsOff && !redAlert)
            {
                visualizer.process(fft, BassWasapi.BASS_WASAPI_GetDeviceLevel(devindex, -1), peakC, dropQ, dropT, kickQ, kickT, snareQ, snareT);
            }
        }
        private void lightTimer_Tick(object sender)
        {
            visualizer.updateHues();
        }

        public void forceUpdate()
        {
            bool change = (visualizer.lightsOff != lightsOff) || (visualizer.redAlert != redAlert)
                || (visualizer.controlLights != controlLights) || (visualizer.brighten != brighten) ||
                (visualizer.colorslide != colorslide) || (visualizer.sat != sat);
            if (visualizer.controlLights)
            {
                visualizer.needupdate = 20;
            }
            else if (change)
            {
                visualizer.needupdate = 10;
            }
            visualizer.lightsOff = lightsOff;
            visualizer.redAlert = redAlert;
            visualizer.brighten = brighten;
            visualizer.colorslide = colorslide;
            visualizer.controlLights = controlLights;
            visualizer.sat = sat;
            if (!controlLights)
            {
                visualizer.silentMode = false;
            }
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

        public void updateConstants(String name, float val)
        {
            if (name == "controlLights")
            {
                if (val == 1)
                {
                    controlLights = true;
                } else
                {
                    controlLights = false;
                }
            }
            if (name == "peakChangeS")
            {
                peakC = val;
            }
            if (name == "dropQuietS")
            {
                dropQ = val;
            }
            if (name == "dropChangeS")
            {
                dropT = val;
            }
            if (name == "kickQuietS")
            {
                kickQ = val;
            }
            if (name == "kickChangeS")
            {
                kickT = val;
            }
            if (name == "snareQuietS")
            {
                snareQ = val;
            }
            if (name == "snareChangeS")
            {
                snareT = val;
            }
        }
    }
}
