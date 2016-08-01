using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;
using System.Threading;
using System.Collections.Concurrent;
using System.Net;
using Newtonsoft.Json;

namespace Spectrum.Hues {

  public struct HueCommand {

    private static JsonSerializerSettings jsonSettings
      = new JsonSerializerSettings {
        NullValueHandling = NullValueHandling.Ignore
      };

    public bool? on;
    public int? bri;
    public int? hue;
    public int? sat;
    public int? transitiontime;
    public string alert;
    public string effect;

    public string ToJson() {
      return JsonConvert.SerializeObject(this, HueCommand.jsonSettings);
    }

  }

  public class HueOutput : Output {

    private struct HueMessage {
      public Uri uri;
      public HueCommand command;
    }

    private Configuration config;
    private ConcurrentQueue<HueMessage> buffer;
    private List<Visualizer> visualizers;

    public HueOutput(Configuration config) {
      this.config = config;
      this.buffer = new ConcurrentQueue<HueMessage>();
      this.visualizers = new List<Visualizer>();
    }

    private bool enabled;
    private Thread outputThread;
    public bool Enabled {
      get {
        lock (this.buffer) {
          return this.enabled;
        }
      }
      set {
        lock (this.buffer) {
          if (this.enabled == value) {
            return;
          }
          if (value) {
            if (this.config.hueOutputInSeparateThread) {
              this.outputThread = new Thread(OutputThread);
              this.outputThread.Start();
            }
          } else {
            if (this.outputThread != null) {
              this.outputThread.Abort();
              this.outputThread.Join();
              this.outputThread = null;
            }
            this.buffer = new ConcurrentQueue<HueMessage>();
          }
          this.enabled = value;
        }
      }
    }

    private void OutputThread() {
      while (true) {
        this.Update();
      }
    }

    public int BufferSize {
      get {
        return this.buffer.Count;
      }
    }

    private void Update() {
      HueMessage message;
      bool result = this.buffer.TryDequeue(out message);
      if (!result) {
        return;
      }
      new WebClient().UploadStringAsync(
        message.uri,
        "PUT",
        message.command.ToJson()
      );
      // Sadly, we need to make sure that we don't
      // update the Hues more than 10 times a second.
      Thread.Sleep(100);
    }

    public void OperatorUpdate() {
      if (!this.config.hueOutputInSeparateThread) {
        this.Update();
      }
    }

    public void RegisterVisualizer(Visualizer visualizer) {
      this.visualizers.Add(visualizer);
    }

    public Visualizer[] GetVisualizers() {
      return this.visualizers.ToArray();
    }

    /**
     * The groupIndex given here is the same groupIndex the Hue knows about.
     */
    public void SendGroupCommand(int groupIndex, HueCommand command) {
      string url = this.config.hueURL + "groups/" + groupIndex + "/action/";
      this.buffer.Enqueue(new HueMessage() {
        command = command,
        uri = new Uri(url),
      });
    }

    /**
     * Note: the lightIndex given here is local, and is mapped to the actual
     * index the Hue uses through this.config.hueIndices.
     */
    public void SendLightCommand(int lightIndex, HueCommand command) {
      string url = this.config.hueURL
        + "lights/"
        + this.config.hueIndices[lightIndex]
        + "/state/";
      string message = command.ToJson();
      this.buffer.Enqueue(new HueMessage() {
        command = command,
        uri = new Uri(url),
      });
    }

  }

}
