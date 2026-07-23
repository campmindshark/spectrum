using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Spectrum.Base;

namespace Spectrum.Web {

  /**
   * Shared parameter-write protocol for user and maintenance routes. This owns
   * JSON scalar unwrapping, advisory-lock enforcement, and the HTTP projection
   * of ControlService write results.
   */
  internal sealed class ParameterWriteHandler {

    internal const string LockTokenHeader = "X-Spectrum-Lock-Token";

    private readonly ControlService controls;
    private readonly AdvisoryLockManager locks;

    internal ParameterWriteHandler(
      ControlService controls, AdvisoryLockManager locks
    ) {
      this.controls = controls;
      this.locks = locks;
    }

    internal async Task<IResult> HandleAsync(
      string key, HttpContext context, ControlRole role
    ) {
      WriteBody? body;
      try {
        body = await context.Request.ReadFromJsonAsync<WriteBody>();
      } catch (JsonException) {
        return Results.BadRequest(new { error = "malformed JSON body" });
      }
      if (body == null || body.value.ValueKind == JsonValueKind.Undefined) {
        return Results.BadRequest(
          new { error = "body must be {\"value\": ...}" });
      }

      object raw;
      try {
        raw = Unwrap(body.value);
      } catch (ArgumentException error) {
        return Results.BadRequest(new { error = error.Message });
      }

      string? resource = LockPolicy.ResourceForKey(key);
      if (resource != null) {
        string token =
          context.Request.Headers[LockTokenHeader].ToString();
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
          return Results.Json(
            new { error = result.Message },
            statusCode: StatusCodes.Status403Forbidden);
        case WriteStatus.Invalid:
        default:
          return Results.BadRequest(new { error = result.Message });
      }
    }

    private static object Unwrap(JsonElement value) {
      switch (value.ValueKind) {
        case JsonValueKind.True:
          return true;
        case JsonValueKind.False:
          return false;
        case JsonValueKind.String:
          return value.GetString() ?? throw new ArgumentException(
            "string value is unavailable");
        case JsonValueKind.Number:
          return value.GetDouble();
        case JsonValueKind.Null:
          throw new ArgumentException("value must not be null");
        default:
          throw new ArgumentException(
            "unsupported value kind: " + value.ValueKind);
      }
    }

    private sealed class WriteBody {
      public JsonElement value { get; set; }
    }
  }
}
