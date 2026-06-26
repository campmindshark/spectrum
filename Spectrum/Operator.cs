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

  class Operator {

    private readonly Configuration config;
    private readonly List<Input> inputs;
    private readonly List<Output> outputs;
    private readonly List<Visualizer> visualizers;
    private readonly Stopwatch frameRateStopwatch;
    private int framesThisSecond;

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

      this.outputs = new List<Output>();
      var dome = new LEDDomeOutput(config);
      this.outputs.Add(dome);
      var bar = new LEDBarOutput(config, dome);
      this.outputs.Add(bar);
      var stage = new LEDStageOutput(config);
      this.outputs.Add(stage);

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
      this.visualizers.Add(new LEDBarFlashColorsDiagnosticVisualizer(
        this.config,
        bar
      ));
      this.visualizers.Add(new LEDStageFlashColorsDiagnosticVisualizer(
        this.config,
        stage
      ));
      this.visualizers.Add(new LEDStageTracerVisualizer(
        this.config,
        stage
      ));
      this.visualizers.Add(new LEDStageDepthLevelVisualizer(
        this.config,
        audio,
        stage
      ));
    }

    private bool enabled;
    private Thread operatorThread;
    // Cooperative stop flag for OperatorThread, replacing Thread.Abort().
    private volatile bool operatorThreadStop;
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
      while (!this.operatorThreadStop) {
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
          input.OperatorUpdate();
        }

        foreach (var visualizer in activeVisualizers) {
          visualizer.Visualize();
        }

        foreach (var output in activeOutputs) {
          output.OperatorUpdate();
        }
      }
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
