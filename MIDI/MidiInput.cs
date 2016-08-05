using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sanford.Multimedia.Midi;
using Spectrum.Base;
using System.Threading;

namespace Spectrum.MIDI {

  public class MidiInput : Input {

    Configuration config;
    InputDevice device;

    public MidiInput(Configuration config) {
      this.config = config;
    }

    private bool active;
    private Thread inputThread;
    private object lockObject = new object();
    public bool Active {
      get {
        lock (this.lockObject) {
          return this.active;
        }
      }
      set {
        lock (this.lockObject) {
          if (this.active == value) {
            return;
          }
          if (value) {
            if (this.config.midiInputInSeparateThread) {
              this.inputThread = new Thread(MidiProcessingThread);
              this.inputThread.Start();
            } else {
              this.InitializeMidi();
            }
          } else {
            if (this.inputThread != null) {
              this.inputThread.Abort();
              this.inputThread.Join();
              this.inputThread = null;
            } else {
              this.TerminateMidi();
            }
          }
          this.active = value;
        }
      }
    }

    public bool Enabled {
      get {
        return this.config.midiInputEnabled;
      }
    }

    private void InitializeMidi() {
      this.device = new InputDevice(this.config.midiDeviceIndex);
      this.device.ChannelMessageReceived += ChannelMessageReceived;
      this.device.StartRecording();
    }

    private void ChannelMessageReceived(
      object sender,
      ChannelMessageEventArgs e
    ) {
      System.Diagnostics.Debug.WriteLine("EVENT!!");
    }

    private void TerminateMidi() {
      this.device.StopRecording();
      this.device = null;
    }

    private void Update() {
      lock (this.lockObject) {
      }
    }

    private void MidiProcessingThread() {
      this.InitializeMidi();
      try {
        while (true) {
          this.Update();
        }
      } catch (ThreadAbortException) {
        this.TerminateMidi();
      }
    }

    public void OperatorUpdate() {
      if (!this.config.midiInputInSeparateThread) {
        this.Update();
      }
    }

    public static int DeviceCount {
      get {
        return InputDevice.DeviceCount;
      }
    }

    public static string GetDeviceName(int deviceIndex) {
      return InputDevice.GetDeviceCapabilities(deviceIndex).name;
    }

  }

}