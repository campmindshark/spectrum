using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Channels;
using Spectrum.Base;

namespace Spectrum.Web {

  /**
   * The state broadcast (docs/web_architecture.md, problem 3). Bridges
   * Configuration.PropertyChanged to connected web clients so that, with
   * last-write-wins sliders, every client still SEES the winning value —
   * nobody holds stale local authoritative state. It also pushes read-only
   * telemetry (FPS/BPM, see TelemetryCatalog) over the same feed.
   *
   * The transport is Server-Sent Events rather than SignalR: the installation
   * runs off-grid, so we can't pull the SignalR JS client from a CDN and don't
   * want to bundle an npm build step. SSE needs only the browser-native
   * EventSource, and the feed is one-way (clients write via REST PUT, read via
   * this stream), which is exactly SSE's shape.
   *
   * Every frame carries a "kind": "param" for a writable parameter's new value
   * (role-gated exactly like the REST layer — maintenance-only changes only
   * reach maintenance subscribers) or "telemetry" for a read-only readout (never
   * role-gated; a readout leaks nothing).
   */
  public sealed class ConfigEventStream : IDisposable {

    public sealed class Subscriber {
      public ControlRole Role { get; }
      public ChannelReader<string> Reader => this.channel.Reader;
      internal ChannelWriter<string> Writer => this.channel.Writer;
      private readonly Channel<string> channel;

      internal Subscriber(ControlRole role) {
        this.Role = role;
        // Unbounded but drop-oldest would be nicer; a slow phone just gets a
        // backlog it drains. Config-change volume is low (human slider drags),
        // so unbounded is fine here.
        this.channel = Channel.CreateUnbounded<string>(
          new UnboundedChannelOptions { SingleReader = true });
      }
    }

    private readonly ParameterRegistry registry;
    private readonly Configuration config;
    private readonly BeatBroadcaster beat;
    // Live engine counters (FPS), split out of Configuration; raises its own
    // PropertyChanged from the operator/OPC threads.
    private readonly RuntimeTelemetry telemetry;
    // The engine on/off source. Its EnabledChanged fires outside Configuration
    // entirely (Enabled is live Operator state, not a config property), so it
    // gets its own subscription and its own frame kind ("operator").
    private readonly Operator op;
    // Telemetry items indexed by the PropertyChanged name that triggers them,
    // split by which object raises that event.
    private readonly Dictionary<string, TelemetryItem> runtimeTelemetry =
      new Dictionary<string, TelemetryItem>();
    private readonly Dictionary<string, TelemetryItem> beatTelemetry =
      new Dictionary<string, TelemetryItem>();
    private readonly ConcurrentDictionary<Guid, Subscriber> subscribers =
      new ConcurrentDictionary<Guid, Subscriber>();

    public ConfigEventStream(
      ParameterRegistry registry, Configuration config, Operator op,
      RuntimeTelemetry telemetry, BeatBroadcaster beat
    ) {
      this.registry = registry;
      this.config = config;
      this.op = op;
      this.telemetry = telemetry;
      this.beat = beat;
      foreach (TelemetryItem item in TelemetryCatalog.Items) {
        switch (item.Source) {
          case TelemetrySource.Runtime:
            this.runtimeTelemetry[item.SourceProperty] = item;
            break;
          case TelemetrySource.Beat:
            this.beatTelemetry[item.SourceProperty] = item;
            break;
        }
      }
      this.config.PropertyChanged += this.OnConfigChanged;
      if (this.telemetry != null) {
        this.telemetry.PropertyChanged += this.OnTelemetryChanged;
      }
      if (this.beat != null) {
        this.beat.PropertyChanged += this.OnBeatChanged;
      }
      if (this.op != null) {
        this.op.EnabledChanged += this.OnOperatorEnabledChanged;
      }
    }

    public Subscriber Subscribe(ControlRole role, out Guid id) {
      var sub = new Subscriber(role);
      id = Guid.NewGuid();
      this.subscribers[id] = sub;
      return sub;
    }

    public void Unsubscribe(Guid id) {
      if (this.subscribers.TryRemove(id, out Subscriber sub)) {
        sub.Writer.TryComplete();
      }
    }

    // The out-of-band state a freshly opened stream needs: every telemetry
    // readout plus the current engine on/off state. The web host writes these to
    // the new stream so a client that connects between changes shows live
    // readouts and the correct Start/Stop label immediately. (Writable
    // parameters get their initial values from the REST GET, but telemetry and
    // operator state have no such GET on the change-fed pages.)
    public List<string> InitialStateFrames() {
      var frames = new List<string>();
      foreach (TelemetryItem item in TelemetryCatalog.Items) {
        frames.Add(Frame("telemetry", item.Key, SafeRead(item)));
      }
      if (this.op != null) {
        frames.Add(Frame("operator", "enabled", this.op.Enabled));
      }
      // Seed the current layer stack so a client that opens the stream before its
      // initial GET (or without one) still renders the panel from server truth.
      frames.Add(
        Frame("layers", "layers", LayersController.SerializeStack(this.config)));
      return frames;
    }

    // Fires on the UI/Dispatcher thread — every config write funnels through
    // the gateway or the native GUI. Reading a scalar and fanning a string onto
    // thread-safe channels is safe regardless.
    private void OnConfigChanged(object sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == null) {
        return;
      }
      if (this.registry.TryGet(e.PropertyName, out ParameterDescriptor d)) {
        object value = d.Get(this.config);
        this.Fan(Frame("param", d.Key, value), d);
        return;
      }
      // The layer stack is compound state (not in the ParameterRegistry), so it
      // gets its own frame kind carrying the whole stack. Not role-gated: it
      // replaced the user-level visualizer selector, and the stack leaks
      // nothing. Any writer (native panel, web PUT) triggers this.
      if (e.PropertyName == nameof(this.config.domeLayerStack)) {
        this.Fan(
          Frame("layers", "layers", LayersController.SerializeStack(this.config)),
          null
        );
      }
    }

    // Fires on the Operator/OPC threads (the FPS counters' writers); fanning
    // onto the thread-safe channels is safe from there.
    private void OnTelemetryChanged(object sender, PropertyChangedEventArgs e) {
      if (e.PropertyName != null &&
          this.runtimeTelemetry.TryGetValue(e.PropertyName, out TelemetryItem item)) {
        this.Fan(Frame("telemetry", item.Key, SafeRead(item)), null);
      }
    }

    private void OnBeatChanged(object sender, PropertyChangedEventArgs e) {
      if (e.PropertyName != null &&
          this.beatTelemetry.TryGetValue(e.PropertyName, out TelemetryItem item)) {
        this.Fan(Frame("telemetry", item.Key, SafeRead(item)), null);
      }
    }

    // The engine was started or stopped (by any client or the native power
    // button). Broadcast to everyone — like telemetry, on/off state leaks
    // nothing and is not role-gated.
    private void OnOperatorEnabledChanged(bool enabled) {
      this.Fan(Frame("operator", "enabled", enabled), null);
    }

    // Fan a frame to subscribers. When gate is non-null the frame is a
    // role-gated parameter change (maintenance-only params only reach
    // maintenance subscribers); a null gate is telemetry, sent to everyone.
    private void Fan(string payload, ParameterDescriptor gate) {
      foreach (Subscriber sub in this.subscribers.Values) {
        if (gate == null || ParameterRegistry.RoleCanAccess(sub.Role, gate)) {
          sub.Writer.TryWrite(payload);
        }
      }
    }

    private object SafeRead(TelemetryItem item) {
      try {
        return item.Read(this.telemetry, this.beat);
      } catch (Exception) {
        // A telemetry getter should never throw, but never let one kill the
        // change feed.
        return null;
      }
    }

    private static string Frame(string kind, string key, object value) =>
      JsonSerializer.Serialize(new { kind, key, value });

    public void Dispose() {
      this.config.PropertyChanged -= this.OnConfigChanged;
      if (this.telemetry != null) {
        this.telemetry.PropertyChanged -= this.OnTelemetryChanged;
      }
      if (this.beat != null) {
        this.beat.PropertyChanged -= this.OnBeatChanged;
      }
      if (this.op != null) {
        this.op.EnabledChanged -= this.OnOperatorEnabledChanged;
      }
      foreach (Subscriber sub in this.subscribers.Values) {
        sub.Writer.TryComplete();
      }
      this.subscribers.Clear();
    }
  }
}
