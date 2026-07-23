using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Spectrum.Base;

namespace Spectrum.Web {

  /**
   * User-facing and globally shared HTTP routes. Compound show-state writes are
   * delegated to their controllers; scalar writes share ParameterWriteHandler.
   */
  internal sealed class UserApiRoutes {

    private readonly ControlService controls;
    private readonly OperatorController operatorControl;
    private readonly LayersController layers;
    private readonly SceneController scenes;
    private readonly PaletteController palettes;
    private readonly WandStatusController wands;
    private readonly WebDomeSimulator? domeSimulator;
    private readonly ParameterWriteHandler parameterWrites;

    internal UserApiRoutes(
      ControlService controls,
      OperatorController operatorControl,
      LayersController layers,
      SceneController scenes,
      PaletteController palettes,
      WandStatusController wands,
      WebDomeSimulator? domeSimulator,
      ParameterWriteHandler parameterWrites
    ) {
      this.controls = controls;
      this.operatorControl = operatorControl;
      this.layers = layers;
      this.scenes = scenes;
      this.palettes = palettes;
      this.wands = wands;
      this.domeSimulator = domeSimulator;
      this.parameterWrites = parameterWrites;
    }

    internal void Map(WebApplication app) {
      // Keep the high-rate preview out of the main control document entirely.
      app.MapGet("/simulator", () => Results.Redirect("/simulator.html"));
      if (this.domeSimulator is WebDomeSimulator domeSimulator) {
        app.MapGet("/api/dome-simulator/geometry", () =>
          Results.Json(domeSimulator.Geometry()));
        app.Map("/api/dome-simulator/frames", domeSimulator.StreamAsync);
      }

      // Global engine state is shared by both surfaces.
      app.MapGet("/api/operator", () =>
        Results.Json(this.operatorControl.State()));
      app.MapPut("/api/operator", async (OperatorBody? body) => {
        if (body == null) {
          return Results.BadRequest(
            new { error = "body must be {\"enabled\": true|false}" });
        }
        return Results.Json(
          await this.operatorControl.SetEnabledAsync(body.enabled));
      });

      app.MapGet("/api/parameters", async () =>
        Results.Json(await this.controls.DescribeAsync(ControlRole.User)));
      app.MapGet("/api/parameters/{key}", async (string key) => {
        ParameterView? view = await this.controls.ReadAsync(
          key, ControlRole.User);
        return view == null ? Results.NotFound() : Results.Json(view);
      });
      app.MapPut("/api/parameters/{key}", (
        string key, HttpContext context
      ) => this.parameterWrites.HandleAsync(
        key, context, ControlRole.User));

      app.MapGet("/api/layers", async () =>
        Results.Json(await this.layers.StateAsync()));
      app.MapPut("/api/layers", async (LayersBody? body) => {
        (bool ok, string? error) =
          await this.layers.ReplaceAsync(body?.layers);
        return ok
          ? Results.Json(await this.layers.StateAsync())
          : Results.BadRequest(new { error });
      });
      app.MapPost("/api/layers/{instanceId}/fire", async (
        string instanceId
      ) => {
        (bool ok, string? error) =
          await this.layers.FireAsync(instanceId);
        return ok ? Results.Ok() : Results.BadRequest(new { error });
      });
      app.MapPost("/api/layers/{instanceId}/clear", async (
        string instanceId
      ) => {
        (bool ok, string? error) =
          await this.layers.ClearAsync(instanceId);
        return ok ? Results.Ok() : Results.BadRequest(new { error });
      });

      app.MapGet("/api/scenes", async () =>
        Results.Json(await this.scenes.StateAsync()));
      app.MapPost("/api/scenes", async (SceneBody? body) => {
        (bool ok, string? error) =
          await this.scenes.SaveAsync(body?.name);
        return ok
          ? Results.Json(await this.scenes.StateAsync())
          : Results.BadRequest(new { error });
      });
      app.MapPost("/api/scenes/{name}/apply", async (string name) => {
        (bool ok, string? error) = await this.scenes.ApplyAsync(name);
        return ok
          ? Results.Json(await this.scenes.StateAsync())
          : Results.BadRequest(new { error });
      });
      app.MapDelete("/api/scenes/{name}", async (string name) => {
        (bool ok, string? error) = await this.scenes.DeleteAsync(name);
        return ok
          ? Results.Json(await this.scenes.StateAsync())
          : Results.BadRequest(new { error });
      });

      app.MapGet("/api/palettes", async () =>
        Results.Json(await this.palettes.StateAsync()));
      app.MapPost("/api/palettes", async (PaletteBody? body) => {
        (bool ok, string? error) = await this.palettes.AddAsync(
          body?.name, body?.sourceName);
        return ok
          ? Results.Json(await this.palettes.StateAsync())
          : Results.BadRequest(new { error });
      });
      app.MapPut("/api/palettes/{name}", async (
        string name, PaletteLiveBody? body
      ) => {
        (bool ok, string? error) = await this.palettes.SetColorsAsync(
          name, body?.colors);
        return ok
          ? Results.Json(await this.palettes.StateAsync())
          : Results.BadRequest(new { error });
      });
      app.MapPost("/api/palettes/{name}/rename", async (
        string name, PaletteRenameBody? body
      ) => {
        (bool ok, string? error) = await this.palettes.RenameAsync(
          name, body?.newName);
        return ok
          ? Results.Json(await this.palettes.StateAsync())
          : Results.BadRequest(new { error });
      });
      app.MapDelete("/api/palettes/{name}", async (string name) => {
        (bool ok, string? error) = await this.palettes.DeleteAsync(name);
        return ok
          ? Results.Json(await this.palettes.StateAsync())
          : Results.BadRequest(new { error });
      });

      app.MapGet("/api/wands", () =>
        Results.Json(new {
          spotlight = this.wands.CurrentSpotlight(),
          rows = this.wands.Snapshot(),
        }));
      app.MapPost("/api/wands/spotlight", (SpotlightBody? body) => {
        if (body == null) {
          return Results.BadRequest(
            new { error = "body must be {\"deviceId\": <int>}" });
        }
        this.wands.SetSpotlight(body.deviceId);
        return Results.Ok();
      });
    }

    private sealed class OperatorBody {
      public bool enabled { get; set; }
    }

    private sealed class SpotlightBody {
      public int deviceId { get; set; }
    }

    private sealed class LayersBody {
      public System.Collections.Generic.List<LayersController.LayerDto?>?
        layers { get; set; }
    }

    private sealed class SceneBody {
      public string? name { get; set; }
    }

    private sealed class PaletteBody {
      public string? name { get; set; }
      public string? sourceName { get; set; }
    }

    private sealed class PaletteLiveBody {
      public System.Collections.Generic.List<PaletteController.SlotDto>? colors {
        get; set;
      }
    }

    private sealed class PaletteRenameBody {
      public string? newName { get; set; }
    }
  }
}
