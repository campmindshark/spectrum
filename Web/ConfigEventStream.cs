using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using Spectrum.Base;

namespace Spectrum.Web {

  /**
   * The state broadcast. Bridges
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

    internal const int SubscriberCapacity = 256;

    public sealed class Subscriber {
      private readonly record struct FrameKey(string Kind, string Key);
      private sealed record PendingFrame(FrameKey Key, string Payload);

      public ControlRole Role { get; }
      public SubscriberReader Reader { get; }
      private readonly object gate = new object();
      private readonly LinkedList<PendingFrame> pending =
        new LinkedList<PendingFrame>();
      private readonly Dictionary<FrameKey, LinkedListNode<PendingFrame>> byKey =
        new Dictionary<FrameKey, LinkedListNode<PendingFrame>>();
      // A single token means "the queue may be readable." The payloads remain
      // in the locked coalescing queue, so signals never grow with traffic.
      private readonly Channel<bool> readable = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) {
          SingleReader = true,
          SingleWriter = false,
          FullMode = BoundedChannelFullMode.DropWrite,
          AllowSynchronousContinuations = false,
        });
      private bool completed;

      internal Subscriber(ControlRole role) {
        this.Role = role;
        this.Reader = new SubscriberReader(this);
      }

      internal void Write(string kind, string key, string payload) {
        bool signal = false;
        lock (this.gate) {
          if (this.completed) {
            return;
          }
          var frameKey = new FrameKey(kind, key);
          if (this.byKey.TryGetValue(
              frameKey, out LinkedListNode<PendingFrame>? existing) &&
              existing != null) {
            existing.Value = new PendingFrame(frameKey, payload);
            return;
          }

          signal = this.pending.Count == 0;
          if (this.pending.Count >= SubscriberCapacity) {
            // More distinct replaceable keys than the bounded state window can
            // represent. A reset is itself retained state and forces the client
            // to re-fetch before it can edit from a stale local model.
            this.pending.Clear();
            this.byKey.Clear();
            frameKey = new FrameKey("reset", "reset");
            payload = Frame(
              "reset", "reset", new { reason = "subscriber-overflow" });
          }
          LinkedListNode<PendingFrame> node = this.pending.AddLast(
            new PendingFrame(frameKey, payload));
          this.byKey[frameKey] = node;
        }
        if (signal) {
          this.readable.Writer.TryWrite(true);
        }
      }

      private bool TryRead([NotNullWhen(true)] out string? payload) {
        lock (this.gate) {
          LinkedListNode<PendingFrame>? first = this.pending.First;
          if (first == null) {
            payload = null;
            return false;
          }
          this.pending.RemoveFirst();
          this.byKey.Remove(first.Value.Key);
          payload = first.Value.Payload;
          return true;
        }
      }

      internal void Complete() {
        lock (this.gate) {
          this.completed = true;
        }
        this.readable.Writer.TryComplete();
      }

      public sealed class SubscriberReader {
        private readonly Subscriber subscriber;

        internal SubscriberReader(Subscriber subscriber) {
          this.subscriber = subscriber;
        }

        public bool TryRead([NotNullWhen(true)] out string? payload) =>
          this.subscriber.TryRead(out payload);

        public async IAsyncEnumerable<string> ReadAllAsync(
          [EnumeratorCancellation] CancellationToken cancellationToken = default
        ) {
          ChannelReader<bool> signalReader = this.subscriber.readable.Reader;
          while (await signalReader.WaitToReadAsync(cancellationToken)) {
            while (signalReader.TryRead(out _)) { }
            while (this.subscriber.TryRead(out string? payload)) {
              yield return payload;
            }
          }
          while (this.subscriber.TryRead(out string? payload)) {
            yield return payload;
          }
        }
      }
    }

    private readonly ParameterRegistry registry;
    private readonly Configuration config;
    private readonly BeatBroadcaster? beat;
    // Live engine counters (FPS), split out of Configuration; raises its own
    // PropertyChanged from the operator/OPC threads.
    private readonly RuntimeTelemetry? telemetry;
    // The engine on/off source. Its EnabledChanged fires outside Configuration
    // entirely (Enabled is live Operator state, not a config property), so it
    // gets its own subscription and its own frame kind ("operator").
    private readonly Operator? op;
    // Telemetry items indexed by the PropertyChanged name that triggers them,
    // split by which object raises that event.
    private readonly Dictionary<string, TelemetryItem> runtimeTelemetry =
      new Dictionary<string, TelemetryItem>();
    private readonly Dictionary<string, TelemetryItem> beatTelemetry =
      new Dictionary<string, TelemetryItem>();
    private readonly ConcurrentDictionary<Guid, Subscriber> subscribers =
      new ConcurrentDictionary<Guid, Subscriber>();
    private readonly Action<DomeShowStateSnapshot>? showSnapshotCaptured;

    public ConfigEventStream(
      ParameterRegistry registry, Configuration config, Operator? op,
      RuntimeTelemetry? telemetry, BeatBroadcaster? beat
    ) : this(registry, config, op, telemetry, beat, null) { }

    internal ConfigEventStream(
      ParameterRegistry registry, Configuration config, Operator? op,
      RuntimeTelemetry? telemetry, BeatBroadcaster? beat,
      Action<DomeShowStateSnapshot>? showSnapshotCaptured
    ) {
      this.registry = registry;
      this.config = config;
      this.op = op;
      this.telemetry = telemetry;
      this.beat = beat;
      this.showSnapshotCaptured = showSnapshotCaptured;
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
      if (this.subscribers.TryRemove(id, out Subscriber? sub) && sub != null) {
        sub.Complete();
      }
    }

    // The out-of-band state a freshly opened stream needs: every telemetry
    // readout plus the current engine on/off state. The web host writes these to
    // the new stream so a client that connects between changes shows live
    // readouts and the correct Start/Stop label immediately. (Writable
    // parameters get their initial values from the REST GET, but telemetry and
    // operator state have no such GET on the change-fed pages.)
    internal List<string> InitialStateFrames() {
      var frames = new List<string>();
      foreach (TelemetryItem item in TelemetryCatalog.Items) {
        frames.Add(Frame("telemetry", item.Key, SafeRead(item)));
      }
      if (this.op != null) {
        frames.Add(Frame("operator", "enabled", this.op.Enabled));
      }
      // Seed the complete look as one message. The browser applies layers,
      // palette choices, and globals inside one event callback and therefore
      // never paints a cross-generation scene recall.
      frames.Add(this.ShowStateFrame());
      // Likewise seed the saved-scene list so the scenes dropdown is correct
      // immediately.
      frames.Add(Frame("scenes", "scenes", SceneNames(this.config)));
      return frames;
    }

    // Fires on the UI/Dispatcher thread — every config write funnels through
    // the gateway or the native GUI. Reading a scalar and fanning a string onto
    // thread-safe channels is safe regardless.
    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == null) {
        return;
      }
      if (e.PropertyName == DomeShowStateSnapshot.NotificationPropertyName) {
        this.Fan("show", "show", this.ShowStateValue(), null);
        return;
      }
      bool hasAtomicShowState =
        this.config is IDomeShowStateConfiguration;
      if (hasAtomicShowState && IsShowStateProperty(e.PropertyName)) {
        // SpectrumConfiguration raises component notifications only after
        // committing every field, then follows with the one generation
        // notification handled above. Do not expose that sequence as several
        // browser-visible states.
        return;
      }
      if (this.registry.TryGet(
          e.PropertyName, out ParameterDescriptor? d) && d != null) {
        object value = d.Get(this.config);
        this.Fan("param", d.Key, value, d);
        return;
      }
      // The layer stack is compound state (not in the ParameterRegistry), so it
      // gets its own frame kind carrying the whole stack. Not role-gated: it
      // replaced the user-level visualizer selector, and the stack leaks
      // nothing. Any writer (native panel, web PUT) triggers this.
      if (e.PropertyName == nameof(this.config.domeLayerStack)) {
        this.Fan("layers", "layers",
          LayersController.SerializeStack(this.config), null);
        return;
      }
      // The saved-scene list is likewise compound state outside the registry;
      // its own frame carries just the names so every client's dropdown stays in
      // sync. Not role-gated — the names leak nothing.
      if (e.PropertyName == nameof(this.config.domeScenes)) {
        this.Fan("scenes", "scenes", SceneNames(this.config), null);
        return;
      }
      // Named palettes are compound state. Explicit replacement operations
      // publish the full ordered list so every editor and dropdown converges.
      if (e.PropertyName == nameof(this.config.domePalettes) ||
          e.PropertyName.StartsWith("domePalettes.")) {
        this.Fan("palettes", "palettes",
          PaletteController.BuildPalettes(this.config), null);
      }
    }

    private static bool IsShowStateProperty(string propertyName) =>
      propertyName == nameof(Configuration.domeLayerStack) ||
      propertyName == nameof(Configuration.domePalettes) ||
      propertyName.StartsWith("domePalettes.") ||
      propertyName == nameof(Configuration.domeGlobalFadeSpeed) ||
      propertyName == nameof(Configuration.domeGlobalHueSpeed);

    private string ShowStateFrame() =>
      Frame("show", "show", this.ShowStateValue());

    private object ShowStateValue() {
      DomeShowStateSnapshot snapshot =
        (this.config as IDomeShowStateConfiguration)?
          .DomeShowStateSnapshot ?? DomeShowStateSnapshot.Empty;
      this.showSnapshotCaptured?.Invoke(snapshot);
      return new {
        generation = snapshot.Generation,
        layers = LayersController.SerializeStack(snapshot.LayerStack),
        palettes = PaletteController.BuildPalettes(snapshot.Palettes),
        globalFadeSpeed = snapshot.GlobalFadeSpeed,
        globalHueSpeed = snapshot.GlobalHueSpeed,
      };
    }

    // The saved-scene names, in stored order — the payload of every "scenes"
    // frame (shared by InitialStateFrames and the change notification).
    private static List<string> SceneNames(Configuration config) {
      var names = new List<string>();
      if (!config.domeScenes.IsDefaultOrEmpty) {
        foreach (DomeSceneView scene in config.domeScenes) {
          if (scene != null && scene.Name != null) {
            names.Add(scene.Name);
          }
        }
      }
      return names;
    }

    // Fires on the Operator/OPC threads (the FPS counters' writers); fanning
    // onto the thread-safe channels is safe from there.
    private void OnTelemetryChanged(object? sender, PropertyChangedEventArgs e) {
      if (e.PropertyName != null &&
          this.runtimeTelemetry.TryGetValue(
            e.PropertyName, out TelemetryItem? item) && item != null) {
        this.Fan("telemetry", item.Key, SafeRead(item), null);
      }
    }

    private void OnBeatChanged(object? sender, PropertyChangedEventArgs e) {
      if (e.PropertyName != null &&
          this.beatTelemetry.TryGetValue(
            e.PropertyName, out TelemetryItem? item) && item != null) {
        this.Fan("telemetry", item.Key, SafeRead(item), null);
      }
    }

    // The engine was started or stopped (by any client or the native power
    // button). Broadcast to everyone — like telemetry, on/off state leaks
    // nothing and is not role-gated.
    private void OnOperatorEnabledChanged(bool enabled) {
      this.Fan("operator", "enabled", enabled, null);
    }

    // Fan a frame to subscribers. When gate is non-null the frame is a
    // role-gated parameter change (maintenance-only params only reach
    // maintenance subscribers); a null gate is telemetry, sent to everyone.
    private void Fan(
      string kind, string key, object? value, ParameterDescriptor? gate
    ) {
      string payload = Frame(kind, key, value);
      foreach (Subscriber sub in this.subscribers.Values) {
        if (gate == null || ParameterRegistry.RoleCanAccess(sub.Role, gate)) {
          sub.Write(kind, key, payload);
        }
      }
    }

    private object? SafeRead(TelemetryItem item) {
      try {
        return item.Read(this.telemetry, this.beat);
      } catch (Exception) {
        // A telemetry getter should never throw, but never let one kill the
        // change feed.
        return null;
      }
    }

    private static string Frame(string kind, string key, object? value) =>
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
        sub.Complete();
      }
      this.subscribers.Clear();
    }
  }
}
