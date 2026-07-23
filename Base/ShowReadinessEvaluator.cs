using System;

namespace Spectrum.Base {

  public enum ReadinessLevel {
    Disabled,
    Warning,
    Error,
    Ready,
  }

  public sealed record ReadinessStatus(
    ReadinessLevel Level,
    string Badge,
    string Detail
  );

  public sealed record ShowReadinessInput(
    bool Running,
    string? SelectedAudioName,
    float AudioSignal,
    bool DomeEnabled,
    bool DomeAddressValid,
    string? DomeAddressError,
    int DomeFramesPerSecond,
    int ConnectedWandCount,
    string? WandSerialPort,
    string? WandReceiverError,
    string? WebServerError,
    int WebServerPort
  );

  public sealed record ShowReadinessSnapshot(
    string PowerButtonText,
    ReadinessStatus Engine,
    ReadinessStatus Audio,
    string AudioSignalText,
    ReadinessStatus Dome,
    ReadinessStatus Wand,
    string ConnectedWandCountText,
    string WebStatus,
    ReadinessLevel WebLevel,
    ReadinessStatus Overall
  );

  /**
   * Converts runtime/configuration facts into one immutable operator-readiness
   * presentation. Native UI code only applies the returned text and severity;
   * it no longer owns readiness policy or duplicates its branch conditions.
   */
  public static class ShowReadinessEvaluator {
    public static ShowReadinessSnapshot Evaluate(ShowReadinessInput input) {
      if (input == null) {
        throw new ArgumentNullException(nameof(input));
      }

      ReadinessStatus engine = input.Running
        ? Ready(
            "✓ Running",
            "Running — live inputs and outputs are being updated.")
        : Disabled("○ Stopped", "Stopped — output is not being updated.");

      bool hasAudio = !string.IsNullOrEmpty(input.SelectedAudioName);
      bool audioReady = false;
      ReadinessStatus audio;
      if (!hasAudio) {
        audio = Error(
          "! No input",
          "Select an active capture device before starting the engine.");
      } else if (!input.Running) {
        audio = Warning(
          "○ Configured",
          input.SelectedAudioName +
            " — start the engine to check its signal.");
        audioReady = true;
      } else if (input.AudioSignal >= 0.005f) {
        audio = Ready(
          "✓ Signal ready",
          input.SelectedAudioName + " — useful audio signal detected.");
        audioReady = true;
      } else {
        audio = Warning(
          "⚠ No signal",
          input.SelectedAudioName +
            " is selected, but the current signal is silent.");
      }

      bool domeReady = !input.DomeEnabled;
      ReadinessStatus dome;
      if (!input.DomeEnabled) {
        dome = Disabled("○ Off", "Dome output is intentionally off.");
      } else if (!input.DomeAddressValid) {
        dome = Error(
          "! Invalid address",
          "OPC address: " + input.DomeAddressError + ".");
      } else if (!input.Running) {
        dome = Warning(
          "○ Waiting",
          "Dome output is enabled; start the engine to connect.");
      } else if (input.DomeFramesPerSecond > 0) {
        dome = Ready(
          "✓ Sending",
          "Frames are reaching the configured OPC controller.");
        domeReady = true;
      } else {
        dome = Warning(
          "⚠ No frames",
          "No OPC frames have been confirmed. Check the address and network.");
      }

      ReadinessStatus wand;
      if (input.ConnectedWandCount > 0) {
        wand = Ready(
          "✓ Ready",
          input.ConnectedWandCount +
            (input.ConnectedWandCount == 1 ? " wand is" : " wands are") +
            " sending orientation data.");
      } else if (string.IsNullOrEmpty(input.WandSerialPort)) {
        wand = Warning(
          "⚠ No wands",
          "No wands detected; no serial receiver port is selected.");
      } else if (input.WandReceiverError != null) {
        wand = Error("! Receiver error", input.WandReceiverError);
      } else {
        wand = Warning(
          "⚠ No wands",
          "Receiver configured, but no wand data is arriving.");
      }

      ReadinessLevel webLevel = input.WebServerError == null
        ? ReadinessLevel.Ready
        : ReadinessLevel.Error;
      string webStatus = input.WebServerError == null
        ? "Ready — listening on port " + input.WebServerPort + "."
        : "Error — web controller could not start: " +
          input.WebServerError;

      ReadinessStatus overall;
      if (!hasAudio ||
          (input.DomeEnabled && !input.DomeAddressValid) ||
          input.WebServerError != null) {
        overall = Error("! Action required", "");
      } else if (input.Running && audioReady && domeReady) {
        overall = Ready("✓ Ready for show", "");
      } else {
        overall = Warning("⚠ Check readiness", "");
      }

      return new ShowReadinessSnapshot(
        input.Running ? "Stop engine" : "Start engine",
        engine,
        audio,
        $"Signal: {Math.Round(input.AudioSignal * 100):0}%",
        dome,
        wand,
        input.ConnectedWandCount +
          (input.ConnectedWandCount == 1
            ? " connected device"
            : " connected devices"),
        webStatus,
        webLevel,
        overall);
    }

    private static ReadinessStatus Disabled(string badge, string detail) =>
      new ReadinessStatus(ReadinessLevel.Disabled, badge, detail);

    private static ReadinessStatus Warning(string badge, string detail) =>
      new ReadinessStatus(ReadinessLevel.Warning, badge, detail);

    private static ReadinessStatus Error(string badge, string detail) =>
      new ReadinessStatus(ReadinessLevel.Error, badge, detail);

    private static ReadinessStatus Ready(string badge, string detail) =>
      new ReadinessStatus(ReadinessLevel.Ready, badge, detail);
  }
}
