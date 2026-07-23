using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Spectrum.Base;

namespace Spectrum.Web {

  /**
   * Server-Sent Events route ownership. Subscription, initial-state capture,
   * streaming, disconnect handling, and unsubscription stay together.
   */
  internal sealed class EventApiRoutes {

    private readonly ControlService controls;
    private readonly ConfigEventStream events;

    internal EventApiRoutes(
      ControlService controls, ConfigEventStream events
    ) {
      this.controls = controls;
      this.events = events;
    }

    internal void Map(WebApplication app) {
      app.MapGet("/api/events", (HttpContext context) =>
        this.StreamAsync(context, ControlRole.User));
      app.MapGet("/api/maintenance/events", (HttpContext context) =>
        this.StreamAsync(context, ControlRole.Maintenance));
    }

    private async Task StreamAsync(
      HttpContext context, ControlRole role
    ) {
      context.Response.Headers["Content-Type"] = "text/event-stream";
      context.Response.Headers["Cache-Control"] = "no-cache";
      context.Response.Headers["X-Accel-Buffering"] = "no";

      ConfigEventStream.Subscriber subscriber =
        this.events.Subscribe(role, out Guid id);
      try {
        await context.Response.WriteAsync(": connected\n\n");
        List<string> initialFrames = await this.controls.CaptureAsync(
          this.events.InitialStateFrames);
        foreach (string frame in initialFrames) {
          await context.Response.WriteAsync(
            $"data: {frame}\n\n", context.RequestAborted);
        }
        await context.Response.Body.FlushAsync(context.RequestAborted);

        await foreach (string message in
            subscriber.Reader.ReadAllAsync(context.RequestAborted)) {
          await context.Response.WriteAsync(
            $"data: {message}\n\n", context.RequestAborted);
          await context.Response.Body.FlushAsync(context.RequestAborted);
        }
      } catch (OperationCanceledException) {
        // Client disconnected; cleanup still runs.
      } finally {
        this.events.Unsubscribe(id);
      }
    }
  }
}
