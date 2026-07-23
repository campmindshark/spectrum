using System;
using System.Threading;
using Spectrum.Base;
using Spectrum.LEDs;
using System.Collections.Generic;
using System.Diagnostics;
using System.ComponentModel;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Spectrum.Visualizers;

namespace Spectrum {

  public class Operator : ISpectrumHostRuntime {

    // Keeps the existing headless/test construction path synchronous. The WPF
    // host supplies its real dispatcher through the overload below.
    private sealed class ImmediateApplicationStateDispatcher :
      ApplicationStateDispatcher {
      public bool CheckAccess() => true;
      public void Post(Action mutation) => mutation();
      public Task InvokeAsync(Action mutation) {
        mutation();
        return Task.CompletedTask;
      }
      public Task<T> InvokeAsync<T>(Func<T> read) =>
        Task.FromResult(read());
    }

    private readonly Configuration config;
    private readonly IDomeShowStateConfiguration showStateSource;
    private readonly IRuntimeSettingsConfiguration runtimeSettingsSource;
    private readonly List<Input> inputs;
    private readonly List<Output> outputs;
    private readonly List<Visualizer> visualizers;
    private readonly LayerCatalog layerCatalog;
    // The sole owner of per-instance renderer state. Matching IDs retain
    // trails and other live state across plan changes and scene recall.
    private readonly LayerRendererStore layerRendererStore;
    private readonly RenderPlanCompiler renderPlanCompiler =
      new RenderPlanCompiler();
    private DomeShowStateSnapshot publishedShowState =
      DomeShowStateSnapshot.Empty;
    private int layerReconcilePending;
    private int showStateRefreshPending;
    private int layerPlanReconciliationCount;
    private readonly ConfigurationDomeLayerEnvironment layerEnvironment;

    internal int LayerPlanReconciliationCount =>
      Volatile.Read(ref this.layerPlanReconciliationCount);

    // Exposed so diagnostic windows (e.g. the wand status display) can read the
    // live orientation-device state. Stable for the Operator's lifetime — Reboot
    // only toggles threads, it doesn't rebuild this instance.
    public OrientationInput OrientationInput { get; }
    internal IMidiControlInput MidiInput { get; }
    // Live runtime state and services, split off Configuration (which carries
    // only persisted settings — arch_issues item 5). All stable for the
    // Operator's lifetime, like OrientationInput above:
    //  - BeatBroadcaster: the tempo service every beat consumer reads and the
    //    beat detectors / tap sources report into.
    //  - Telemetry: FPS counters for the native labels and the web SSE feed.
    //  - MidiLog: the rolling binding log (owned by MidiInput), shown by the
    //    VJ HUD.
    //  - DomeCalibration: the transient dome-mapping calibration selection
    //    shared by both calibration UIs and the calibration visualizer.
    //  - DomeOutput: the dome device, exposed for the simulator window to drain
    //    its frame queue directly from the producer.
    public BeatBroadcaster BeatBroadcaster { get; }
    public RuntimeTelemetry Telemetry { get; }
    public ObservableMidiLog MidiLog { get; }
    public DomeCalibrationState DomeCalibration { get; }
    public LEDDomeOutput DomeOutput { get; }
    private readonly Stopwatch frameRateStopwatch;
    private int completedFramesInWindow;
    private int allocationMeasurementEnabled;
    private int allocationMeasurementWriters;
    private long measuredFrames;
    private long measuredAllocatedBytes;

    // Global rate cap: the operator loop runs no faster than 400Hz, i.e. at
    // least this many Stopwatch ticks per frame (2.5ms). Stopwatch.Frequency
    // is ticks-per-second, so dividing by it yields the per-frame budget. Note
    // OPC output to the BeagleBone has its own, independent send-rate cap (see
    // MaxRefreshRateHz in OPCAPI) — this one bounds engine compute, not the wire.
    private const int MaxFramesPerSecond = 400;
    private static readonly long MinFrameTicks =
      Stopwatch.Frequency / MaxFramesPerSecond;

    // Scratch collections reused across every OperatorThread frame so the
    // scheduling pass allocates nothing steady-state. Only ever touched on the
    // operator thread, so they need no synchronization.
    private readonly List<Output> activeOutputs = new List<Output>();
    private readonly HashSet<Visualizer> activeVisualizers =
      new HashSet<Visualizer>();
    private readonly HashSet<Input> activeInputs = new HashSet<Input>();
    private readonly List<Visualizer> topPriVisualizers =
      new List<Visualizer>();
    private readonly List<Visualizer> alwaysRunVisualizers =
      new List<Visualizer>();
    private readonly Dictionary<Visualizer, ImmutableArray<Input>>
      plannedLayerInputs =
        new Dictionary<Visualizer, ImmutableArray<Input>>();

    // Visualizers that have thrown too many times are quarantined: the
    // scheduling pass skips them so a consistently-broken visualizer can't take
    // the output down every frame (and can't spam the log). Keyed by visualizer,
    // valued by cumulative throw count; once the count crosses
    // VisualizerQuarantineThreshold the visualizer is treated as unavailable.
    // Only touched on the operator thread, so it needs no synchronization.
    private const int VisualizerQuarantineThreshold = 10;
    private readonly Dictionary<Visualizer, int> visualizerFailureCounts =
      new Dictionary<Visualizer, int>();
    // Hardware initialization happens inside Input/Output.Active setters. A
    // failed transition is quarantined until the operator is restarted so a
    // missing device cannot throw (and retry) hundreds of times per second.
    private readonly HashSet<Input> inputActivationFailures = new HashSet<Input>();
    private readonly HashSet<Output> outputActivationFailures = new HashSet<Output>();

    public Operator(Configuration config) : this(
      config,
      new ImmediateApplicationStateDispatcher(),
      new DisabledSpectrumInputFactory()) {
    }

    public Operator(
      Configuration config,
      ApplicationStateDispatcher stateDispatcher
    ) : this(config, stateDispatcher, new DisabledSpectrumInputFactory()) {
    }

    // Production construction connects the physical adapters. The internal
    // overload gives the integrated concurrency test deterministic, in-memory
    // hardware boundaries while running this same operator loop.
    internal Operator(
      Configuration config,
      ApplicationStateDispatcher stateDispatcher,
      bool connectHardware
    ) : this(
      config,
      stateDispatcher,
      new DisabledSpectrumInputFactory(),
      connectHardware) {
    }

    public Operator(
      Configuration config,
      ApplicationStateDispatcher stateDispatcher,
      ISpectrumInputFactory inputFactory
    ) : this(config, stateDispatcher, inputFactory, true) {
    }

    internal Operator(
      Configuration config,
      ApplicationStateDispatcher stateDispatcher,
      ISpectrumInputFactory inputFactory,
      bool connectHardware
    ) {
      if (stateDispatcher == null) {
        throw new ArgumentNullException(nameof(stateDispatcher));
      }
      if (inputFactory == null) {
        throw new ArgumentNullException(nameof(inputFactory));
      }
      this.config = config;
      this.showStateSource = config as IDomeShowStateConfiguration ??
        throw new ArgumentException(
          "Operator configuration must publish immutable show-state snapshots.",
          nameof(config));
      this.runtimeSettingsSource = config as IRuntimeSettingsConfiguration ??
        throw new ArgumentException(
          "Operator configuration must publish immutable runtime settings.",
          nameof(config));
      this.BeatBroadcaster = new BeatBroadcaster(config);
      this.Telemetry = new RuntimeTelemetry();
      this.DomeCalibration = new DomeCalibrationState();

      this.frameRateStopwatch = new Stopwatch();
      this.frameRateStopwatch.Start();
      this.completedFramesInWindow = 0;

      this.inputs = new List<Input>();
      IAudioLevelInput audio = inputFactory.CreateAudioInput(
        config, this.BeatBroadcaster) ?? throw new InvalidOperationException(
          "The platform input factory returned no audio input.");
      this.AudioInput = audio;
      this.inputs.Add(audio);
      IMidiControlInput midi = inputFactory.CreateMidiInput(
        config, this.BeatBroadcaster, stateDispatcher) ??
        throw new InvalidOperationException(
          "The platform input factory returned no MIDI input.");
      this.inputs.Add(midi);
      this.MidiInput = midi;
      this.MidiLog = midi.MidiLog;
      // Pro DJ Link is portable receive-only UDP input. Keep it independent of
      // the platform audio adapter so ALSA and WASAPI compositions both offer
      // the same network tempo source.
      this.inputs.Add(new ProDjLinkInput(
        config, this.BeatBroadcaster, connectHardware));
      var orientation = new OrientationInput(
        config, stateDispatcher, connectHardware);
      this.inputs.Add(orientation);
      this.OrientationInput = orientation;
      // Shared by every orientation-driven dome layer (Paintbrush, Ripple,
      // Stamp, Metaball, Magnetic Field) so they idle-drift around the same
      // wandering point instead of each running its own independent random walk.
      var orientationCenter = new OrientationCenter(config, orientation);
      this.layerEnvironment = new ConfigurationDomeLayerEnvironment();

      this.outputs = new List<Output>();
      // orientationCenter doubles as the prism blends' live wand-angle source
      // (Follow Orientation), so ChromaticFringe/Iridescence can track the
      // spotlighted wand.
      var dome = new LEDDomeOutput(
        config, this.Telemetry, this.BeatBroadcaster, orientationCenter);
      this.outputs.Add(dome);
      this.DomeOutput = dome;

      this.visualizers = new List<Visualizer>();
      this.AddVisualizer(new LEDDomeStrutIterationDiagnosticVisualizer(
        this.config,
        dome
      ));
      this.AddVisualizer(new LEDDomeFlashColorsDiagnosticVisualizer(
        this.config,
        dome
      ));
      this.AddVisualizer(new LEDDomeStrandTestDiagnosticVisualizer(
        this.config,
        dome
      ));
      this.AddVisualizer(new LEDDomeMappingCalibrationVisualizer(
        this.config,
        this.DomeCalibration,
        dome
      ));
      this.AddVisualizer(new LEDDomeFullColorFlashDiagnosticVisualizer(
        this.config,
        dome
      ));
      this.AddVisualizer(new LEDDomeQuaternionTestVisualizer(
        this.config,
        orientation,
        dome
      ));
      this.layerCatalog = DomeLayerCatalog.Create(
        this.layerEnvironment, audio, orientation, orientationCenter,
        this.BeatBroadcaster, dome);
      this.layerRendererStore = new LayerRendererStore(this.layerCatalog);
      this.config.PropertyChanged += this.OnLayerConfigurationChanged;
      this.CapturePublishedShowState();
      this.ReconcileLayerVisualizers();
      DomeRuntimeFrameSnapshot initialRuntime =
        this.runtimeSettingsSource.DomeRuntimeFrameSnapshot;
      this.layerEnvironment.BeginOperatorFrame(
        this.DomeOutput.ShowState, initialRuntime);
      Interlocked.Exchange(ref this.layerReconcilePending, 0);
    }

    // Live input signal for readiness surfaces. AudioInput owns capture state;
    // exposing the instance avoids duplicating device or meter logic in UI code.
    public IAudioLevelInput AudioInput { get; }

    private void OnLayerConfigurationChanged(
      object sender, PropertyChangedEventArgs e
    ) {
      if (e.PropertyName == DomeShowStateSnapshot.NotificationPropertyName) {
        // SpectrumConfiguration compiled this complete immutable generation
        // before raising PropertyChanged. Reconciliation and frames never
        // reconstruct a look from serializer DTOs.
        bool layerStackChanged = this.CapturePublishedShowState();
        if (layerStackChanged) {
          Interlocked.Exchange(ref this.layerReconcilePending, 1);
          if (!this.Enabled) {
            this.ReconcileLayerVisualizers();
            DomeRuntimeFrameSnapshot runtime =
              this.runtimeSettingsSource.DomeRuntimeFrameSnapshot;
            this.layerEnvironment.BeginOperatorFrame(
              this.DomeOutput.ShowState, runtime);
            Interlocked.Exchange(ref this.layerReconcilePending, 0);
          }
        } else {
          // Palettes and global effects share the accepted plan but do not
          // require renderer preparation or plan compilation.
          if (this.Enabled) {
            // Keep render-generation publication on the operator thread. This
            // prevents a palette/global event racing an in-flight plan commit
            // from being overwritten by that older show-state object.
            Interlocked.Exchange(ref this.showStateRefreshPending, 1);
          } else {
            this.DomeOutput.PublishShowState(
              Volatile.Read(ref this.publishedShowState));
            this.layerEnvironment.BeginOperatorFrame(
              Volatile.Read(ref this.publishedShowState),
              this.runtimeSettingsSource.DomeRuntimeFrameSnapshot);
          }
        }
      } else if (e.PropertyName == nameof(this.config.domeScenes)) {
        Interlocked.Exchange(ref this.layerReconcilePending, 1);
      } else if (!this.Enabled) {
        // Headless/direct-render tests and disabled preview surfaces still see
        // the latest immutable control generation without creating a render
        // plan or pretending an operator frame is active.
        this.layerEnvironment.BeginOperatorFrame(
          Volatile.Read(ref this.publishedShowState),
          this.runtimeSettingsSource.DomeRuntimeFrameSnapshot);
      }
    }

    private bool CapturePublishedShowState() {
      DomeShowStateSnapshot snapshot =
        this.showStateSource.DomeShowStateSnapshot ??
          DomeShowStateSnapshot.Empty;
      DomeShowStateSnapshot previous = Volatile.Read(
        ref this.publishedShowState) ?? DomeShowStateSnapshot.Empty;
      Volatile.Write(
        ref this.publishedShowState,
        snapshot);
      return !ReferenceEquals(previous.LayerStack, snapshot.LayerStack);
    }

    private bool ReconcileLayerVisualizers() {
      Interlocked.Increment(ref this.layerPlanReconciliationCount);
      DomeShowStateSnapshot showState = Volatile.Read(
        ref this.publishedShowState) ?? DomeShowStateSnapshot.Empty;
      LayerStackSnapshot snapshot = showState.LayerStack;
      try {
        using LayerRendererStore.Transaction candidate =
          this.layerRendererStore.Prepare(snapshot);
        foreach (LayerRendererBinding binding in candidate.Bindings) {
          if (binding.Created && binding.Renderer is not Visualizer) {
            throw new InvalidOperationException(
              "Layer renderer must implement Visualizer: " +
              binding.Renderer?.RendererId);
          }
        }
        RenderPlan plan = this.renderPlanCompiler.Compile(
          snapshot, candidate.Get);

        // Everything that can invoke a feature compiler or renderer factory
        // has succeeded. Publish this generation as one owner-side commit.
        candidate.Commit();
        foreach (LayerRendererBinding binding in candidate.Bindings) {
          if (binding.Created) {
            this.AddVisualizer((Visualizer)binding.Renderer);
          }
        }
        this.DomeOutput.PublishRenderGeneration(
          new DomeRenderGeneration(plan, showState));
        foreach (LayerRendererBinding binding in candidate.Bindings) {
          if (binding.ReplacedRenderer != null) {
            this.ReleaseLayerRenderer(binding.ReplacedRenderer);
          }
        }
        foreach (ILayerRenderer retired in this.layerRendererStore.Retain(
          this.RetainedLayerInstanceIds(snapshot))) {
          this.ReleaseLayerRenderer(retired);
        }
        this.Telemetry.LayerPlanError = null;
        return true;
      } catch (Exception error) {
        // Keep the last published compositor plan and live renderer store.
        // A bad extension/layer is observable but cannot terminate the engine.
        this.Telemetry.LayerPlanError = error.Message;
        Debug.WriteLine("Layer plan rejected: " + error);
        return false;
      }
    }

    private void AddVisualizer(Visualizer visualizer) {
      if (visualizer == null) {
        throw new ArgumentNullException(nameof(visualizer));
      }
      this.visualizers.Add(visualizer);
      this.DomeOutput.RegisterVisualizer(visualizer);
    }

    private HashSet<LayerInstanceId> RetainedLayerInstanceIds(
      LayerStackSnapshot current
    ) {
      var retained = new HashSet<LayerInstanceId>();
      foreach (LayerSnapshot layer in current.Layers) {
        retained.Add(layer.Id);
      }
      SceneRetentionSnapshot scenes =
        this.runtimeSettingsSource.SceneRetentionSnapshot;
      foreach (string instanceId in scenes.LayerInstanceIds) {
        retained.Add(new LayerInstanceId(instanceId));
      }
      return retained;
    }

    private void ReleaseLayerRenderer(ILayerRenderer renderer) {
      if (renderer is Visualizer visualizer) {
        this.visualizers.Remove(visualizer);
        this.DomeOutput.UnregisterVisualizer(visualizer);
        this.visualizerFailureCounts.Remove(visualizer);
      }
      if (renderer is IDisposable disposable) {
        disposable.Dispose();
      }
    }

    private bool enabled;
    private Thread operatorThread;
    // Cooperative stop flag for OperatorThread, replacing Thread.Abort().
    private volatile bool operatorThreadStop;
    // Raised (outside the visualizers lock) whenever Enabled actually flips, so
    // observers such as the web control surface can reflect and broadcast the
    // engine's on/off state. Every native path that stops/starts the engine (the
    // power button, an audio-device refresh) routes through the Enabled setter,
    // so every transition is observed here regardless of who initiated it.
    public event Action<bool> EnabledChanged;
    public bool Enabled {
      get {
        lock (this.visualizers) {
          return this.enabled;
        }
      }
      set {
        lock (this.visualizers) {
          if (this.enabled == value) {
            return;
          }
          if (value) {
            this.ReconcileLayerVisualizers();
            this.inputActivationFailures.Clear();
            this.outputActivationFailures.Clear();
            this.operatorThreadStop = false;
            this.operatorThread = new Thread(OperatorThread);
            this.operatorThread.Start();
          } else {
            // OperatorThread does not take lock(this.visualizers), so joining
            // while holding it is safe and won't deadlock.
            this.operatorThreadStop = true;
            this.operatorThread.Join();
            this.operatorThread = null;

            foreach (var input in this.inputs) {
              this.SetInputActiveSafely(input, false);
            }
            foreach (var output in this.outputs) {
              this.SetOutputActiveSafely(output, false);
            }
          }
          this.enabled = value;
        }
        this.EnabledChanged?.Invoke(value);
      }
    }

    public void Reboot() {
      // Enabled raises EnabledChanged after releasing the visualizers lock.
      // Do not wrap the two transitions in that same lock or the outer Monitor
      // would silently break the event's locking contract.
      if (this.Enabled) {
        this.Enabled = false;
        this.Enabled = true;
      }
    }

    private void OperatorThread() {
      // The Operator may sit disabled for an arbitrary amount of time after it
      // is constructed. Start the telemetry window when work actually starts
      // so the first reading is not diluted by that idle time.
      this.frameRateStopwatch.Restart();
      this.completedFramesInWindow = 0;

      // Timestamp this frame is allowed to start, advanced by one frame budget
      // each tick to cap the loop at MaxFramesPerSecond.
      long nextFrameTimestamp = Stopwatch.GetTimestamp();
      while (!this.operatorThreadStop) {
        ThrottleFrame(ref nextFrameTimestamp);
        bool measureAllocation = Volatile.Read(
          ref this.allocationMeasurementEnabled) != 0;
        long allocatedBefore = 0;
        if (measureAllocation) {
          Interlocked.Increment(ref this.allocationMeasurementWriters);
          allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        }

        if (Interlocked.Exchange(ref this.layerReconcilePending, 0) != 0) {
          this.ReconcileLayerVisualizers();
        }
        if (Interlocked.Exchange(ref this.showStateRefreshPending, 0) != 0) {
          DomeShowStateSnapshot latest = Volatile.Read(
            ref this.publishedShowState) ?? DomeShowStateSnapshot.Empty;
          if (ReferenceEquals(
              latest.LayerStack,
              this.DomeOutput.ShowState.LayerStack)) {
            this.DomeOutput.PublishShowState(latest);
          }
        }

        // One accepted object supplies this frame's plan, palettes, and global
        // effects. A concurrently published configuration generation cannot be
        // observed until the next reconciliation/frame boundary.
        DomeRuntimeFrameSnapshot frameRuntime =
          this.runtimeSettingsSource.DomeRuntimeFrameSnapshot;
        DomeShowStateSnapshot frameShowState =
          this.DomeOutput.BeginOperatorFrame(frameRuntime);
        this.layerEnvironment.BeginOperatorFrame(
          frameShowState, frameRuntime);

        // Compile once before scheduling. These are the same renderer objects
        // and immutable layer snapshots DomeCompositor will consume below.
        this.plannedLayerInputs.Clear();
        foreach (CompiledLayer layer in this.DomeOutput.RenderPlan.Layers) {
          if (layer.Renderer is Visualizer visualizer) {
            this.plannedLayerInputs[visualizer] = layer.RequiredInputs;
          }
        }

        // We're going to start by figuring out which Outputs consider
        // themselves enabled. For each enabled Output, we'll find what the
        // highest priority reported by any Visualizer is, and we'll consider
        // those Visualizers as candidates to enable.
        this.activeOutputs.Clear();
        this.activeVisualizers.Clear();
        foreach (var output in this.outputs) {
          if (!output.Enabled) {
            continue;
          }
          int topPri = 1;
          this.topPriVisualizers.Clear();
          this.alwaysRunVisualizers.Clear();
          foreach (var visualizer in output.GetVisualizers()) {
            bool isLayerVisualizer = visualizer is DomeLayerVisualizer;
            if (isLayerVisualizer) {
              if (!this.plannedLayerInputs.TryGetValue(
                    visualizer, out ImmutableArray<Input> plannedInputs)) {
                continue;
              }
              // Keep ImmutableArray strongly typed. Converting it to
              // IReadOnlyList boxes the struct once per layer, per frame.
              if (!AllInputsEnabled(plannedInputs)) {
                continue;
              }
            } else if (!AllInputsEnabled(visualizer.GetInputs())) {
              // We can only consider a visualizer if all its inputs are
              // enabled.
              continue;
            }
            // Skip visualizers that have been quarantined for throwing too
            // often, so the output falls through to a lower-priority tier
            // instead of going dark every frame.
            if (this.IsQuarantined(visualizer)) {
              continue;
            }
            // Layer membership and enabled state were already resolved by the
            // compiled plan. Priority remains only for diagnostic overrides.
            int pri = isLayerVisualizer ? 2 : visualizer.Priority;
            bool canAdd = false;
            if (pri == -1) {
              this.alwaysRunVisualizers.Add(visualizer);
            } else if (pri > topPri) {
              topPri = pri;
              this.topPriVisualizers.Clear();
              canAdd = true;
            } else if (pri == topPri) {
              canAdd = true;
            }
            if (!canAdd) {
              continue;
            }
            this.topPriVisualizers.Add(visualizer);
          }
          this.topPriVisualizers.AddRange(this.alwaysRunVisualizers);
          if (this.topPriVisualizers.Count != 0) {
            this.activeOutputs.Add(output);
          }
          // HashSet.UnionWith receives IEnumerable<T>, which boxes List<T>'s
          // struct enumerator. Add by index to keep scheduling allocation-free.
          for (int i = 0; i < this.topPriVisualizers.Count; i++) {
            this.activeVisualizers.Add(this.topPriVisualizers[i]);
          }
        }

        this.activeInputs.Clear();
        foreach (var visualizer in this.activeVisualizers) {
          if (this.plannedLayerInputs.TryGetValue(
              visualizer, out ImmutableArray<Input> plannedInputs)) {
            for (int i = 0; i < plannedInputs.Length; i++) {
              this.activeInputs.Add(plannedInputs[i]);
            }
          } else {
            foreach (Input input in visualizer.GetInputs()) {
              this.activeInputs.Add(input);
            }
          }
        }
        foreach (var input in this.inputs) {
          if (input.Enabled && input.AlwaysActive) {
            this.activeInputs.Add(input);
          }
        }

        foreach (var output in this.outputs) {
          this.SetOutputActiveSafely(output, activeOutputs.Contains(output));
        }
        foreach (var visualizer in this.visualizers) {
          visualizer.Enabled = activeVisualizers.Contains(visualizer);
        }
        foreach (var input in this.inputs) {
          this.SetInputActiveSafely(input, activeInputs.Contains(input));
        }

        foreach (var input in activeInputs) {
          try {
            input.OperatorUpdate();
          } catch (Exception e) {
            // An unhandled throw on this background thread would terminate the
            // whole process; for a live installation the loop must survive it.
            Debug.WriteLine(
              "Operator: input " + input.GetType().Name +
              " threw in OperatorUpdate: " + e);
          }
        }

        // Start one shared orientation-snapshot generation for this frame.
        // The first OrientationCenter, LayerTrigger, or PointCloud consumer
        // captures it lazily; every later consumer reuses that deep clone.
        this.OrientationInput.BeginOperatorFrame(frameRuntime);

        foreach (var visualizer in activeVisualizers) {
          try {
            visualizer.Visualize();
          } catch (Exception e) {
            // Keep the engine running even if one visualizer throws mid-frame.
            // Repeat offenders are quarantined so they stop taking the
            // output down and stop spamming the log.
            this.RecordVisualizerFailure(visualizer, e);
          }
        }

        foreach (var output in activeOutputs) {
          try {
            output.OperatorUpdate();
          } catch (Exception e) {
            Debug.WriteLine(
              "Operator: output " + output.GetType().Name +
              " threw in OperatorUpdate: " + e);
          }
        }

        // A frame is generated only after all visualizers and outputs have
        // finished. Counting at the top of the loop made an expensive,
        // in-progress frame appear complete and reported a raw frame count as
        // FPS even when the measurement window stretched well past a second.
        this.completedFramesInWindow++;
        if (measureAllocation) {
          Interlocked.Add(
            ref this.measuredAllocatedBytes,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
          Interlocked.Increment(ref this.measuredFrames);
          Interlocked.Decrement(ref this.allocationMeasurementWriters);
        }
        if (this.frameRateStopwatch.ElapsedMilliseconds >= 1000) {
          double elapsedSeconds =
            this.frameRateStopwatch.Elapsed.TotalSeconds;
          this.Telemetry.OperatorFPS = (int)Math.Round(
            this.completedFramesInWindow / elapsedSeconds,
            MidpointRounding.AwayFromZero
          );
          this.completedFramesInWindow = 0;
          this.frameRateStopwatch.Restart();
        }
      }
    }

    internal void BeginAllocationMeasurement() {
      Interlocked.Exchange(ref this.measuredFrames, 0);
      Interlocked.Exchange(ref this.measuredAllocatedBytes, 0);
      Volatile.Write(ref this.allocationMeasurementEnabled, 1);
    }

    internal (long Frames, long Bytes) EndAllocationMeasurement() {
      Volatile.Write(ref this.allocationMeasurementEnabled, 0);
      SpinWait.SpinUntil(
        () => Volatile.Read(ref this.allocationMeasurementWriters) == 0,
        TimeSpan.FromSeconds(2));
      return (
        Volatile.Read(ref this.measuredFrames),
        Volatile.Read(ref this.measuredAllocatedBytes));
    }

    // Tracks a visualizer throw and quarantines the visualizer once it has
    // failed VisualizerQuarantineThreshold times, so a persistently-broken
    // visualizer is dropped from scheduling instead of throwing every frame.
    private void RecordVisualizerFailure(Visualizer visualizer, Exception e) {
      this.visualizerFailureCounts.TryGetValue(visualizer, out int count);
      count++;
      this.visualizerFailureCounts[visualizer] = count;
      if (count == VisualizerQuarantineThreshold) {
        Debug.WriteLine(
          "Operator: quarantining visualizer " + visualizer.GetType().Name +
          " after " + count + " failures; last error: " + e);
      } else if (count < VisualizerQuarantineThreshold) {
        Debug.WriteLine(
          "Operator: visualizer " + visualizer.GetType().Name +
          " threw in Visualize: " + e);
      }
    }

    private void SetInputActiveSafely(Input input, bool active) {
      if (active && this.inputActivationFailures.Contains(input)) {
        return;
      }
      try {
        input.Active = active;
        if (!active) {
          this.inputActivationFailures.Remove(input);
        }
      } catch (Exception e) {
        if (active) {
          this.inputActivationFailures.Add(input);
          // An Active setter may have completed part of its initialization
          // before throwing. Best-effort rollback leaves the device in a known
          // inactive state until the operator is restarted.
          try {
            input.Active = false;
          } catch (Exception rollbackError) {
            Debug.WriteLine(
              "Operator: input " + input.GetType().Name +
              " also failed activation rollback: " + rollbackError);
          }
        }
        Debug.WriteLine(
          "Operator: input " + input.GetType().Name +
          " failed to become " + (active ? "active" : "inactive") + ": " + e);
      }
    }

    private void SetOutputActiveSafely(Output output, bool active) {
      if (active && this.outputActivationFailures.Contains(output)) {
        return;
      }
      try {
        output.Active = active;
        if (!active) {
          this.outputActivationFailures.Remove(output);
        }
      } catch (Exception e) {
        if (active) {
          this.outputActivationFailures.Add(output);
          try {
            output.Active = false;
          } catch (Exception rollbackError) {
            Debug.WriteLine(
              "Operator: output " + output.GetType().Name +
              " also failed activation rollback: " + rollbackError);
          }
        }
        Debug.WriteLine(
          "Operator: output " + output.GetType().Name +
          " failed to become " + (active ? "active" : "inactive") + ": " + e);
      }
    }

    private bool IsQuarantined(Visualizer visualizer) {
      return this.visualizerFailureCounts.TryGetValue(
        visualizer, out int count
      ) && count >= VisualizerQuarantineThreshold;
    }

    // Blocks until roughly one frame budget (1/MaxFramesPerSecond) has elapsed
    // since the previous frame, so the whole program runs no faster than
    // MaxFramesPerSecond. nextFrameTimestamp tracks the earliest allowed start
    // of the next frame. We Thread.Sleep off the whole-millisecond portion of
    // the remaining budget and skip the sub-millisecond tail rather than
    // busy-spinning a core. App.OnStartup raises the Windows timer resolution to
    // 1ms (timeBeginPeriod), so Thread.Sleep(1) lands near 1ms and the loop holds
    // close to the cap; the dropped sub-millisecond tail still lets it drift
    // slightly under MaxFramesPerSecond — an acceptable trade for not pinning a CPU.
    private static void ThrottleFrame(ref long nextFrameTimestamp) {
      long now = Stopwatch.GetTimestamp();
      // If we fell behind (a frame ran long), don't try to "catch up" by
      // bursting above the cap — just reset the clock to now.
      if (now > nextFrameTimestamp) {
        nextFrameTimestamp = now;
      } else {
        long remainingMs =
          (nextFrameTimestamp - now) * 1000 / Stopwatch.Frequency;
        if (remainingMs > 0) {
          Thread.Sleep((int)remainingMs);
        }
      }
      nextFrameTimestamp += MinFrameTicks;
    }

    // Allocation-free replacement for inputs.All(i => i.Enabled): avoids the
    // per-visualizer delegate + enumerator that LINQ would create on the hot
    // scheduling path. Layer inputs come from the immutable render plan;
    // diagnostics continue to declare theirs directly on the visualizer.
    private static bool AllInputsEnabled(IReadOnlyList<Input> inputs) {
      for (int i = 0; i < inputs.Count; i++) {
        if (!inputs[i].Enabled) {
          return false;
        }
      }
      return true;
    }

    private static bool AllInputsEnabled(ImmutableArray<Input> inputs) {
      for (int i = 0; i < inputs.Length; i++) {
        if (!inputs[i].Enabled) {
          return false;
        }
      }
      return true;
    }

  }

}
