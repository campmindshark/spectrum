using System;
using System.Threading;
using Spectrum.Base;
using Spectrum.Audio;
using Spectrum.LEDs;
using Spectrum.MIDI;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Spectrum.Visualizers;

namespace Spectrum {

  public class Operator {

    private readonly Configuration config;
    private readonly List<Input> inputs;
    private readonly List<Output> outputs;
    private readonly List<Visualizer> visualizers;

    // Exposed so diagnostic windows (e.g. the wand status display) can read the
    // live orientation-device state. Stable for the Operator's lifetime — Reboot
    // only toggles threads, it doesn't rebuild this instance.
    public OrientationInput OrientationInput { get; }
    private readonly Stopwatch frameRateStopwatch;
    private int framesThisSecond;

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

    // Visualizers that have thrown too many times are quarantined: the
    // scheduling pass skips them so a consistently-broken visualizer can't take
    // the output down every frame (and can't spam the log). Keyed by visualizer,
    // valued by cumulative throw count; once the count crosses
    // VisualizerQuarantineThreshold the visualizer is treated as unavailable.
    // Only touched on the operator thread, so it needs no synchronization.
    private const int VisualizerQuarantineThreshold = 10;
    private readonly Dictionary<Visualizer, int> visualizerFailureCounts =
      new Dictionary<Visualizer, int>();

    public Operator(Configuration config) {
      this.config = config;

      this.frameRateStopwatch = new Stopwatch();
      this.frameRateStopwatch.Start();
      this.framesThisSecond = 0;

      this.inputs = new List<Input>();
      var audio = new AudioInput(config);
      this.inputs.Add(audio);
      var midi = new MidiInput(config);
      this.inputs.Add(midi);
      var orientation = new OrientationInput(config);
      this.inputs.Add(orientation);
      this.OrientationInput = orientation;

      this.outputs = new List<Output>();
      var dome = new LEDDomeOutput(config);
      this.outputs.Add(dome);

      this.visualizers = new List<Visualizer>();
      this.visualizers.Add(new LEDDomeMidiTestVisualizer(
        this.config,
        midi,
        dome
      ));
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
        dome
      ));
      this.visualizers.Add(new LEDDomeFullColorFlashDiagnosticVisualizer(
        this.config,
        dome
      ));
      this.visualizers.Add(new LEDDomeVolumeVisualizer(
        this.config,
        audio,
        dome
      ));
      this.visualizers.Add(new LEDDomeRadialVisualizer(
        this.config,
        audio,
        dome
      ));
      this.visualizers.Add(new LEDDomeSplatVisualizer(
        this.config,
        audio,
        dome
      ));
      this.visualizers.Add(new LEDDomeQuaternionTestVisualizer(
        this.config,
        orientation,
        dome
        ));
      this.visualizers.Add(new LEDDomeQuaternionMultiTestVisualizer(
        this.config,
        orientation,
        dome
        ));
      this.visualizers.Add(new LEDDomeQuaternionPaintbrushVisualizer(
        this.config,
        audio,
        orientation,
        dome
        ));
      this.visualizers.Add(new LEDDomeRaceVisualizer(
        this.config,
        audio,
        midi,
        dome
      ));
      this.visualizers.Add(new LEDDomeSnakesVisualizer(
        this.config,
        audio,
        dome
      ));
      this.visualizers.Add(new LEDDomeTVStaticVisualizer(
        this.config,
        dome
      ));
      this.visualizers.Add(new LEDDomeFlashVisualizer(
        this.config,
        audio,
        midi,
        dome
      ));
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
              input.Active = false;
            }
            foreach (var output in this.outputs) {
              output.Active = false;
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
      // Timestamp this frame is allowed to start, advanced by one frame budget
      // each tick to cap the loop at MaxFramesPerSecond.
      long nextFrameTimestamp = Stopwatch.GetTimestamp();
      while (!this.operatorThreadStop) {
        ThrottleFrame(ref nextFrameTimestamp);

        if (this.frameRateStopwatch.ElapsedMilliseconds >= 1000) {
          this.frameRateStopwatch.Restart();
          this.config.operatorFPS = this.framesThisSecond;
          this.framesThisSecond = 0;
        }
        this.framesThisSecond++;

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
            // We can only consider a visualizer if all its inputs are enabled
            if (!AllInputsEnabled(visualizer)) {
              continue;
            }
            // Skip visualizers that have been quarantined for throwing too
            // often, so the output falls through to a lower-priority tier
            // instead of going dark every frame.
            if (this.IsQuarantined(visualizer)) {
              continue;
            }
            int pri = visualizer.Priority;
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
          foreach (var input in visualizer.GetInputs()) {
            this.activeInputs.Add(input);
          }
        }
        foreach (var input in this.inputs) {
          if (input.Enabled && input.AlwaysActive) {
            this.activeInputs.Add(input);
          }
        }

        foreach (var output in this.outputs) {
          output.Active = activeOutputs.Contains(output);
        }
        foreach (var visualizer in this.visualizers) {
          visualizer.Enabled = activeVisualizers.Contains(visualizer);
        }
        foreach (var input in this.inputs) {
          input.Active = activeInputs.Contains(input);
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

        foreach (var visualizer in activeVisualizers) {
          try {
            visualizer.Visualize();
          } catch (Exception e) {
            // Keep the engine running even if one visualizer throws mid-frame
            // (see findings 1-2 in docs/viz_issues.md for reachable render-path
            // throws). Repeat offenders are quarantined so they stop taking the
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

    // Allocation-free replacement for GetInputs().All(i => i.Enabled): avoids
    // the per-visualizer delegate + array enumerator that LINQ would create on
    // the hot scheduling path.
    private static bool AllInputsEnabled(Visualizer visualizer) {
      foreach (var input in visualizer.GetInputs()) {
        if (!input.Enabled) {
          return false;
        }
      }
      return true;
    }

  }

}
