using System;
using System.Threading;
using Spectrum.Base;
using Spectrum.Audio;
using Spectrum.LEDs;
using Spectrum.MIDI;
using System.Collections.Generic;
using System.Diagnostics;
using System.ComponentModel;
using System.Collections.Immutable;
using Spectrum.Visualizers;

namespace Spectrum {

  public class Operator {

    private readonly Configuration config;
    private readonly List<Input> inputs;
    private readonly List<Output> outputs;
    private readonly List<Visualizer> visualizers;
    private readonly LayerCatalog layerCatalog;
    private readonly Dictionary<string, string> createdRendererByInstance =
      new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly Dictionary<string, LayerRendererRuntime>
      layerRuntimeByInstance =
        new Dictionary<string, LayerRendererRuntime>(StringComparer.Ordinal);
    private LayerStackSnapshot publishedLayerStack = LayerStackSnapshot.Empty;
    private int layerReconcilePending;

    // Exposed so diagnostic windows (e.g. the wand status display) can read the
    // live orientation-device state. Stable for the Operator's lifetime — Reboot
    // only toggles threads, it doesn't rebuild this instance.
    public OrientationInput OrientationInput { get; }
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

    public Operator(Configuration config) {
      this.config = config;
      this.BeatBroadcaster = new BeatBroadcaster(config);
      this.Telemetry = new RuntimeTelemetry();
      this.DomeCalibration = new DomeCalibrationState();

      this.frameRateStopwatch = new Stopwatch();
      this.frameRateStopwatch.Start();
      this.completedFramesInWindow = 0;

      this.inputs = new List<Input>();
      var audio = new AudioInput(config, this.BeatBroadcaster);
      this.inputs.Add(audio);
      var midi = new MidiInput(config, this.BeatBroadcaster);
      this.inputs.Add(midi);
      this.MidiLog = midi.MidiLog;
      var orientation = new OrientationInput(config);
      this.inputs.Add(orientation);
      this.OrientationInput = orientation;
      // Shared by every orientation-driven dome layer (Paintbrush, Ripple,
      // Stamp, Metaball) so they idle-drift around the same wandering point
      // instead of each running its own independent random walk.
      var orientationCenter = new OrientationCenter(config, orientation);

      this.outputs = new List<Output>();
      // orientationCenter doubles as the prism blends' live wand-angle source
      // (Follow Orientation), so ChromaticFringe/Iridescence can track the
      // spotlighted wand — see docs/prism.md.
      var dome = new LEDDomeOutput(
        config, this.Telemetry, this.BeatBroadcaster, orientationCenter);
      this.outputs.Add(dome);
      this.DomeOutput = dome;

      this.visualizers = new List<Visualizer>();
      this.visualizers.Add(new LEDDomeStrutIterationDiagnosticVisualizer(
        this.config,
        dome
      ));
      this.visualizers.Add(new LEDDomeFlashColorsDiagnosticVisualizer(
        this.config,
        dome
      ));
      this.visualizers.Add(new LEDDomeStrandTestDiagnosticVisualizer(
        this.config,
        dome
      ));
      this.visualizers.Add(new LEDDomeMappingCalibrationVisualizer(
        this.config,
        this.DomeCalibration,
        dome
      ));
      this.visualizers.Add(new LEDDomeFullColorFlashDiagnosticVisualizer(
        this.config,
        dome
      ));
      var rendererFactories = new Dictionary<
        string, Func<LayerRenderContext, ILayerRenderer>>(
          StringComparer.Ordinal) {
        ["volume"] = context => new LEDDomeVolumeVisualizer(
          this.config, context.Runtime, audio, this.BeatBroadcaster, dome),
        ["radial"] = context => new LEDDomeRadialVisualizer(
          this.config, context.Runtime, audio, this.BeatBroadcaster, dome),
        ["splat"] = context => new LEDDomeSplatVisualizer(
          this.config, context.Runtime, audio, this.BeatBroadcaster, dome),
        ["quaternion-test"] = context =>
          new LEDDomeQuaternionTestVisualizer(this.config, orientation, dome),
        ["quaternion-paintbrush"] = context =>
          new LEDDomeQuaternionPaintbrushVisualizer(
            this.config, context.Runtime, audio, orientation,
            orientationCenter, this.BeatBroadcaster, dome),
        ["race"] = context => new LEDDomeRaceVisualizer(
          this.config, context.Runtime, audio, midi,
          this.BeatBroadcaster, dome),
        ["snakes"] = context => new LEDDomeSnakesVisualizer(
          this.config, context.Runtime, dome),
        ["tv-static"] = context =>
          new LEDDomeTVStaticVisualizer(this.config, dome),
        ["twinkle"] = context => new LEDDomeTwinkleVisualizer(
          this.config, context.Runtime, dome),
        ["background"] = context => new LEDDomeBackgroundVisualizer(
          this.config, context.Runtime, dome),
        ["noise-cloud"] = context => new LEDDomeNoiseCloudVisualizer(
          this.config, context.Runtime, dome),
        ["vortex"] = context => new LEDDomeVortexVisualizer(
          this.config, context.Runtime, dome),
        ["caustics"] = context => new LEDDomeCausticsVisualizer(
          this.config, context.Runtime, audio, orientation,
          orientationCenter, this.BeatBroadcaster, dome),
        ["flash"] = context => new LEDDomeFlashVisualizer(
          this.config, context.Runtime, audio, orientation,
          this.BeatBroadcaster, dome),
        ["wave"] = context => new LEDDomeWaveVisualizer(
          this.config, context.Runtime, orientation, dome),
        ["gyroscope"] = context => new LEDDomeGyroscopeVisualizer(
          this.config, context.Runtime, orientation, orientationCenter, dome),
        ["ripple"] = context => new LEDDomeRippleVisualizer(
          this.config, context.Runtime, audio, orientation,
          orientationCenter, this.BeatBroadcaster, dome),
        ["stamp"] = context => new LEDDomeStampVisualizer(
          this.config, context.Runtime, audio, orientation,
          orientationCenter, this.BeatBroadcaster, dome),
        ["metaball"] = context => new LEDDomeMetaballVisualizer(
          this.config, context.Runtime, audio, orientation,
          orientationCenter, dome),
        ["point-cloud"] = context => new LEDDomePointCloudVisualizer(
          this.config, context.Runtime, orientation, dome),
        ["shooting-star"] = context => new LEDDomeShootingStarVisualizer(
          this.config, context.Runtime, audio, orientation,
          orientationCenter, this.BeatBroadcaster, dome),
        ["sparkler"] = context => new LEDDomeSparklerVisualizer(
          this.config, context.Runtime, audio, orientation,
          orientationCenter, this.BeatBroadcaster, dome),
      };
      this.layerCatalog = LayerCatalog.Default.WithFactories(rendererFactories);
      foreach (LayerDefinition definition in this.layerCatalog.Definitions) {
        if (definition.CreateRenderer == null) {
          throw new InvalidOperationException(
            "No renderer factory registered for layer " + definition.Id);
        }
      }
      this.config.PropertyChanged += this.OnLayerConfigurationChanged;
      this.CapturePublishedLayerStack();
      this.ReconcileLayerVisualizers();
      Interlocked.Exchange(ref this.layerReconcilePending, 0);
    }

    private void OnLayerConfigurationChanged(
      object sender, PropertyChangedEventArgs e
    ) {
      if (e.PropertyName == nameof(this.config.domeLayerStack)) {
        // SpectrumConfiguration compiled this immutable value before raising
        // PropertyChanged. Alternate Configuration implementations are frozen
        // synchronously here, on their publishing thread, as a compatibility
        // fallback; ReconcileLayerVisualizers never reads serializer DTOs.
        this.CapturePublishedLayerStack();
        Interlocked.Exchange(ref this.layerReconcilePending, 1);
      }
    }

    private void CapturePublishedLayerStack() {
      LayerStackSnapshot snapshot =
        this.config is ILayerStackSnapshotSource source
          ? source.DomeLayerStackSnapshot
          : LayerStackService.SnapshotFor(this.config.domeLayerStack);
      Volatile.Write(
        ref this.publishedLayerStack, snapshot ?? LayerStackSnapshot.Empty);
    }

    private void ReconcileLayerVisualizers() {
      LayerStackSnapshot snapshot = Volatile.Read(
        ref this.publishedLayerStack) ?? LayerStackSnapshot.Empty;
      foreach (LayerSnapshot layer in snapshot.Layers) {
        string instanceId = layer.Id.Value;
        if (this.createdRendererByInstance.TryGetValue(
              instanceId, out string createdRenderer) &&
            createdRenderer == layer.RendererId &&
            this.layerRuntimeByInstance.TryGetValue(
              instanceId, out LayerRendererRuntime existingRuntime)) {
          existingRuntime.Publish(layer);
          continue;
        }
        LayerDefinition definition = this.layerCatalog.Get(
          layer.RendererId);
        if (definition?.CreateRenderer == null) {
          continue;
        }
        var runtime = new LayerRendererRuntime(layer);
        using (LayerInstanceScope.Push(instanceId)) {
          ILayerRenderer renderer = definition.CreateRenderer(
            new LayerRenderContext(layer.Id, layer, runtime: runtime));
          if (renderer is not Visualizer visualizer) {
            throw new InvalidOperationException(
              "Layer renderer must implement Visualizer: " + definition.Id);
          }
          this.visualizers.Add(visualizer);
        }
        this.createdRendererByInstance[instanceId] = layer.RendererId;
        this.layerRuntimeByInstance[instanceId] = runtime;
      }
      this.DomeOutput.PublishLayerStack(snapshot);
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
      lock (this.visualizers) {
        if (this.Enabled) {
          this.Enabled = false;
          this.Enabled = true;
        }
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

        if (Interlocked.Exchange(ref this.layerReconcilePending, 0) != 0) {
          this.ReconcileLayerVisualizers();
        }

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
            IReadOnlyList<Input> requiredInputs;
            if (isLayerVisualizer) {
              if (!this.plannedLayerInputs.TryGetValue(
                    visualizer, out ImmutableArray<Input> plannedInputs)) {
                continue;
              }
              requiredInputs = plannedInputs;
            } else {
              requiredInputs = visualizer.GetInputs();
            }
            // We can only consider a visualizer if all its inputs are enabled
            if (!AllInputsEnabled(requiredInputs)) {
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
          this.activeVisualizers.UnionWith(this.topPriVisualizers);
        }

        this.activeInputs.Clear();
        foreach (var visualizer in this.activeVisualizers) {
          IReadOnlyList<Input> requiredInputs =
            this.plannedLayerInputs.TryGetValue(
              visualizer, out ImmutableArray<Input> plannedInputs)
                ? plannedInputs : visualizer.GetInputs();
          foreach (var input in requiredInputs) {
            this.activeInputs.Add(input);
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
        this.OrientationInput.BeginOperatorFrame();

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

  }

}
