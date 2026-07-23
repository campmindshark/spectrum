using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Spectrum.Base;

namespace Spectrum.Web {

  public enum WriteStatus {
    Ok,
    NotFound,
    Forbidden,
    Invalid,
  }

  public readonly struct WriteResult {
    public WriteStatus Status { get; }
    public string? Message { get; }
    // The coerced value that was actually applied (null unless Status == Ok).
    public object? Value { get; }

    public WriteResult(
      WriteStatus status, string? message, object? value
    ) {
      this.Status = status;
      this.Message = message;
      this.Value = value;
    }

    public static WriteResult Ok(object value) =>
      new WriteResult(WriteStatus.Ok, null, value);
    public static WriteResult NotFound(string key) =>
      new WriteResult(WriteStatus.NotFound, "unknown parameter: " + key, null);
    public static WriteResult Forbidden(string key) =>
      new WriteResult(WriteStatus.Forbidden, "not permitted for this role: " + key, null);
    public static WriteResult Invalid(string message) =>
      new WriteResult(WriteStatus.Invalid, message, null);
  }

  // A JSON-friendly projection of a descriptor + its current value, used to
  // render both the parameter list and single-parameter reads.
  public sealed class ParameterView {
    public string key { get; set; } = "";
    public string type { get; set; } = "";
    public string role { get; set; } = "";
    public string label { get; set; } = "";
    public string? description { get; set; }
    public string? unit { get; set; }
    public object? value { get; set; }
    public object? min { get; set; }
    public object? max { get; set; }
    public IReadOnlyList<string>? options { get; set; }
    // The advisory-lock resource this parameter participates in (a modal op such
    // as a test pattern), or null if it is a free last-write-wins knob. The
    // maintenance UI acquires this lease before writing such a parameter.
    public string? @lock { get; set; }
  }

  /**
   * The bridge the REST/SignalR layer uses. Reads and writes are serialized
   * through the ApplicationStateDispatcher so Kestrel never touches mutable
   * Configuration or serializer DTO collections directly.
   *
   * This is the only web-side object that holds the Configuration reference,
   * the registry, and the gateway together.
   */
  public sealed class ControlService {

    private readonly ParameterRegistry registry;
    private readonly ApplicationStateDispatcher gateway;
    private readonly Configuration config;

    public ControlService(
      ParameterRegistry registry,
      ApplicationStateDispatcher gateway,
      Configuration config
    ) {
      this.registry = registry;
      this.gateway = gateway;
      this.config = config;
    }

    public ParameterRegistry Registry => this.registry;

    internal Task<T> CaptureAsync<T>(Func<T> read) =>
      this.gateway.InvokeAsync(read);

    public Task<List<ParameterView>> DescribeAsync(ControlRole role) =>
      this.gateway.InvokeAsync(() => this.Describe(role));

    public Task<ParameterView?> ReadAsync(string key, ControlRole role) =>
      this.gateway.InvokeAsync(() => this.Read(key, role));

    // All parameters visible to a caller with the given role, with values.
    private List<ParameterView> Describe(ControlRole role) {
      var views = new List<ParameterView>();
      foreach (ParameterDescriptor d in this.registry.ForRole(role)) {
        views.Add(this.ToView(d));
      }
      return views;
    }

    // Read one parameter. Returns null if unknown or not visible to the role
    // (callers treat both as 404 to avoid leaking the maintenance key set).
    private ParameterView? Read(string key, ControlRole role) {
      if (!this.registry.TryGet(key, out ParameterDescriptor? d) || d == null) {
        return null;
      }
      if (!ParameterRegistry.RoleCanAccess(role, d)) {
        return null;
      }
      return this.ToView(d);
    }

    public async Task<WriteResult> WriteAsync(
      string key, object? raw, ControlRole role
    ) {
      if (!this.registry.TryGet(key, out ParameterDescriptor? d) || d == null) {
        return WriteResult.NotFound(key);
      }
      if (!ParameterRegistry.RoleCanAccess(role, d)) {
        return WriteResult.Forbidden(key);
      }
      object coerced;
      try {
        coerced = d.Coerce(raw);
      } catch (ArgumentException e) {
        return WriteResult.Invalid(e.Message);
      }
      // Serialize the actual mutation onto the gateway thread. The descriptor's
      // Set runs there, so PropertyChanged fires on the UI thread exactly as a
      // native GUI write would.
      await this.gateway.InvokeAsync(() => d.Set(this.config, coerced));
      return WriteResult.Ok(coerced);
    }

    private ParameterView ToView(ParameterDescriptor d) => new ParameterView {
      key = d.Key,
      type = d.Type,
      role = d.Role == ControlRole.User ? "user" : "maintenance",
      label = d.Label,
      description = d.Description,
      unit = d.Unit,
      value = d.Get(this.config),
      min = d.Min,
      max = d.Max,
      options = d.Options,
      @lock = LockPolicy.ResourceForKey(d.Key),
    };
  }
}
