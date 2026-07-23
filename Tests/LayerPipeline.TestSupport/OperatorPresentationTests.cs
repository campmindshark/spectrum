using System;
using System.Linq;
using Spectrum.Base;
using static Spectrum.LayerPipeline.Tests.TestAssertions;

namespace Spectrum.LayerPipeline.Tests {

  public static class OperatorPresentationTests {
    public static void Register(Action<string, Action> run) {
      run("readiness evaluator reports blocking configuration failures",
        BlockingReadiness);
      run("readiness evaluator distinguishes ready and stopped systems",
        ReadyAndStoppedReadiness);
      run("wand serial presentation retains configured missing ports",
        MissingWandPort);
      run("wand serial presentation classifies receiver liveness",
        WandReceiverLiveness);
    }

    private static void BlockingReadiness() {
      ShowReadinessSnapshot snapshot = ShowReadinessEvaluator.Evaluate(
        new ShowReadinessInput(
          Running: true,
          SelectedAudioName: null,
          AudioSignal: 0,
          DomeEnabled: true,
          DomeAddressValid: false,
          DomeAddressError: "invalid host",
          DomeFramesPerSecond: 0,
          ConnectedWandCount: 0,
          WandSerialPort: "COM7",
          WandReceiverError: "access denied",
          WebServerError: "port in use",
          WebServerPort: 8080));

      Assert(snapshot.Audio.Level == ReadinessLevel.Error &&
          snapshot.Dome.Level == ReadinessLevel.Error &&
          snapshot.Dome.Detail.Contains("invalid host") &&
          snapshot.Wand.Level == ReadinessLevel.Error &&
          snapshot.WebLevel == ReadinessLevel.Error &&
          snapshot.Overall.Level == ReadinessLevel.Error,
        "blocking readiness inputs did not produce action-required state");
    }

    private static void ReadyAndStoppedReadiness() {
      ShowReadinessSnapshot ready = ShowReadinessEvaluator.Evaluate(
        new ShowReadinessInput(
          Running: true,
          SelectedAudioName: "Line In",
          AudioSignal: 0.2f,
          DomeEnabled: true,
          DomeAddressValid: true,
          DomeAddressError: null,
          DomeFramesPerSecond: 60,
          ConnectedWandCount: 1,
          WandSerialPort: "COM7",
          WandReceiverError: null,
          WebServerError: null,
          WebServerPort: 8080));
      Assert(ready.Engine.Level == ReadinessLevel.Ready &&
          ready.Audio.Level == ReadinessLevel.Ready &&
          ready.Dome.Level == ReadinessLevel.Ready &&
          ready.Wand.Level == ReadinessLevel.Ready &&
          ready.Overall.Level == ReadinessLevel.Ready &&
          ready.ConnectedWandCountText == "1 connected device",
        "fully live inputs did not produce ready-for-show state");

      ShowReadinessSnapshot stopped = ShowReadinessEvaluator.Evaluate(
        new ShowReadinessInput(
          Running: false,
          SelectedAudioName: "Line In",
          AudioSignal: 0,
          DomeEnabled: false,
          DomeAddressValid: true,
          DomeAddressError: null,
          DomeFramesPerSecond: 0,
          ConnectedWandCount: 0,
          WandSerialPort: null,
          WandReceiverError: null,
          WebServerError: null,
          WebServerPort: 8080));
      Assert(stopped.Engine.Level == ReadinessLevel.Disabled &&
          stopped.Audio.Level == ReadinessLevel.Warning &&
          stopped.Dome.Level == ReadinessLevel.Disabled &&
          stopped.Overall.Level == ReadinessLevel.Warning,
        "stopped configured system was not distinguished from a live show");
    }

    private static void MissingWandPort() {
      var options = global::Spectrum.WandSerialPresentationModel
        .BuildPortOptions("COM7", new[] { "COM2", "COM4" });
      Assert(options.Select(option => option.Value).SequenceEqual(
            new[] { "", "COM2", "COM4", "COM7" }) &&
          options[^1].Display == "COM7 (missing)",
        "configured missing receiver port was not retained");

      var present = global::Spectrum.WandSerialPresentationModel
        .BuildPortOptions("COM4", new[] { "COM2", "COM4" });
      Assert(present.Count(option => option.Value == "COM4") == 1 &&
          !present.Any(option => option.Display.Contains("missing")),
        "live configured receiver port was duplicated as missing");
    }

    private static void WandReceiverLiveness() {
      var liveStatus = new global::Spectrum.WandSerialStatus(
        "COM7", true, 200, 300, null);
      var staleStatus = new global::Spectrum.WandSerialStatus(
        "COM7", true, 5000, 4000, null);
      var errorStatus = new global::Spectrum.WandSerialStatus(
        "COM7", false, 5000, 5000, "access denied");

      Assert(global::Spectrum.WandSerialPresentationModel.EvaluateStatus(
            "", liveStatus).Kind ==
          global::Spectrum.WandSerialPresentationKind.Inactive &&
          global::Spectrum.WandSerialPresentationModel.EvaluateStatus(
            "COM7", liveStatus).Kind ==
          global::Spectrum.WandSerialPresentationKind.Ready &&
          global::Spectrum.WandSerialPresentationModel.EvaluateStatus(
            "COM7", staleStatus).Kind ==
          global::Spectrum.WandSerialPresentationKind.Warning &&
          global::Spectrum.WandSerialPresentationModel.EvaluateStatus(
            "COM7", errorStatus).Kind ==
          global::Spectrum.WandSerialPresentationKind.Error,
        "receiver liveness states were not classified consistently");
    }
  }
}
