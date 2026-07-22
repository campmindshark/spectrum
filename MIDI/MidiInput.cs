using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sanford.Multimedia.Midi;
using Spectrum.Base;
using System.Threading;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Spectrum.MIDI {

  using BindingKey = Tuple<MidiCommandType, int>;
  using InnerBindingKey = Tuple<int, MidiCommandType, int>;

  public class MidiInput : IMidiControlInput {

    // Each key on the keyboard corresponds to a color
    private static readonly int[] colorFromColorIndex = new int[] {
      0x000000, 0xFF0000, 0xFF3232, 0xFE00FF, 0xFD32FF, 0xFD54FF, 0xA100FF,
      0xA432FF, 0xA954FF, 0x0055FF, 0x3262FF, 0x00D5FF, 0x33D9FF, 0x54DEFF,
      0x00FFB9, 0x33FFBA, 0x39FF00, 0x50FF34, 0xE6FF00, 0xE8FF34, 0xFFD300,
      0xFFD334, 0xFF7100, 0xFF7834, 0xFFFFFF,
      /*0x000000, 0xFF0000, 0xFF4400, 0xFF8800, 0xFFCC00, 0xFFFF00, 0xCCFF00,
      0x88FF00, 0x44FF00, 0x00FF00, 0x00FF44, 0x00FF88, 0x00FFCC, 0x00FFFF,
      0x00CCFF, 0x0088FF, 0x0044FF, 0x0000FF, 0x4400FF, 0x8800FF, 0xCC00FF,
      0xFF00FF, 0xFF55FF, 0xFFABFF, 0xFFFFFF,*/
    };

    private readonly Configuration config;
    private readonly IRuntimeSettingsConfiguration runtimeSettings;
    // The live tempo service, needed by tap-tempo/ADSR bindings (owned by the
    // Operator, not part of Configuration).
    private readonly BeatBroadcaster beat;
    private readonly ApplicationStateDispatcher stateDispatcher;
    private readonly bool connectHardware;
    private Dictionary<int, InputDevice> devices;
    private long appliedDeviceGeneration = -1;
    public long AppliedDeviceGeneration =>
      Volatile.Read(ref this.appliedDeviceGeneration);
    private readonly ConcurrentQueue<MidiCommand> buffer;
    // Latest-value state has one deliberately chosen owner lock: driver
    // callbacks write under it and the operator/visualizers read under it.
    private readonly object midiStateLock = new object();
    private readonly Dictionary<int, Dictionary<int, double>> knobValues;
    private readonly Dictionary<int, Dictionary<int, double>> noteVelocities;
    private MidiCommand[] commandsSinceLastTick;
    // Callbacks capture exactly one fully compiled generation. SetBindings
    // never publishes the mutable builder it uses during compilation.
    private ImmutableDictionary<InnerBindingKey, ImmutableArray<Binding>>
      bindings = ImmutableDictionary<
        InnerBindingKey, ImmutableArray<Binding>>.Empty;

    // The rolling log of triggered bindings, owned here (its writer) and shown
    // by the VJ HUD; it raises its own PropertyChanged on append.
    public ObservableMidiLog MidiLog { get; } = new ObservableMidiLog();

    public MidiInput(
      Configuration config,
      BeatBroadcaster beat,
      ApplicationStateDispatcher stateDispatcher
    ) : this(config, beat, stateDispatcher, true) {
    }

    // The internal disconnected path is used only by Spectrum's integrated
    // operator harness. It keeps binding/device-generation reconciliation live
    // while replacing Sanford device handles with an empty in-memory set.
    internal MidiInput(
      Configuration config,
      BeatBroadcaster beat,
      ApplicationStateDispatcher stateDispatcher,
      bool connectHardware
    ) {
      this.config = config;
      this.runtimeSettings = config as IRuntimeSettingsConfiguration ??
        throw new ArgumentException(
          "MidiInput requires immutable runtime settings.", nameof(config));
      this.beat = beat;
      this.stateDispatcher = stateDispatcher ??
        throw new ArgumentNullException(nameof(stateDispatcher));
      this.connectHardware = connectHardware;
      this.buffer = new ConcurrentQueue<MidiCommand>();
      this.knobValues = new Dictionary<int, Dictionary<int, double>>();
      this.noteVelocities = new Dictionary<int, Dictionary<int, double>>();
      this.commandsSinceLastTick = new MidiCommand[0];
      this.SetBindings();
      this.config.PropertyChanged += ConfigUpdated;
    }

    private void ConfigUpdated(object sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == nameof(this.config.midiDevices) ||
          e.PropertyName == nameof(this.config.midiPresets)) {
        this.SetBindings();
      }
    }

    private void SetBindings() {
      MidiSettingsSnapshot settings =
        this.runtimeSettings.MidiSettingsSnapshot;
      var nextBindings =
        new Dictionary<InnerBindingKey, List<Binding>>();
      KeyValuePair<int, int>[] configuredDevices =
        settings.Devices.ToArray();

      foreach (KeyValuePair<int, int> configuredDevice in configuredDevices) {
        int deviceIndex = configuredDevice.Key;
        lock (this.midiStateLock) {
          if (!this.noteVelocities.ContainsKey(deviceIndex)) {
            this.noteVelocities[deviceIndex] = new Dictionary<int, double>();
          }
          if (!this.knobValues.ContainsKey(deviceIndex)) {
            this.knobValues[deviceIndex] = new Dictionary<int, double>();
          }
        }
        AddBinding(
          nextBindings,
          new Binding() {
            key = new BindingKey(MidiCommandType.Note, -1),
            callback = (index, val) => {
              lock (this.midiStateLock) {
                this.noteVelocities[deviceIndex][index] = val;
              }
              return new BindingInvocation(null);
            },
          },
          deviceIndex
        );
        AddBinding(
          nextBindings,
          new Binding() {
            key = new BindingKey(MidiCommandType.Knob, -1),
            callback = (index, val) => {
              lock (this.midiStateLock) {
                this.knobValues[deviceIndex][index] = val;
              }
              return new BindingInvocation(null);
            },
          },
          deviceIndex
        );
      }

      ImmutableDictionary<int, MidiPreset> presets = settings.Presets;
      foreach (KeyValuePair<int, int> pair in configuredDevices) {
        if (!presets.TryGetValue(pair.Value, out MidiPreset preset) ||
            preset?.Bindings == null) {
          this.MidiLog.Append(
            "MIDI device " + pair.Key + " references missing preset " +
            pair.Value + "; bindings skipped");
          continue;
        }
        foreach (IMidiBindingConfig bindingConfig in preset.Bindings) {
          Binding[] compiledBindings;
          try {
            if (bindingConfig == null) {
              throw new InvalidOperationException(
                "preset contains an empty binding");
            }
            compiledBindings = bindingConfig.GetBindings(
              this.config, this.beat, this.stateDispatcher);
          } catch (Exception error) {
            this.MidiLog.Append(
              "Binding \"" +
              (bindingConfig?.BindingName ?? "unnamed") +
              "\" skipped: " + error.Message);
            continue;
          }
          foreach (Binding binding in compiledBindings) {
            AddBinding(nextBindings, binding, pair.Key);
          }
        }
      }

      ImmutableDictionary<InnerBindingKey, ImmutableArray<Binding>> published =
        nextBindings.ToImmutableDictionary(
          pair => pair.Key,
          pair => pair.Value.ToImmutableArray());
      Volatile.Write(ref this.bindings, published);
    }

    private static void AddBinding(
      Dictionary<InnerBindingKey, List<Binding>> target,
      Binding binding,
      int deviceIndex
    ) {
      var innerBindingKey = new InnerBindingKey(
        deviceIndex,
        binding.key.Item1,
        binding.key.Item2
      );
      if (!target.TryGetValue(
          innerBindingKey, out List<Binding> keyBindings)) {
        keyBindings = new List<Binding>();
        target.Add(innerBindingKey, keyBindings);
      }
      keyBindings.Add(binding);
    }

    private bool active;
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
            // Sanford owns the callback threads; the operator owns device-set
            // reconciliation.
            this.InitializeMidi(
              this.runtimeSettings.MidiSettingsSnapshot);
          } else {
            this.TerminateMidi();
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
        return this.runtimeSettings.MidiSettingsSnapshot.Enabled;
      }
    }

    private void InitializeMidi(MidiSettingsSnapshot settings) {
      this.devices = new Dictionary<int, InputDevice>();
      this.appliedDeviceGeneration = settings.DeviceGeneration;
      if (!this.connectHardware) {
        return;
      }
      foreach (var pair in settings.Devices) {
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
      MidiCommand command;
      if (e.Message.Command == ChannelCommand.Controller) {
        double value = (double)e.Message.Data2 / 127;
        command = new MidiCommand() {
          deviceIndex = deviceIndex,
          type = MidiCommandType.Knob,
          index = e.Message.Data1,
          value = value,
        };
        this.MidiLog.Append(
          "MIDI message on " + MidiInput.GetDeviceName(deviceIndex) +
          " channel " + e.Message.MidiChannel +
          " updating knob #" + e.Message.Data1 +
          " to value " + value
        );
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
        var onOrOff = e.Message.Command == ChannelCommand.NoteOn ? "ON" : "OFF";
        this.MidiLog.Append(
          "MIDI message on " + MidiInput.GetDeviceName(deviceIndex) +
          " channel " + e.Message.MidiChannel +
          " updating note #" + e.Message.Data1 +
          " to " + onOrOff +
          " with value " + value
        );
      } else if (e.Message.Command == ChannelCommand.ProgramChange) {
        command = new MidiCommand() {
          deviceIndex = deviceIndex,
          type = MidiCommandType.Program,
          index = e.Message.Data1,
        };
        this.MidiLog.Append(
          "MIDI message on " + MidiInput.GetDeviceName(deviceIndex) +
          " channel " + e.Message.MidiChannel +
          " updating program to #" + e.Message.Data1
        );
      } else {
        return;
      }
      this.buffer.Enqueue(command);
      _ = this.DispatchBindingsAsync(command);
    }

    public Task DispatchBindingsAsync(MidiCommand command) {
      ImmutableDictionary<InnerBindingKey, ImmutableArray<Binding>> snapshot =
        Volatile.Read(ref this.bindings);
      var tasks = new List<Task>();
      var genericKey = new InnerBindingKey(
        command.deviceIndex, command.type, -1);
      var key = new InnerBindingKey(
        command.deviceIndex, command.type, command.index);
      this.CollectBindingInvocations(snapshot, genericKey, command, tasks);
      this.CollectBindingInvocations(snapshot, key, command, tasks);
      return tasks.Count == 0 ? Task.CompletedTask : Task.WhenAll(tasks);
    }

    private void CollectBindingInvocations(
      ImmutableDictionary<InnerBindingKey, ImmutableArray<Binding>> snapshot,
      InnerBindingKey key,
      MidiCommand command,
      List<Task> tasks
    ) {
      if (!snapshot.TryGetValue(
          key, out ImmutableArray<Binding> triggered)) {
        return;
      }
      foreach (Binding binding in triggered) {
        tasks.Add(this.InvokeBindingAsync(binding, command));
      }
    }

    private async Task InvokeBindingAsync(
      Binding binding, MidiCommand command
    ) {
      try {
        BindingInvocation invocation =
          binding.callback(command.index, command.value);
        if (invocation.Completion != null) {
          await invocation.Completion.ConfigureAwait(false);
        }
        if (invocation.Message != null) {
          this.MidiLog.Append(
            "Binding \"" + (binding.config?.BindingName ?? "unnamed") +
            "\" triggered: " + invocation.Message);
        }
      } catch (Exception error) {
        this.MidiLog.Append(
          "Binding \"" + (binding.config?.BindingName ?? "unnamed") +
          "\" failed: " + UnwrapInvocationError(error).Message);
      }
    }

    private static Exception UnwrapInvocationError(Exception error) =>
      error is System.Reflection.TargetInvocationException invocation &&
          invocation.InnerException != null
        ? invocation.InnerException
        : error;

    private void TerminateMidi() {
      if (this.devices == null) {
        return;
      }
      foreach (var pair in this.devices) {
        pair.Value.StopRecording();
        pair.Value.Dispose();
      }
      this.devices = null;
    }

    public void OperatorUpdate() {
      MidiSettingsSnapshot settings =
        this.runtimeSettings.MidiSettingsSnapshot;
      if (this.active &&
          settings.DeviceGeneration != this.appliedDeviceGeneration) {
        // Sanford callbacks keep feeding the concurrent command queue while
        // the operator thread exclusively owns this device-set transition.
        // Mark the generation before opening devices so a bad device is
        // contained by Operator's input exception boundary instead of being
        // retried hundreds of times per second.
        this.appliedDeviceGeneration = settings.DeviceGeneration;
        this.TerminateMidi();
        try {
          this.InitializeMidi(settings);
        } catch {
          this.TerminateMidi();
          throw;
        }
      }
      int numMessages = this.buffer.Count;
      MidiCommand[] commands = numMessages == 0
        ? Array.Empty<MidiCommand>()
        : new MidiCommand[numMessages];
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
      lock (this.midiStateLock) {
        if (!this.knobValues.TryGetValue(
            deviceIndex, out Dictionary<int, double> values) ||
            !values.TryGetValue(knob, out double value)) {
          return -1.0;
        }
        return value;
      }
    }

    public double GetNoteVelocity(int deviceIndex, int note) {
      lock (this.midiStateLock) {
        if (!this.noteVelocities.TryGetValue(
            deviceIndex, out Dictionary<int, double> values) ||
            !values.TryGetValue(note, out double value)) {
          return 0.0;
        }
        return value;
      }
    }

    public MidiCommand[] GetCommandsSinceLastTick() {
      return this.commandsSinceLastTick;
    }

  }

}
