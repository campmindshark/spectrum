using System.Threading.Tasks;
using Spectrum.Base;

namespace Spectrum.Web {

  /**
   * The web control behind the global Start/Stop button. Reads and toggles the
   * Operator's runtime Enabled flag — the same engine on/off switch the native
   * power button drives. This is deliberately NOT a ParameterRegistry entry:
   * Enabled is live Operator state, not a persisted Configuration property, so
   * it rides its own tiny endpoint rather than the field-level LWW path.
   *
   * The write is marshaled through the application-state dispatcher so it lands on the same
   * thread a native power-button click would (the Enabled setter spawns/joins
   * the OperatorThread and touches WPF-affine state). ConfigEventStream watches
   * Operator.EnabledChanged and broadcasts every flip, so a client's button
   * stays coherent with the native GUI and with other clients.
   */
  public sealed class OperatorController {

    private readonly Operator op;
    private readonly ApplicationStateDispatcher gateway;

    public OperatorController(
      Operator op, ApplicationStateDispatcher gateway
    ) {
      this.op = op;
      this.gateway = gateway;
    }

    public object State() => new { enabled = this.op.Enabled };

    // Read-only maintenance snapshot for service monitoring and Linux soak
    // qualification. Keep live counters out of Configuration: they are
    // transient process health, never persisted operator settings.
    public object RuntimeState() => new {
      enabled = this.op.Enabled,
      operatorFps = this.op.Telemetry.OperatorFPS,
      domeOpcFps = this.op.Telemetry.DomeBeagleboneOPCFPS,
      layerPlanError = this.op.Telemetry.LayerPlanError,
    };

    // Sets the engine on/off through the gateway and reports the resulting
    // state. Idempotent — the setter no-ops (and fires no event) if already in
    // the requested state, so a redundant press from a stale client is harmless.
    public async Task<object> SetEnabledAsync(bool enabled) {
      await this.gateway.InvokeAsync(() => this.op.Enabled = enabled);
      return this.State();
    }
  }
}
