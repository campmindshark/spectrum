using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sanford.Multimedia.Midi;
using Spectrum.Base;
using System.Threading;
using System.Collections.Concurrent;

namespace Spectrum.MIDI {

  public enum MidiCommandType : byte { Knob, Note }

  public struct MidiCommand {
    public MidiCommandType type;
    public int index;
    public double value;
  }

  public class MidiInput : Input {

    private Configuration config;
    private InputDevice device;
    private ConcurrentQueue<MidiCommand> buffer;
    private Dictionary<int, double> knobValues;
    private Dictionary<int, double> noteVelocities;
    private MidiCommand[] commandsSinceLastTick;

    public MidiInput(Configuration config) {
      this.config = config;
      this.buffer = new ConcurrentQueue<MidiCommand>();
      this.knobValues = new Dictionary<int, double>();
      this.noteVelocities = new Dictionary<int, double>();
      this.commandsSinceLastTick = new MidiCommand[0];
    }

    private bool active;
    private Thread inputThread;
    public bool Active {
      get {
        lock (this.buffer) {
          return this.active;
        }
      }
      set {
        lock (this.buffer) {
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
      System.Diagnostics.Debug.WriteLine(
        "MIDI channel message on channel " + e.Message.MidiChannel +
        " with command " + e.Message.Command +
        ", data1 " + e.Message.Data1 +
        ", data2 " + e.Message.Data2
      );
      if (e.Message.Command == ChannelCommand.Controller) {
        double value = (double)e.Message.Data2 / 127;
        this.knobValues[e.Message.Data1] = value;
        this.buffer.Enqueue(new MidiCommand() {
          type = MidiCommandType.Knob,
          index = e.Message.Data1,
          value = value,
        });
      } else if (
        e.Message.Command == ChannelCommand.NoteOn ||
        e.Message.Command == ChannelCommand.NoteOff
      ) {
        double value = (double)e.Message.Data2 / 127;
        this.noteVelocities[e.Message.Data1] = value;
        this.buffer.Enqueue(new MidiCommand() {
          type = MidiCommandType.Note,
          index = e.Message.Data1,
          value = value,
        });
      }
    }

    private void TerminateMidi() {
      this.device.StopRecording();
      this.device.Dispose();
      this.device = null;
    }

    private void Update() {
      lock (this.buffer) {
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

      int numMessages = this.buffer.Count;
      if (numMessages == 0) {
        return;
      }

      var commands = new MidiCommand[numMessages];
      for (int i = 0; i < numMessages; i++) {
        bool result = this.buffer.TryDequeue(out commands[i]);
        if (!result) {
          throw new System.Exception("Someone else is dequeueing!");
        }
      }

      this.commandsSinceLastTick = commands;
    }

    public static int DeviceCount {
      get {
        return InputDevice.DeviceCount;
      }
    }

    public static string GetDeviceName(int deviceIndex) {
      return InputDevice.GetDeviceCapabilities(deviceIndex).name;
    }

    public double GetKnobValue(int knob) {
      if (!this.knobValues.ContainsKey(knob)) {
        return -1.0;
      }
      return this.knobValues[knob];
    }

    public double GetNoteVelocity(int note) {
      if (!this.noteVelocities.ContainsKey(note)) {
        return -1.0;
      }
      return this.noteVelocities[note];
    }

    public MidiCommand[] GetCommandsSinceLastTick() {
      return this.commandsSinceLastTick;
    }

  }

}