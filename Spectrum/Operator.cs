using System;
using System.Windows;
using System.Windows.Controls;
using System.Threading;
using Spectrum.Base;
using Spectrum.Audio;
using Spectrum.Hues;
using Spectrum.LEDs;
using System.Collections.Generic;

namespace Spectrum {

  class Operator {

    private Configuration config;
    private List<Input> inputs;
    private List<Output> outputs;
    private List<Visualizer> visualizers;

    public Operator(Configuration config) {
      this.config = config;

      this.inputs = new List<Input>();
      var audio = new AudioInput(config);
      this.inputs.Add(audio);

      this.outputs = new List<Output>();
      var hue = new HueOutput(config);
      this.outputs.Add(hue);
      var teensy = new CartesianTeensyOutput(config);
      this.outputs.Add(teensy);

      this.visualizers = new List<Visualizer>();
      this.visualizers.Add(new HueAudioVisualizer(
        this.config,
        audio,
        hue
      ));
      this.visualizers.Add(new LEDPanelVolumeVisualizer(
        this.config,
        audio,
        teensy
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
        List<Output> activeOutputs = new List<Output>();
        HashSet<Visualizer> activeVisualizers = new HashSet<Visualizer>();
        foreach (var output in this.outputs) {
          if (!output.Enabled) {
            output.Active = false;
            continue;
          }
          int topPri = 1;
          List<Visualizer> topPriVisualizers = new List<Visualizer>();
          foreach (var visualizer in output.GetVisualizers()) {
            int pri = visualizer.Priority;
            if (pri > topPri) {
              topPri = pri;
              topPriVisualizers.Clear();
              topPriVisualizers.Add(visualizer);
            } else if (pri == topPri) {
              topPriVisualizers.Add(visualizer);
            }
          }
          output.Active = topPriVisualizers.Count != 0;
          if (topPriVisualizers.Count != 0) {
            activeOutputs.Add(output);
          }
          activeVisualizers.UnionWith(topPriVisualizers);
        }

        foreach (var visualizer in visualizers) {
          visualizer.Enabled = activeVisualizers.Contains(visualizer);
        }

        HashSet<Input> activeInputs = new HashSet<Input>();
        foreach (var visualizer in activeVisualizers) {
          activeInputs.UnionWith(visualizer.GetInputs());
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