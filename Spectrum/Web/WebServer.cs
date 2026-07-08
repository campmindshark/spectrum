using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectrum.Base;

namespace Spectrum.Web {

  /**
   * The in-process Kestrel host for the web control surface. Runs inside the
   * WPF process, sharing the one Configuration instance (via ControlService).
   *
   * Two scopes, split only by which parameters they expose — there is no
   * authentication of any kind (the installation LAN is trusted):
   *   - user  — the curated VJ knobs (ControlRole.User)
   *   - maint — everything (ControlRole.Maintenance)
   */
  public sealed class WebServer {

    private readonly ControlService controls;
    private readonly ConfigEventStream events;
    private readonly AdvisoryLockManager locks;
    private readonly DomeCalibrationController calibration;
    private readonly WandStatusController wands;
    private readonly OperatorController operatorControl;
    private readonly TempoController tempo;
    private readonly LayersController layers;
    private readonly int port;
    private WebApplication app;

    // Advisory-lock lease token, carried on writes to a locked resource and on
    // every calibration action.
    private const string LockTokenHeader = "X-Spectrum-Lock-Token";

    // Watchdog that reconciles the modal dome-calibration flow with its advisory
    // lease. The native DomeMappingWindow always deactivates calibration in its
    // WindowClosed handler; the web flow has no such guaranteed hook. A client
    // that navigates away, closes the tab, or drops off the LAN only releases
    // the lease (app.js pagehide) or lets it lapse on TTL — it never cancels the
    // flow. Left unreconciled, DomeCalibrationState.Active stays true and
    // LEDDomeMappingCalibrationVisualizer keeps its priority-10000 override,
    // seizing the dome so no normal visualizer runs again. This sweep restores
    // the native contract: once nobody holds the domeCalibration lease, a
    // still-active flow is auto-cancelled and the dome hands back to the normal
    // visualizers.
    private Timer calibrationWatchdog;
    private static readonly TimeSpan CalibrationWatchdogInterval =
      TimeSpan.FromSeconds(3);

    public WebServer(
      ControlService controls,
      ConfigEventStream events,
      AdvisoryLockManager locks,
      DomeCalibrationController calibration,
      WandStatusController wands,
      OperatorController operatorControl,
      TempoController tempo,
      LayersController layers,
      int port
    ) {
      this.controls = controls;
      this.events = events;
      this.locks = locks;
      this.calibration = calibration;
      this.wands = wands;
      this.operatorControl = operatorControl;
      this.tempo = tempo;
      this.layers = layers;
      this.port = port;
    }

    // Builds and starts Kestrel on a background thread. Returns once the host
    // has begun listening. Safe to call once.
    public void Start() {
      var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
        // The WPF app's base dir; wwwroot lives beside the exe.
        ContentRootPath = AppContext.BaseDirectory,
      });
      // Don't let ASP.NET Core hijack process-wide concerns from the WPF app.
      builder.Logging.ClearProviders();
      builder.WebHost.UseKestrel();
      // Listen on all interfaces so phones on the LAN can reach it.
      builder.WebHost.UseUrls($"http://0.0.0.0:{this.port}");

      this.app = builder.Build();

      this.app.UseDefaultFiles();
      this.app.UseStaticFiles();

      this.MapApi(this.app);

      // RunAsync blocks until shutdown; fire it on a background task so the WPF
      // message loop keeps running. StartAsync() would also work, but RunAsync
      // keeps the host alive until StopAsync is called on close.
      this.app.RunAsync();

      this.calibrationWatchdog = new Timer(
        this.ReconcileCalibrationLease, null,
        CalibrationWatchdogInterval, CalibrationWatchdogInterval);
    }

    public async Task StopAsync() {
      if (this.calibrationWatchdog != null) {
        await this.calibrationWatchdog.DisposeAsync();
        this.calibrationWatchdog = null;
      }
      if (this.app != null) {
        await this.app.StopAsync(TimeSpan.FromSeconds(2));
        await this.app.DisposeAsync();
        this.app = null;
      }
    }

    // Timer callback: if the calibration flow is still active but nobody holds
    // its lease anymore (released on the client leaving, or lapsed on TTL), the
    // driving client is gone — cancel the flow so the dome returns to the normal
    // visualizers. Best-effort; any failure is retried on the next tick.
    private async void ReconcileCalibrationLease(object _) {
      try {
        var state = this.calibration.State();
        if (!state.active && !state.reviewing) {
          return;
        }
        if (this.locks.Get(LockPolicy.DomeCalibration) != null) {
          return; // a client still holds the lease and is driving the flow
        }
        await this.calibration.CancelAsync();
      } catch {
        // Swallow: this runs on a pool thread with no caller to observe it, and
        // the next tick retries.
      }
    }

    private void MapApi(WebApplication app) {
      // ---- Global engine on/off (the Start/Stop button) ----
      // Not scoped or role-gated: it's the one switch the whole installation
      // shares, exposed to every surface exactly like the native power button.
      // Unlocked — starting/stopping is idempotent and momentary, so (like the
      // native button) it takes no advisory lease.
      app.MapGet("/api/operator", () =>
        Results.Json(this.operatorControl.State()));

      app.MapPut("/api/operator", async (OperatorBody body) => {
        if (body == null) {
          return Results.BadRequest(new { error = "body must be {\"enabled\": true|false}" });
        }
        return Results.Json(await this.operatorControl.SetEnabledAsync(body.enabled));
      });

      // ---- User scope ----
      app.MapGet("/api/parameters", (HttpContext ctx) =>
        Results.Json(this.controls.Describe(ControlRole.User)));

      app.MapGet("/api/parameters/{key}", (string key) => {
        ParameterView view = this.controls.Read(key, ControlRole.User);
        return view == null ? Results.NotFound() : Results.Json(view);
      });

      app.MapPut("/api/parameters/{key}", (string key, HttpContext ctx) =>
        this.HandleWrite(key, ctx, ControlRole.User));

      // ---- Dome layer stack (user scope — it replaces the old domeActiveVis
      // selector, which was user-level). Whole-stack last-write-wins: the client
      // always PUTs its full edited copy, so no advisory lease is needed. The
      // result is broadcast on the SSE "layers" frame, converging every client.
      app.MapGet("/api/layers", () => Results.Json(this.layers.State()));

      app.MapPut("/api/layers", async (LayersBody body) => {
        (bool ok, string error) = await this.layers.ReplaceAsync(body?.layers);
        return ok
          ? Results.Json(this.layers.State())
          : Results.BadRequest(new { error });
      });

      // Read-only wand status for the user surface's slimmed-down "Wand status"
      // box (ID/Type/Motion/Quality). Wraps the same row snapshot the
      // maintenance table polls together with the current spotlight value, so
      // the user page can reflect which spotlight radio is selected (the rows
      // alone can't distinguish -1/all from -2/idle). Exposed under the user
      // namespace so the user page never reaches into /api/maintenance/*.
      app.MapGet("/api/wands", () =>
        Results.Json(new {
          spotlight = this.wands.CurrentSpotlight(),
          rows = this.wands.Snapshot(),
        }));

      // Set the orientation "spotlight" wand from the user surface's wand view
      // (one radio per connected device). deviceId = -1 clears the spotlight so
      // every wand renders again; -2 forces the dome idle (every wand ignored,
      // screen-saver on). Momentary config write, marshaled through the gateway
      // like the native VJ HUD text box — no advisory lease.
      app.MapPost("/api/wands/spotlight", (SpotlightBody body) => {
        if (body == null) {
          return Results.BadRequest(
            new { error = "body must be {\"deviceId\": <int>}" });
        }
        this.wands.SetSpotlight(body.deviceId);
        return Results.Ok();
      });

      // ---- Maintenance scope (same host, just the full parameter set) ----
      app.MapGet("/api/maintenance/parameters", (HttpContext ctx) =>
        Results.Json(this.controls.Describe(ControlRole.Maintenance)));

      app.MapGet("/api/maintenance/parameters/{key}", (string key) => {
        ParameterView view = this.controls.Read(key, ControlRole.Maintenance);
        return view == null ? Results.NotFound() : Results.Json(view);
      });

      app.MapPut("/api/maintenance/parameters/{key}", (string key, HttpContext ctx) =>
        this.HandleWrite(key, ctx, ControlRole.Maintenance));

      // ---- Advisory locks for modal ops ----
      app.MapGet("/api/maintenance/locks", () =>
        Results.Json(this.locks.ActiveLocks()));

      app.MapPost("/api/maintenance/locks/{resource}", async (string resource, HttpContext ctx) => {
        string holderName = "operator";
        try {
          AcquireBody body = await ctx.Request.ReadFromJsonAsync<AcquireBody>();
          if (!string.IsNullOrWhiteSpace(body?.holderName)) {
            holderName = body.holderName;
          }
        } catch (JsonException) { /* empty body is fine */ }

        string token = this.locks.TryAcquire(resource, holderName, out var current);
        if (token == null) {
          // Held by someone else.
          return Results.Json(new { error = "locked", holder = current },
            statusCode: StatusCodes.Status423Locked);
        }
        return Results.Json(new { token, holder = current });
      });

      app.MapPost("/api/maintenance/locks/{resource}/heartbeat", (string resource, HttpContext ctx) => {
        string token = ctx.Request.Headers[LockTokenHeader];
        return this.locks.TryRenew(resource, token)
          ? Results.Ok()
          : Results.Json(new { error = "not holder" }, statusCode: StatusCodes.Status409Conflict);
      });

      app.MapDelete("/api/maintenance/locks/{resource}", (string resource, HttpContext ctx) => {
        string token = ctx.Request.Headers[LockTokenHeader];
        this.locks.TryRelease(resource, token);
        return Results.Ok();
      });

      // ---- Dome mapping calibration (modal — guarded by the domeCalibration
      // lease, which the client must hold before driving the flow) ----
      // Each mutating action binds the lease token from the header as a route
      // parameter, so the handler is a proper route handler whose returned
      // IResult is written to the response (a lone-HttpContext handler returning
      // Task<IResult> would instead bind as a RequestDelegate and silently drop
      // the body — ASP0016).
      app.MapGet("/api/maintenance/calibration", () =>
        Results.Json(this.calibration.State()));

      // Static dome-diagram geometry (projected strut segments + labels/colors
      // per endpoint) the client draws the clickable diagram from. Unguarded and
      // read-only — it's the same fixed layout regardless of flow state.
      app.MapGet("/api/maintenance/calibration/geometry", () =>
        Results.Json(this.calibration.Geometry()));

      app.MapPost("/api/maintenance/calibration/start",
        ([FromHeader(Name = LockTokenHeader)] string token) =>
          this.RunCalibration(token, c => c.StartAsync()));

      app.MapPost("/api/maintenance/calibration/load",
        ([FromHeader(Name = LockTokenHeader)] string token) =>
          this.RunCalibration(token, c => c.LoadAsync()));

      app.MapPost("/api/maintenance/calibration/pick",
        ([FromHeader(Name = LockTokenHeader)] string token, PickBody body) =>
          this.RunCalibration(token, c => c.PickAsync(body?.endpoint ?? -1)));

      app.MapPost("/api/maintenance/calibration/skip",
        ([FromHeader(Name = LockTokenHeader)] string token) =>
          this.RunCalibration(token, c => c.SkipAsync()));

      app.MapPost("/api/maintenance/calibration/back",
        ([FromHeader(Name = LockTokenHeader)] string token) =>
          this.RunCalibration(token, c => c.BackAsync()));

      app.MapPost("/api/maintenance/calibration/restart",
        ([FromHeader(Name = LockTokenHeader)] string token) =>
          this.RunCalibration(token, c => c.RestartAsync()));

      app.MapPost("/api/maintenance/calibration/swap",
        ([FromHeader(Name = LockTokenHeader)] string token, SwapBody body) =>
          this.RunCalibration(token, c => c.SwapAsync(body?.a ?? -1, body?.b ?? -1)));

      app.MapPost("/api/maintenance/calibration/cancel",
        ([FromHeader(Name = LockTokenHeader)] string token) =>
          this.RunCalibration(token, c => c.CancelAsync()));

      app.MapPost("/api/maintenance/calibration/save",
        async ([FromHeader(Name = LockTokenHeader)] string token) => {
          if (!this.locks.HoldsLock(LockPolicy.DomeCalibration, token)) {
            return this.CalibrationLocked();
          }
          (bool ok, string error, var state) = await this.calibration.SaveAsync();
          return ok
            ? Results.Json(state)
            : Results.BadRequest(new { error, state });
        });

      // ---- Wand status (read-only orientation-device diagnostics, polled by
      // the client). Calibrate is a momentary global action, unguarded like the
      // native Calibrate All button. ----
      app.MapGet("/api/maintenance/wands", () =>
        Results.Json(this.wands.Snapshot()));

      // Serial (USB-CDC ESP-NOW) receiver: port list, selection, and liveness.
      // Port selection uses the existing PUT .../parameters/wandSerialPort.
      app.MapGet("/api/maintenance/wands/serial", () =>
        Results.Json(this.wands.SerialInfo()));

      app.MapPost("/api/maintenance/wands/calibrate", () => {
        this.wands.CalibrateAll();
        return Results.Ok();
      });

      // ---- Human tap tempo. The client times its own taps and posts the
      // computed BPM (momentary, like the native Tap button — no advisory
      // lease). Also flips the source to Human, matching the native button. ----
      app.MapPost("/api/maintenance/tempo/tap", async (TempoBody body) => {
        if (body == null || double.IsNaN(body.bpm) ||
            double.IsInfinity(body.bpm) || body.bpm <= 0.0) {
          return Results.BadRequest(
            new { error = "body must be {\"bpm\": <positive number>}" });
        }
        await this.tempo.SetManualBPMAsync(body.bpm);
        return Results.Ok();
      });

      // ---- Change feed (Server-Sent Events) ----
      // Clients open one of these after their initial GET and re-render on each
      // pushed {key, value}. This is what makes last-write-wins coherent across
      // phones — everyone sees the winning value.
      app.MapGet("/api/events", (HttpContext ctx) =>
        this.StreamEvents(ctx, ControlRole.User));

      app.MapGet("/api/maintenance/events", (HttpContext ctx) =>
        this.StreamEvents(ctx, ControlRole.Maintenance));
    }

    // Runs a calibration action for a caller holding the domeCalibration lease
    // (a modal flow can't be shared) and returns its result as JSON. Returns 423
    // with the current holder if the caller doesn't hold the lease, or 400 if
    // the action rejects its argument.
    private async Task<IResult> RunCalibration(
      string token,
      Func<DomeCalibrationController, Task<DomeCalibrationController.CalibrationState>> action
    ) {
      if (!this.locks.HoldsLock(LockPolicy.DomeCalibration, token)) {
        return this.CalibrationLocked();
      }
      try {
        return Results.Json(await action(this.calibration));
      } catch (ArgumentException e) {
        // Return the unchanged state alongside the error so the client re-renders
        // the flow (diagram, picks) instead of collapsing to the start screen.
        return Results.BadRequest(new { error = e.Message, state = this.calibration.State() });
      }
    }

    private IResult CalibrationLocked() => Results.Json(
      new { error = "hold the domeCalibration lock first",
        holder = this.locks.Get(LockPolicy.DomeCalibration) },
      statusCode: StatusCodes.Status423Locked);

    private async Task StreamEvents(HttpContext ctx, ControlRole role) {
      ctx.Response.Headers["Content-Type"] = "text/event-stream";
      ctx.Response.Headers["Cache-Control"] = "no-cache";
      ctx.Response.Headers["X-Accel-Buffering"] = "no";

      ConfigEventStream.Subscriber sub = this.events.Subscribe(role, out Guid id);
      try {
        // Nudge the stream open so EventSource fires onopen promptly, then seed
        // the current telemetry values (parameters get theirs from the initial
        // REST GET, but telemetry has no such GET).
        await ctx.Response.WriteAsync(": connected\n\n");
        foreach (string frame in this.events.InitialStateFrames()) {
          await ctx.Response.WriteAsync($"data: {frame}\n\n", ctx.RequestAborted);
        }
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

        await foreach (string msg in
            sub.Reader.ReadAllAsync(ctx.RequestAborted)) {
          await ctx.Response.WriteAsync($"data: {msg}\n\n", ctx.RequestAborted);
          await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        }
      } catch (OperationCanceledException) {
        // Client disconnected; fall through to cleanup.
      } finally {
        this.events.Unsubscribe(id);
      }
    }

    private async Task<IResult> HandleWrite(string key, HttpContext ctx, ControlRole role) {
      WriteBody body;
      try {
        body = await ctx.Request.ReadFromJsonAsync<WriteBody>();
      } catch (JsonException) {
        return Results.BadRequest(new { error = "malformed JSON body" });
      }
      if (body == null || body.value.ValueKind == JsonValueKind.Undefined) {
        return Results.BadRequest(new { error = "body must be {\"value\": ...}" });
      }

      object raw;
      try {
        raw = Unwrap(body.value);
      } catch (ArgumentException e) {
        return Results.BadRequest(new { error = e.Message });
      }

      // Advisory-lock gate: if this parameter is part of a modal resource that
      // another user currently holds, refuse the write.
      string resource = LockPolicy.ResourceForKey(key);
      if (resource != null) {
        string token = ctx.Request.Headers[LockTokenHeader];
        if (!this.locks.CanWrite(resource, token)) {
          return Results.Json(
            new { error = "locked", holder = this.locks.Get(resource) },
            statusCode: StatusCodes.Status423Locked);
        }
      }

      WriteResult result = await this.controls.WriteAsync(key, raw, role);
      switch (result.Status) {
        case WriteStatus.Ok:
          return Results.Json(new { key, value = result.Value });
        case WriteStatus.NotFound:
          return Results.NotFound(new { error = result.Message });
        case WriteStatus.Forbidden:
          return Results.Json(new { error = result.Message }, statusCode: StatusCodes.Status403Forbidden);
        case WriteStatus.Invalid:
        default:
          return Results.BadRequest(new { error = result.Message });
      }
    }

    // Converts a JSON value into the boxed scalar the descriptors expect.
    private static object Unwrap(JsonElement value) {
      switch (value.ValueKind) {
        case JsonValueKind.True:
          return true;
        case JsonValueKind.False:
          return false;
        case JsonValueKind.String:
          return value.GetString();
        case JsonValueKind.Number:
          return value.GetDouble();
        case JsonValueKind.Null:
          throw new ArgumentException("value must not be null");
        default:
          throw new ArgumentException("unsupported value kind: " + value.ValueKind);
      }
    }

    private sealed class WriteBody {
      public JsonElement value { get; set; }
    }

    private sealed class OperatorBody {
      public bool enabled { get; set; }
    }

    private sealed class TempoBody {
      public double bpm { get; set; }
    }

    private sealed class SpotlightBody {
      public int deviceId { get; set; }
    }

    private sealed class AcquireBody {
      public string holderName { get; set; }
    }

    private sealed class PickBody {
      public int endpoint { get; set; }
    }

    private sealed class SwapBody {
      public int a { get; set; }
      public int b { get; set; }
    }

    private sealed class LayersBody {
      public System.Collections.Generic.List<LayersController.LayerDto> layers {
        get; set;
      }
    }
  }
}
