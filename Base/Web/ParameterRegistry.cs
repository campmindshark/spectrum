using System;
using System.Collections.Generic;

namespace Spectrum.Base {

  /**
   * The catalog of web-controllable parameters, keyed by their wire key. The
   * REST/SignalR layer resolves every request through this registry, which is
   * what keeps Configuration from being exposed 1:1 over HTTP.
   *
   * Role gating rule: a User caller may only see/touch User parameters; a
   * Maintenance caller may see/touch everything.
   */
  public sealed class ParameterRegistry {

    private readonly Dictionary<string, ParameterDescriptor> byKey;

    public ParameterRegistry(IEnumerable<ParameterDescriptor> descriptors) {
      this.byKey = new Dictionary<string, ParameterDescriptor>();
      foreach (ParameterDescriptor d in descriptors) {
        if (this.byKey.ContainsKey(d.Key)) {
          throw new ArgumentException("duplicate parameter key: " + d.Key);
        }
        this.byKey[d.Key] = d;
      }
    }

    public IReadOnlyCollection<ParameterDescriptor> All => this.byKey.Values;

    public bool TryGet(string key, out ParameterDescriptor descriptor) =>
      this.byKey.TryGetValue(key, out descriptor);

    // Whether a caller with the given role may see/touch this parameter.
    public static bool RoleCanAccess(ControlRole callerRole, ParameterDescriptor d) =>
      callerRole == ControlRole.Maintenance || d.Role == ControlRole.User;

    // The descriptors visible to a caller with the given role.
    public IEnumerable<ParameterDescriptor> ForRole(ControlRole callerRole) {
      foreach (ParameterDescriptor d in this.byKey.Values) {
        if (RoleCanAccess(callerRole, d)) {
          yield return d;
        }
      }
    }
  }
}
