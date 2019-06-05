using System;
using System.Windows;
using System.Windows.Controls;
using System.Threading;
using Spectrum.Base;
using Spectrum.Audio;
using Spectrum.Hues;
using Spectrum.LEDs;
using Spectrum.MIDI;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace Spectrum {

  class Operator {

    private readonly Configuration config;
    private readonly List<Input> inputs;
    private readonly List<Output> outputs;
    private readonly List<Visualizer> visualizers;
    private readonly Stopwatch operatorThreadBlockingStopwatch;
    private readonly Stopwatch frameRateStopwatch;
    private int framesThisSecond;

    public Operator(Configuration config) {
      this.config = config;
      this.operatorThreadBlockingStopwatch = new Stopwatch();
      this.operatorThreadBlockingStopwatch.Start();

      this.frameRateStopwatch = new Stopwatch();
      this.frameRateStopwatch.Start();
      this.framesThisSecond = 0;

      this.inputs = new List<Input>();
      var audio = new AudioInput(config);
      this.inputs.Add(audio);
      var midi = new MidiInput(config);
      this.inputs.Add(midi);

      this.outputs = new List<Output>();
      var hue = new HueOutput(config);
      this.outputs.Add(hue);
      var board = new LEDBoardOutput(config);
      this.outputs.Add(board);
      var dome = new LEDDomeOutput(config);
      this.outputs.Add(dome);
      var bar = new LEDBarOutput(config, dome);
      this.outputs.Add(bar);
      var stage = new LEDStageOutput(config);
      this.outputs.Add(stage);

      this.visualizers = new List<Visualizer>();
      this.visualizers.Add(new HueAudioVisualizer(
        this.config,
        audio,
        hue
      ));
      this.visualizers.Add(new LEDPanelVolumeVisualizer(
        this.config,
        audio,
        board
      ));
      this.visualizers.Add(new HueSolidColorVisualizer(
        this.config,
        hue
      ));
      this.visualizers.Add(new HueSilentVisualizer(
        this.config,
        audio,
        hue
      ));
      this.visualizers.Add(new LEDPanelMidiVisualizer(
        this.config,
        midi,
        board
      ));
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
      this.visualizers.Add(new LEDDomeVolumeVisualizer(
        this.config,
        audio,
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
            this.operatorThread = new Thread(OperatorThread);
            this.operatorThread.Start();
          } else {
            this.operatorThread.Abort();
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
      while (true) {
        if (this.operatorThreadBlockingStopwatch.ElapsedMilliseconds < 1) {
          Thread.Sleep(1);
        }
        this.operatorThreadBlockingStopwatch.Restart();

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
        List<Output> activeOutputs = new List<Output>();
        HashSet<Visualizer> activeVisualizers = new HashSet<Visualizer>();
        foreach (var output in this.outputs) {
          if (!output.Enabled) {
            continue;
          }
          int topPri = 1;
          List<Visualizer> topPriVisualizers = new List<Visualizer>();
          List<Visualizer> alwaysRunVisualizers = new List<Visualizer>();
          foreach (var visualizer in output.GetVisualizers()) {
            // We can only consider a visualizer if all its inputs are enabled
            bool allInputsEnabled = visualizer.GetInputs().All(
              input => input.Enabled
            );
            if (!allInputsEnabled) {
              continue;
            }
            int pri = visualizer.Priority;
            bool canAdd = false;
            if (pri == -1) {
              alwaysRunVisualizers.Add(visualizer);
            } else if (pri > topPri) {
              topPri = pri;
              topPriVisualizers.Clear();
              canAdd = true;
            } else if (pri == topPri) {
              canAdd = true;
            }
            if (!canAdd) {
              continue;
            }
            if (visualizer.GetInputs().All(input => input.Enabled)) {
              topPriVisualizers.Add(visualizer);
            }
          }
          topPriVisualizers.AddRange(alwaysRunVisualizers);
          if (topPriVisualizers.Count != 0) {
            activeOutputs.Add(output);
          }
          activeVisualizers.UnionWith(topPriVisualizers);
        }

        HashSet<Input> activeInputs = new HashSet<Input>();
        foreach (var visualizer in activeVisualizers) {
          activeInputs.UnionWith(visualizer.GetInputs());
        }
        activeInputs.UnionWith(
          this.inputs.Where(input => input.Enabled && input.AlwaysActive)
        );

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

  }

}