using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Spectrum.Base;

namespace Spectrum.Web {

  /**
   * Maintenance-only diagnostics and modal-operation routes, including
   * advisory-lock and dome-calibration protocol ownership.
   */
  internal sealed class MaintenanceApiRoutes {

    private readonly ControlService controls;
    private readonly AudioDeviceController audio;
    private readonly OperatorController operatorControl;
    private readonly AdvisoryLockManager locks;
    private readonly DomeCalibrationController calibration;
    private readonly WandStatusController wands;
    private readonly TempoController tempo;
    private readonly ParameterWriteHandler parameterWrites;

    internal MaintenanceApiRoutes(
      ControlService controls,
      AudioDeviceController audio,
      OperatorController operatorControl,
      AdvisoryLockManager locks,
      DomeCalibrationController calibration,
      WandStatusController wands,
      TempoController tempo,
      ParameterWriteHandler parameterWrites
    ) {
      this.controls = controls;
      this.audio = audio;
      this.operatorControl = operatorControl;
      this.locks = locks;
      this.calibration = calibration;
      this.wands = wands;
      this.tempo = tempo;
      this.parameterWrites = parameterWrites;
    }

    internal void Map(WebApplication app) {
      app.MapGet("/api/maintenance/parameters", async () =>
        Results.Json(await this.controls.DescribeAsync(
          ControlRole.Maintenance)));
      app.MapGet("/api/maintenance/parameters/{key}", async (string key) => {
        ParameterView? view = await this.controls.ReadAsync(
          key, ControlRole.Maintenance);
        return view == null ? Results.NotFound() : Results.Json(view);
      });
      app.MapPut("/api/maintenance/parameters/{key}", (
        string key, HttpContext context
      ) => this.parameterWrites.HandleAsync(
        key, context, ControlRole.Maintenance));

      app.MapGet("/api/maintenance/audio", () =>
        Results.Json(this.audio.State()));
      app.MapGet("/api/maintenance/runtime", () =>
        Results.Json(this.operatorControl.RuntimeState()));

      app.MapGet("/api/maintenance/locks", () =>
        Results.Json(this.locks.ActiveLocks()));
      app.MapPost("/api/maintenance/locks/{resource}", async (
        string resource, HttpContext context
      ) => {
        string holderName = "operator";
        try {
          AcquireBody? body =
            await context.Request.ReadFromJsonAsync<AcquireBody>();
          if (!string.IsNullOrWhiteSpace(body?.holderName)) {
            holderName = body.holderName;
          }
        } catch (JsonException) {
          // An empty body uses the default operator name.
        }

        string? token = this.locks.TryAcquire(
          resource,
          holderName,
          out AdvisoryLockManager.LockInfo current);
        if (token == null) {
          return Results.Json(
            new { error = "locked", holder = current },
            statusCode: StatusCodes.Status423Locked);
        }
        return Results.Json(new { token, holder = current });
      });
      app.MapPost(
        "/api/maintenance/locks/{resource}/heartbeat",
        (string resource, HttpContext context) => {
          string token = context.Request.Headers[
            ParameterWriteHandler.LockTokenHeader].ToString();
          return this.locks.TryRenew(resource, token)
            ? Results.Ok()
            : Results.Json(
              new { error = "not holder" },
              statusCode: StatusCodes.Status409Conflict);
        });
      app.MapDelete("/api/maintenance/locks/{resource}", (
        string resource, HttpContext context
      ) => {
        string token = context.Request.Headers[
          ParameterWriteHandler.LockTokenHeader].ToString();
        this.locks.TryRelease(resource, token);
        return Results.Ok();
      });

      app.MapGet("/api/maintenance/calibration", async () =>
        Results.Json(await this.calibration.StateAsync()));
      app.MapPost(
        "/api/maintenance/calibration/start",
        ([FromHeader(Name = ParameterWriteHandler.LockTokenHeader)]
          string? token) =>
          this.RunCalibration(token, controller => controller.StartAsync()));
      app.MapPost(
        "/api/maintenance/calibration/navigate",
        ([FromHeader(Name = ParameterWriteHandler.LockTokenHeader)]
          string? token,
         DirectionBody? body) =>
          this.RunCalibration(
            token,
            controller => controller.NavigateAsync(body?.direction ?? 0)));
      app.MapPost(
        "/api/maintenance/calibration/confirm",
        ([FromHeader(Name = ParameterWriteHandler.LockTokenHeader)]
          string? token) =>
          this.RunCalibration(
            token, controller => controller.ConfirmAsync()));
      app.MapPost(
        "/api/maintenance/calibration/back",
        ([FromHeader(Name = ParameterWriteHandler.LockTokenHeader)]
          string? token) =>
          this.RunCalibration(token, controller => controller.BackAsync()));
      app.MapPost(
        "/api/maintenance/calibration/select-box",
        ([FromHeader(Name = ParameterWriteHandler.LockTokenHeader)]
          string? token,
         BoxBody? body) =>
          this.RunCalibration(
            token,
            controller => controller.SelectBoxAsync(body?.box ?? -1)));
      app.MapPost(
        "/api/maintenance/calibration/apply-box-one",
        ([FromHeader(Name = ParameterWriteHandler.LockTokenHeader)]
          string? token) =>
          this.RunCalibration(
            token, controller => controller.ApplyBoxOneAsync()));
      app.MapPost(
        "/api/maintenance/calibration/recalibrate-box",
        ([FromHeader(Name = ParameterWriteHandler.LockTokenHeader)]
          string? token,
         BoxBody? body) =>
          this.RunCalibration(
            token,
            controller => controller.RecalibrateBoxAsync(body?.box ?? -1)));
      app.MapPost(
        "/api/maintenance/calibration/cancel",
        ([FromHeader(Name = ParameterWriteHandler.LockTokenHeader)]
          string? token) =>
          this.RunCalibration(
            token, controller => controller.CancelAsync()));
      app.MapPost(
        "/api/maintenance/calibration/save",
        async ([FromHeader(
          Name = ParameterWriteHandler.LockTokenHeader)] string? token) => {
          if (!this.locks.HoldsLock(
              LockPolicy.DomeCalibration, token)) {
            return this.CalibrationLocked();
          }
          (bool ok, string? error, var state) =
            await this.calibration.SaveAsync();
          return ok
            ? Results.Json(state)
            : Results.BadRequest(new { error, state });
        });

      app.MapGet("/api/maintenance/wands", () =>
        Results.Json(this.wands.Snapshot()));
      app.MapGet("/api/maintenance/wands/serial", () =>
        Results.Json(this.wands.SerialInfo()));
      app.MapPost("/api/maintenance/wands/calibrate", () => {
        this.wands.CalibrateAll();
        return Results.Ok();
      });

      app.MapPost("/api/maintenance/tempo/tap", async (
        TempoBody? body
      ) => {
        if (body == null ||
            double.IsNaN(body.bpm) ||
            double.IsInfinity(body.bpm) ||
            body.bpm <= 0.0) {
          return Results.BadRequest(
            new { error = "body must be {\"bpm\": <positive number>}" });
        }
        await this.tempo.SetManualBPMAsync(body.bpm);
        return Results.Ok();
      });
    }

    private async Task<IResult> RunCalibration(
      string? token,
      Func<
        DomeCalibrationController,
        Task<DomeCalibrationController.CalibrationState>> action
    ) {
      if (!this.locks.HoldsLock(LockPolicy.DomeCalibration, token)) {
        return this.CalibrationLocked();
      }
      try {
        return Results.Json(await action(this.calibration));
      } catch (ArgumentException error) {
        return Results.BadRequest(new {
          error = error.Message,
          state = await this.calibration.StateAsync(),
        });
      }
    }

    private IResult CalibrationLocked() => Results.Json(
      new {
        error = "hold the domeCalibration lock first",
        holder = this.locks.Get(LockPolicy.DomeCalibration),
      },
      statusCode: StatusCodes.Status423Locked);

    private sealed class AcquireBody {
      public string? holderName { get; set; }
    }

    private sealed class DirectionBody {
      public int direction { get; set; }
    }

    private sealed class BoxBody {
      public int box { get; set; }
    }

    private sealed class TempoBody {
      public double bpm { get; set; }
    }
  }
}
