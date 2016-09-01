using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;
using System.Threading;
using System.Collections.Concurrent;
using System.Net;

namespace Spectrum.WhyFire {

  public class WhyFireOutput : Output {

    private Configuration config;
    private ConcurrentQueue<Uri> buffer;
    private List<Visualizer> visualizers;

    public WhyFireOutput(Configuration config) {
      this.config = config;
      this.buffer = new ConcurrentQueue<Uri>();
      this.visualizers = new List<Visualizer>();
    }

    private bool active;
    private Thread outputThread;
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
            if (this.config.whyFireOutputInSeparateThread) {
              this.outputThread = new Thread(OutputThread);
              this.outputThread.Start();
            }
          } else {
            if (this.outputThread != null) {
              this.outputThread.Abort();
              this.outputThread.Join();
              this.outputThread = null;
            }
            this.buffer = new ConcurrentQueue<Uri>();
          }
          this.active = value;
        }
      }
    }

    public bool Enabled {
      get {
        return this.config.whyFireEnabled;
      }
    }

    private void OutputThread() {
      while (true) {
        this.Update();
      }
    }

    private void Update() {
      int numMessages = this.buffer.Count;
      if (numMessages == 0) {
        return;
      }
      Uri[] messages = new Uri[numMessages];
      for (int i = 0; i < numMessages; i++) {
        bool result = this.buffer.TryDequeue(out messages[i]);
        if (!result) {
          throw new System.Exception("Someone else is dequeueing!");
        }
      }
      foreach (Uri message in messages) {
        new WebClient().DownloadStringAsync(message);
      }
    }

    public void OperatorUpdate() {
      if (!this.config.whyFireOutputInSeparateThread) {
        this.Update();
      }
    }

    public void RegisterVisualizer(Visualizer visualizer) {
      this.visualizers.Add(visualizer);
    }

    public Visualizer[] GetVisualizers() {
      return this.visualizers.ToArray();
    }

    public void FireEffect(int effect) {
      this.buffer.Enqueue(new Uri(this.config.whyFireURL + effect + "/fire"));
    }

    public void FireAll() {
      this.buffer.Enqueue(new Uri(this.config.whyFireURL + "/all"));
    }

    public void Winston() {
      this.buffer.Enqueue(new Uri(this.config.whyFireURL + "/winston"));
    }

    public void WhyNot() {
      this.buffer.Enqueue(new Uri(this.config.whyFireURL + "/ynot"));
    }

    public void StayOut() {
      this.buffer.Enqueue(new Uri(this.config.whyFireURL + "/rollcall"));
    }

    public void Alternate() {
      this.buffer.Enqueue(new Uri(this.config.whyFireURL + "/alternate"));
    }

    public void SweepRight() {
      this.buffer.Enqueue(new Uri(this.config.whyFireURL + "/sweepright"));
    }

    public void SweepLeft() {
      this.buffer.Enqueue(new Uri(this.config.whyFireURL + "/sweepleft"));
    }

  }

}