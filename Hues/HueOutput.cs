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

  public class HueOutput : Output {

    public struct HueCommand {

      private static JsonSerializerSettings jsonSettings
        = new JsonSerializerSettings {
          NullValueHandling = NullValueHandling.Ignore
        };

      public bool? on;
      public int? brightness;
      public int? hue;
      public int? saturation;
      public int? transitiontime;
      public string alert;
      public string effect;

      public string ToJson() {
        return JsonConvert.SerializeObject(this, HueCommand.jsonSettings);
      }

    }

    private struct HueMessage {
      public Uri uri;
      public HueCommand command;
    }

    private Configuration config;
    private ConcurrentQueue<HueMessage> buffer;

    public HueOutput(Configuration config) {
      this.config = config;
      this.buffer = new ConcurrentQueue<HueMessage>();
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
            this.outputThread = new Thread(OutputThread);
            this.outputThread.Start();
          } else {
            this.outputThread.Abort();
            this.outputThread.Join();
          }
          this.enabled = value;
        }
      }
    }

    private void OutputThread() {
      HueMessage message;
      while (true) {
        bool result = this.buffer.TryDequeue(out message);
        if (!result) {
          continue;
        }
        new WebClient().UploadStringAsync(
          message.uri,
          "PUT",
          message.command.ToJson()
        );
        Thread.Sleep(100);
      }
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
