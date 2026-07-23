using System;
using Spectrum.LEDs;
using static Spectrum.LayerPipeline.Tests.TestAssertions;

namespace Spectrum.LayerPipeline.Tests {

  public static class DomeOutputPublicationTests {
    public static void Register(Action<string, Action> run) {
      run("native simulator publication uses a replaceable frame mailbox",
        NativeFrameMailbox);
      run("browser simulator publication remains independent from native state",
        IndependentWebChannel);
    }

    private static void NativeFrameMailbox() {
      var publisher = new DomeSimulatorPublisher {
        NativeHasConsumer = true,
      };

      publisher.PublishPixel(3, 4, 0x102030, simulationEnabled: true);
      Assert(publisher.NativeCommands.Count == 1,
        "native diagnostic pixel was not queued");

      DomeSimulatorFrameCapture capture =
        publisher.BeginFrame(3, simulationEnabled: true);
      Assert(capture.Enabled && capture.NativeFrame != null &&
          capture.WebFrame == null,
        "native frame capture did not follow consumer state");
      capture.SetColor(0, 0x112233);
      capture.SetColor(1, 0x445566);
      capture.SetColor(2, 0x778899);
      publisher.CompleteFrame(capture, simulationEnabled: true);

      Assert(publisher.NativeCommands.IsEmpty,
        "normal frame did not supersede older diagnostics");
      bool hadFrame = publisher.TryTakeNativeFrame(out int[]? frame);
      Assert(hadFrame && frame != null &&
          frame[0] == 0x112233 &&
          frame[1] == 0x445566 &&
          frame[2] == 0x778899,
        "native mailbox did not retain the completed frame");
      DomeSimulatorPublisher.ReturnFrame(frame);

      publisher.FlushFrame(simulationEnabled: true);
      Assert(publisher.NativeCommands.IsEmpty,
        "completed normal frame also published a redundant flush command");
      publisher.FlushFrame(simulationEnabled: true);
      Assert(publisher.NativeCommands.TryDequeue(out var flush) &&
          flush.isFlush,
        "diagnostic-only frame did not publish its flush command");

      DomeSimulatorFrameCapture disabled =
        publisher.BeginFrame(1, simulationEnabled: true);
      disabled.SetColor(0, 0x010101);
      publisher.CompleteFrame(disabled, simulationEnabled: false);
      Assert(!publisher.TryTakeNativeFrame(out _),
        "native frame survived simulation being disabled during capture");

      DomeSimulatorFrameCapture abandoned =
        publisher.BeginFrame(1, simulationEnabled: true);
      abandoned.SetColor(0, 0xABCDEF);
      publisher.CompleteFrame(abandoned, simulationEnabled: true);
      publisher.NativeHasConsumer = false;
      Assert(!publisher.TryTakeNativeFrame(out _),
        "closing the native consumer retained a pooled frame");
    }

    private static void IndependentWebChannel() {
      var publisher = new DomeSimulatorPublisher {
        WebHasConsumer = true,
      };

      publisher.PublishPixel(2, 5, 0x123456, simulationEnabled: false);
      Assert(publisher.NativeCommands.IsEmpty &&
          publisher.WebCommands.Count == 1,
        "browser diagnostics depended on native simulation state");

      DomeSimulatorFrameCapture capture =
        publisher.BeginFrame(2, simulationEnabled: false);
      Assert(capture.Enabled && capture.NativeFrame == null &&
          capture.WebFrame != null,
        "browser frame capture activated the native mailbox");
      capture.SetColor(0, 0x010203);
      capture.SetColor(1, 0xA0B0C0);
      publisher.CompleteFrame(capture, simulationEnabled: false);

      bool hadFrame = publisher.TryTakeWebFrame(out int[]? frame);
      Assert(publisher.WebCommands.IsEmpty &&
          hadFrame && frame != null &&
          frame[0] == 0x010203 &&
          frame[1] == 0xA0B0C0,
        "browser mailbox did not replace its diagnostic backlog");
      DomeSimulatorPublisher.ReturnFrame(frame);

      publisher.WebHasConsumer = false;
      Assert(!publisher.TryTakeWebFrame(out _) &&
          publisher.WebCommands.IsEmpty,
        "closing the browser consumer retained publication state");
    }
  }
}
