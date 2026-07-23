using System;
using System.Collections.Generic;

namespace Spectrum {

  public sealed record WandSerialPortOption(string Display, string Value) {
    public override string ToString() => this.Display;
  }

  public enum WandSerialPresentationKind {
    Inactive,
    Ready,
    Warning,
    Error,
  }

  public sealed record WandSerialPresentation(
    string Text,
    WandSerialPresentationKind Kind
  );

  /**
   * UI-neutral presentation rules for the receiver port selector and liveness
   * indicator. Native controls consume these values without owning missing-port
   * retention or receiver-state classification.
   */
  public static class WandSerialPresentationModel {
    public static IReadOnlyList<WandSerialPortOption> BuildPortOptions(
      string? configuredPort,
      IEnumerable<string> availablePorts
    ) {
      string configured = configuredPort ?? "";
      var options = new List<WandSerialPortOption> {
        new WandSerialPortOption("(none)", ""),
      };
      bool configuredPresent = false;
      foreach (string port in availablePorts) {
        if (string.IsNullOrEmpty(port)) {
          continue;
        }
        options.Add(new WandSerialPortOption(port, port));
        if (port == configured) {
          configuredPresent = true;
        }
      }
      if (!string.IsNullOrEmpty(configured) && !configuredPresent) {
        options.Add(new WandSerialPortOption(
          configured + " (missing)", configured));
      }
      return options;
    }

    public static WandSerialPresentation EvaluateStatus(
      string? configuredPort,
      WandSerialStatus status
    ) {
      if (string.IsNullOrEmpty(configuredPort)) {
        return new WandSerialPresentation(
          "No port selected", WandSerialPresentationKind.Inactive);
      }
      if (status.LastError != null) {
        return new WandSerialPresentation(
          "Error: " + status.LastError,
          WandSerialPresentationKind.Error);
      }
      if (!status.PortOpen) {
        return new WandSerialPresentation(
          "Opening…", WandSerialPresentationKind.Inactive);
      }
      double since = Math.Min(
        status.MillisSinceLastHeartbeat,
        status.MillisSinceLastFrame);
      if (since < WandSerialReceiver.RECEIVER_ALIVE_MS) {
        return new WandSerialPresentation(
          "Receiver connected (" +
            (since / 1000.0).ToString("0.0") + " s ago)",
          WandSerialPresentationKind.Ready);
      }
      return new WandSerialPresentation(
        "Port open — no data", WandSerialPresentationKind.Warning);
    }
  }
}
