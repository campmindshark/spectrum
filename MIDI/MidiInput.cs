using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sanford.Multimedia.Midi;
using Spectrum.Base;
using System.Threading;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Spectrum.MIDI {

  using BindingKey = Tuple<MidiCommandType, int>;
  using InnerBindingKey = Tuple<int, MidiCommandType, int>;

  public class MidiInput : IMidiControlInput {

    private readonly Configuration config;
    private readonly IRuntimeSettingsConfiguration runtimeSettings;
    // The live tempo service, needed by tap-tempo bindings (owned by the
    // Operator, not part of Configuration).
    private readonly BeatBroadcaster beat;
    private readonly ApplicationStateDispatcher stateDispatcher;
    private readonly bool connectHardware;
    private Dictionary<int, InputDevice>? devices;
    private long appliedDeviceGeneration = -1;
    public long AppliedDeviceGeneration =>
      Volatile.Read(ref this.appliedDeviceGeneration);
    internal event Action? SettingsApplied;
    private readonly object lifecycleLock = new object();
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
      this.SetBindings();
      this.config.PropertyChanged += ConfigUpdated;
    }

    private void ConfigUpdated(object? sender, PropertyChangedEventArgs e) {
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

      ImmutableDictionary<int, MidiPreset> presets = settings.Presets;
      foreach (KeyValuePair<int, int> pair in configuredDevices) {
        if (!presets.TryGetValue(pair.Value, out MidiPreset? preset) ||
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
      BindingKey? bindingKey = binding.key;
      if (bindingKey == null || binding.callback == null) {
        throw new InvalidOperationException(
          "MIDI binding is missing its key or callback.");
      }
      var innerBindingKey = new InnerBindingKey(
        deviceIndex,
        bindingKey.Item1,
        bindingKey.Item2
      );
      if (!target.TryGetValue(
          innerBindingKey, out List<Binding>? keyBindings)) {
        keyBindings = new List<Binding>();
        target.Add(innerBindingKey, keyBindings);
      }
      keyBindings.Add(binding);
    }

    private bool active;
    public bool Active {
      get {
        lock (this.lifecycleLock) {
          return this.active;
        }
      }
      set {
        lock (this.lifecycleLock) {
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
      object? sender,
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
        Binding.bindingCallback? callback = binding.callback;
        if (callback == null) {
          throw new InvalidOperationException(
            "MIDI binding callback is unavailable.");
        }
        BindingInvocation invocation = callback(command.index, command.value);
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
        // The operator thread exclusively owns this device-set transition.
        // Mark the generation before opening devices so a bad device is
        // contained by Operator's input exception boundary instead of being
        // retried hundreds of times per second.
        this.appliedDeviceGeneration = settings.DeviceGeneration;
        this.TerminateMidi();
        try {
          this.InitializeMidi(settings);
          this.PublishSettingsApplied();
        } catch {
          this.TerminateMidi();
          throw;
        }
      }
    }

    private void PublishSettingsApplied() {
      try {
        this.SettingsApplied?.Invoke();
      } catch (Exception error) {
        Debug.WriteLine("MidiInput settings observer failed: " + error);
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
