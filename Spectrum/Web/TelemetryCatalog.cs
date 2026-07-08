using System;
using System.Collections.Generic;
using Spectrum.Base;

namespace Spectrum.Web {

  /**
   * Read-only telemetry the web clients display but never write: engine FPS, the
   * OPC output rate, and the current BPM (docs/web_architecture.md — the
   * "richer read-only telemetry push" the users want alongside the controls).
   *
   * Telemetry is deliberately NOT part of the ParameterRegistry: it is never
   * writable, never role-gated (a readout leaks nothing a control would), and
   * must not appear in the REST parameter list. ConfigEventStream pushes these
   * over the same SSE feed as parameter changes, tagged with kind "telemetry" so
   * the client renders them as static readouts rather than controls.
   *
   * Each item names the PropertyChanged event that signals it changed. The FPS
   * counters ride RuntimeTelemetry.PropertyChanged and BPM rides
   * BeatBroadcaster.PropertyChanged ("BPMString") — both live services owned
   * by the Operator, not Configuration.
   */
  public enum TelemetrySource { Runtime, Beat }

  public sealed class TelemetryItem {
    public string Key { get; }
    public TelemetrySource Source { get; }
    // The PropertyChanged property name on the source object that fires when
    // this telemetry value changes.
    public string SourceProperty { get; }
    private readonly Func<RuntimeTelemetry, BeatBroadcaster, object> getter;

    public TelemetryItem(
      string key,
      TelemetrySource source,
      string sourceProperty,
      Func<RuntimeTelemetry, BeatBroadcaster, object> getter
    ) {
      this.Key = key;
      this.Source = source;
      this.SourceProperty = sourceProperty;
      this.getter = getter;
    }

    public object Read(RuntimeTelemetry telemetry, BeatBroadcaster beat) =>
      this.getter(telemetry, beat);
  }

  public static class TelemetryCatalog {

    public static IReadOnlyList<TelemetryItem> Items { get; } =
      new List<TelemetryItem> {
        new TelemetryItem("operatorFPS", TelemetrySource.Runtime,
          nameof(RuntimeTelemetry.OperatorFPS), (t, b) => t.OperatorFPS),
        new TelemetryItem("domeOPCFPS", TelemetrySource.Runtime,
          nameof(RuntimeTelemetry.DomeBeagleboneOPCFPS),
          (t, b) => t.DomeBeagleboneOPCFPS),
        new TelemetryItem("bpm", TelemetrySource.Beat,
          "BPMString", (t, b) => b.BPMString),
      };
  }
}
