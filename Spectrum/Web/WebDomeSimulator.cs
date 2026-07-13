using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Spectrum.LEDs;

namespace Spectrum.Web {

  /**
   * Low-bandwidth browser port of DomeSimulatorWindow. Geometry is static JSON;
   * live frames are fixed-size binary RGB messages capped at 10 FPS by the dome
   * producer. One immutable packed frame is shared by every connected client.
   */
  public sealed class WebDomeSimulator {

    public sealed class GeometryView {
      public int pixelCount { get; set; }
      public IReadOnlyList<double[]> points { get; set; }
    }

    private readonly LEDDomeOutput dome;
    private readonly object gate = new object();
    private readonly int[] colors;
    private readonly int[] strutOffsets;
    private readonly GeometryView geometry;
    private byte[] packedFrame;
    private long sequence;
    private int clients;

    public WebDomeSimulator(LEDDomeOutput dome) {
      this.dome = dome;
      int numStruts = LEDDomeOutput.GetNumStruts();
      this.strutOffsets = new int[numStruts];
      var points = new List<double[]>();
      for (int strut = 0; strut < numStruts; strut++) {
        this.strutOffsets[strut] = points.Count;
        int count = LEDDomeOutput.GetNumLEDs(strut);
        for (int led = 0; led < count; led++) {
          var point = StrutLayoutFactory.GetProjectedLEDPoint(strut, led);
          points.Add(new[] { point.Item1, point.Item2 });
        }
      }
      this.colors = new int[points.Count];
      this.geometry = new GeometryView {
        pixelCount = points.Count,
        points = points,
      };
    }

    public GeometryView Geometry() => this.geometry;

    public async Task StreamAsync(HttpContext context) {
      if (!context.WebSockets.IsWebSocketRequest) {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
      }

      using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
      this.AddClient();
      using var linked = CancellationTokenSource.CreateLinkedTokenSource(
        context.RequestAborted);
      Task receiver = ReceiveUntilClosedAsync(socket, linked.Token);
      long seen = -1;
      try {
        while (socket.State == WebSocketState.Open && !receiver.IsCompleted) {
          byte[] frame = this.GetFrame(ref seen);
          if (frame != null) {
            await socket.SendAsync(
              new ArraySegment<byte>(frame),
              WebSocketMessageType.Binary,
              true,
              linked.Token);
          }
          await Task.Delay(100, linked.Token);
        }
      } catch (OperationCanceledException) {
      } catch (WebSocketException) {
      } finally {
        linked.Cancel();
        this.RemoveClient();
      }
    }

    private static async Task ReceiveUntilClosedAsync(
      WebSocket socket,
      CancellationToken cancellationToken
    ) {
      var buffer = new byte[1];
      try {
        while (socket.State == WebSocketState.Open) {
          WebSocketReceiveResult result = await socket.ReceiveAsync(
            new ArraySegment<byte>(buffer), cancellationToken);
          if (result.MessageType == WebSocketMessageType.Close) {
            await socket.CloseOutputAsync(
              WebSocketCloseStatus.NormalClosure, null, cancellationToken);
            return;
          }
        }
      } catch (OperationCanceledException) {
      } catch (WebSocketException) {
      }
    }

    private void AddClient() {
      lock (this.gate) {
        if (this.clients++ == 0) {
          Array.Clear(this.colors, 0, this.colors.Length);
          this.packedFrame = null;
          this.sequence++;
          this.dome.WebSimulatorHasConsumer = true;
        }
      }
    }

    private void RemoveClient() {
      lock (this.gate) {
        if (this.clients > 0 && --this.clients == 0) {
          this.dome.WebSimulatorHasConsumer = false;
        }
      }
    }

    // Pulls the producer's latest snapshot and ordered diagnostic commands into
    // one persistent color buffer, then packs RGB once for all browser clients.
    private byte[] GetFrame(ref long seen) {
      lock (this.gate) {
        bool redraw = false;
        if (this.dome.TryTakeWebSimulatorFrame(out int[] latest)) {
          try {
            Array.Copy(latest, this.colors, this.colors.Length);
            redraw = true;
          } finally {
            ArrayPool<int>.Shared.Return(latest);
          }
        }

        while (this.dome.WebSimulatorCommandQueue.TryDequeue(out var command)) {
          if (command.isFlush) {
            redraw = true;
            continue;
          }
          if (command.strutIndex < 0 ||
              command.strutIndex >= this.strutOffsets.Length) {
            continue;
          }
          int index = this.strutOffsets[command.strutIndex] + command.ledIndex;
          if (index >= this.strutOffsets[command.strutIndex] &&
              index < this.colors.Length) {
            this.colors[index] = command.color;
          }
        }

        if (redraw) {
          // Publish an immutable buffer. Other clients may still be awaiting a
          // SendAsync on the previous frame, so mutating it in place can corrupt
          // an in-flight WebSocket message. At 10 FPS these are small Gen-0
          // allocations (roughly 13 KB for this dome), not an accumulating queue.
          var packed = new byte[this.colors.Length * 3];
          for (int i = 0, p = 0; i < this.colors.Length; i++) {
            int color = this.colors[i];
            packed[p++] = (byte)(color >> 16);
            packed[p++] = (byte)(color >> 8);
            packed[p++] = (byte)color;
          }
          this.packedFrame = packed;
          this.sequence++;
        }
        if (this.packedFrame == null || seen == this.sequence) {
          return null;
        }
        seen = this.sequence;
        return this.packedFrame;
      }
    }
  }
}
