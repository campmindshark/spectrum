using System;
using System.Collections.Generic;
using Spectrum.Base;

namespace Spectrum.Web {

  /**
   * Read-only telemetry the web clients display but never write: engine FPS, the
   * three OPC output rates, and the current BPM (docs/web_architecture.md — the
   * "richer read-only telemetry push" the users want alongside the controls).
   *
   * Telemetry is deliberately NOT part of the ParameterRegistry: it is never
   * writable, never role-gated (a readout leaks nothing a control would), and
   * must not appear in the REST parameter list. ConfigEventStream pushes these
   * over the same SSE feed as parameter changes, tagged with kind "telemetry" so
   * the client renders them as static readouts rather than controls.
   *
   * Each item names the PropertyChanged event that signals it changed. Most ride
   * Configuration.PropertyChanged (the FPS counters are XmlIgnore config
   * properties updated by the Operator/OPC threads); BPM rides
   * BeatBroadcaster.PropertyChanged ("BPMString").
   */
  public enum TelemetrySource { Config, Beat }

  public sealed class TelemetryItem {
    public string Key { get; }
    public TelemetrySource Source { get; }
    // The PropertyChanged property name on the source object that fires when
    // this telemetry value changes.
    public string SourceProperty { get; }
    private readonly Func<Configuration, object> getter;

    public TelemetryItem(
      string key,
      TelemetrySource source,
      string sourceProperty,
      Func<Configuration, object> getter
    ) {
      this.Key = key;
      this.Source = source;
      this.SourceProperty = sourceProperty;
      this.getter = getter;
    }

    public object Read(Configuration config) => this.getter(config);
  }

  public static class TelemetryCatalog {

    public static IReadOnlyList<TelemetryItem> Items { get; } =
      new List<TelemetryItem> {
        new TelemetryItem("operatorFPS", TelemetrySource.Config,
          "operatorFPS", c => c.operatorFPS),
        new TelemetryItem("domeOPCFPS", TelemetrySource.Config,
          "domeBeagleboneOPCFPS", c => c.domeBeagleboneOPCFPS),
        new TelemetryItem("barOPCFPS", TelemetrySource.Config,
          "barBeagleboneOPCFPS", c => c.barBeagleboneOPCFPS),
        new TelemetryItem("stageOPCFPS", TelemetrySource.Config,
          "stageBeagleboneOPCFPS", c => c.stageBeagleboneOPCFPS),
        new TelemetryItem("bpm", TelemetrySource.Beat,
          "BPMString", c => c.beatBroadcaster.BPMString),
      };
  }
}
