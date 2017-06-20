using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sanford.Multimedia.Midi;
using Spectrum.Base;
using System.Threading;
using System.Collections.Concurrent;
using System.ComponentModel;

namespace Spectrum.MIDI {

  using BindingKey = Tuple<MidiCommandType, int>;
  using InnerBindingKey = Tuple<int, MidiCommandType, int>;

  public struct MidiCommand {
    public int deviceIndex;
    public MidiCommandType type;
    public int index;
    public double value;
  }

  public class MidiInput : Input {

    // Each key on the keyboard corresponds to a color
    private static int[] colorFromColorIndex = new int[] {
      0x000000, 0xFF0000, 0xFF3232, 0xFE00FF, 0xFD32FF, 0xFD54FF, 0xA100FF,
      0xA432FF, 0xA954FF, 0x0055FF, 0x3262FF, 0x00D5FF, 0x33D9FF, 0x54DEFF,
      0x00FFB9, 0x33FFBA, 0x39FF00, 0x50FF34, 0xE6FF00, 0xE8FF34, 0xFFD300,
      0xFFD334, 0xFF7100, 0xFF7834, 0xFFFFFF,
      /*0x000000, 0xFF0000, 0xFF4400, 0xFF8800, 0xFFCC00, 0xFFFF00, 0xCCFF00,
      0x88FF00, 0x44FF00, 0x00FF00, 0x00FF44, 0x00FF88, 0x00FFCC, 0x00FFFF,
      0x00CCFF, 0x0088FF, 0x0044FF, 0x0000FF, 0x4400FF, 0x8800FF, 0xCC00FF,
      0xFF00FF, 0xFF55FF, 0xFFABFF, 0xFFFFFF,*/
    };

    private Configuration config;
    private Dictionary<int, InputDevice> devices;
    private ConcurrentQueue<MidiCommand> buffer;
    private Dictionary<int, Dictionary<int, double>> knobValues;
    private Dictionary<int, Dictionary<int, double>> noteVelocities;
    private MidiCommand[] commandsSinceLastTick;
    private Dictionary<InnerBindingKey, List<Binding>> bindings;

    public MidiInput(Configuration config) {
      this.config = config;
      this.buffer = new ConcurrentQueue<MidiCommand>();
      this.knobValues = new Dictionary<int, Dictionary<int, double>>();
      this.noteVelocities = new Dictionary<int, Dictionary<int, double>>();
      this.commandsSinceLastTick = new MidiCommand[0];
      this.SetBindings();
      this.config.PropertyChanged += ConfigUpdated;
    }

    private void ConfigUpdated(object sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == "midiDevices" || e.PropertyName == "midiPresets") {
        this.SetBindings();
      }
    }

    private void SetBindings() {
      this.bindings = new Dictionary<InnerBindingKey, List<Binding>>();

      foreach (int deviceIndex in this.config.midiDevices.Keys) {
        if (!this.noteVelocities.ContainsKey(deviceIndex)) {
          this.noteVelocities[deviceIndex] = new Dictionary<int, double>();
        }
        if (!this.knobValues.ContainsKey(deviceIndex)) {
          this.knobValues[deviceIndex] = new Dictionary<int, double>();
        }
        this.AddBinding(
          new Binding() {
            key = new BindingKey(MidiCommandType.Note, -1),
            callback = (index, val) => this.noteVelocities[deviceIndex][index] = val,
          },
          deviceIndex
        );
        this.AddBinding(
          new Binding() {
            key = new BindingKey(MidiCommandType.Knob, -1),
            callback = (index, val) => this.knobValues[deviceIndex][index] = val,
          },
          deviceIndex
        );
      }

      var activePresets = this.config.midiDevices.ToDictionary(
        (pair) => pair.Key,
        (pair) => this.config.midiPresets[pair.Value]
      );
      foreach (var pair in activePresets) {
        foreach (IMidiBindingConfig config in pair.Value.Bindings) {
          Binding[] bindings = config.GetBindings(this.config);
          foreach (Binding binding in bindings) {
            this.AddBinding(binding, pair.Key);
          }
        }
      }
    }

    private void AddBinding(Binding binding, int deviceIndex) {
      var innerBindingKey = new InnerBindingKey(
        deviceIndex,
        binding.key.Item1,
        binding.key.Item2
      );
      if (!this.bindings.ContainsKey(innerBindingKey)) {
        this.bindings.Add(innerBindingKey, new List<Binding>());
      }
      this.bindings[innerBindingKey].Add(binding);
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

    public bool AlwaysActive {
      get {
        return true;
      }
    }

    public bool Enabled {
      get {
        return this.config.midiInputEnabled;
      }
    }

    private void InitializeMidi() {
      this.devices = new Dictionary<int, InputDevice>();
      foreach (var pair in this.config.midiDevices) {
        var device = new InputDevice(pair.Key);
        device.ChannelMessageReceived +=
          (sender, e) => ChannelMessageReceived(pair.Key, sender, e);
        device.StartRecording();
        this.devices[pair.Key] = device;
      }
    }

    private void ChannelMessageReceived(
      int deviceIndex,
      object sender,
      ChannelMessageEventArgs e
    ) {
      //System.Diagnostics.Debug.WriteLine(
      //  "MIDI channel message on channel " + e.Message.MidiChannel +
      //  " with command " + e.Message.Command +
      //  ", data1 " + e.Message.Data1 +
      //  ", data2 " + e.Message.Data2
      //);
      MidiCommand command;
      if (e.Message.Command == ChannelCommand.Controller) {
        double value = (double)e.Message.Data2 / 127;
        command = new MidiCommand() {
          deviceIndex = deviceIndex,
          type = MidiCommandType.Knob,
          index = e.Message.Data1,
          value = value,
        };
      } else if (
        e.Message.Command == ChannelCommand.NoteOn ||
        e.Message.Command == ChannelCommand.NoteOff
      ) {
        double value = (double)e.Message.Data2 / 127;
        command = new MidiCommand() {
          deviceIndex = deviceIndex,
          type = MidiCommandType.Note,
          index = e.Message.Data1,
          value = value,
        };
      } else if (e.Message.Command == ChannelCommand.ProgramChange) {
        command = new MidiCommand() {
          deviceIndex = deviceIndex,
          type = MidiCommandType.Program,
          index = e.Message.Data1,
        };
      } else {
        return;
      }
      this.buffer.Enqueue(command);

      List<Binding> triggered = new List<Binding>();
      var genericKey = new InnerBindingKey(deviceIndex, command.type, -1);
      if (this.bindings.ContainsKey(genericKey)) {
        triggered.AddRange(this.bindings[genericKey]);
      }
      var key = new InnerBindingKey(deviceIndex, command.type, command.index);
      if (this.bindings.ContainsKey(key)) {
        triggered.AddRange(this.bindings[key]);
      }
      foreach (Binding binding in triggered) {
        binding.callback(command.index, command.value);
      }
    }

    private void TerminateMidi() {
      foreach (var pair in this.devices) {
        pair.Value.StopRecording();
        pair.Value.Dispose();
      }
      this.devices = null;
    }

    private void Update() {
      //lock (this.buffer) {
      //}
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

    public double GetKnobValue(int deviceIndex, int knob) {
      if (
        !this.knobValues.ContainsKey(deviceIndex) ||
        !this.knobValues[deviceIndex].ContainsKey(knob)
      ) {
        return -1.0;
      }
      return this.knobValues[deviceIndex][knob];
    }

    public double GetNoteVelocity(int deviceIndex, int note) {
      if (
        !this.noteVelocities.ContainsKey(deviceIndex) ||
        !this.noteVelocities[deviceIndex].ContainsKey(note)
      ) {
        return 0.0;
      }
      return this.noteVelocities[deviceIndex][note];
    }

    public MidiCommand[] GetCommandsSinceLastTick() {
      return this.commandsSinceLastTick;
    }

  }

}