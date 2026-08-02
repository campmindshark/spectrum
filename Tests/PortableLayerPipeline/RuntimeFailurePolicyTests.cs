using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.Web;
using static Spectrum.LayerPipeline.Tests.LayerPipelineTestFixtures;

namespace Spectrum.LayerPipeline.Tests {

  [TestClass]
  [DoNotParallelize]
  public sealed class RuntimeFailurePolicyTests {

    [TestMethod]
    public void ConsecutiveFailuresOnly() {
      var component = new object();
      var tracker = new ConsecutiveFailureTracker<object>(3);

      FailureUpdate first = tracker.RecordFailure(component);
      tracker.RecordSuccess(component);
      FailureUpdate afterSuccess = tracker.RecordFailure(component);
      FailureUpdate second = tracker.RecordFailure(component);
      FailureUpdate quarantined = tracker.RecordFailure(component);

      Assert.IsTrue(first.ConsecutiveFailures == 1 &&
          afterSuccess.ConsecutiveFailures == 1 &&
          second.ConsecutiveFailures == 2 &&
          !second.NewlyQuarantined &&
          quarantined.ConsecutiveFailures == 3 &&
          quarantined.NewlyQuarantined &&
          tracker.IsQuarantined(component),
        "transient failures accumulated across a successful update");

      tracker.RecordSuccess(component);
      Assert.IsTrue(tracker.IsQuarantined(component),
        "successful work incorrectly released an existing quarantine");
      tracker.Reset();
      Assert.IsTrue(!tracker.IsQuarantined(component),
        "engine restart did not clear component quarantine");
    }

    [TestMethod]
    public void InputUpdateQuarantine() {
      var input = new ThrowingUpdateAudioInput();
      var op = CreateOperator(input);
      try {
        op.Enabled = true;
        Assert.IsTrue(SpinWait.SpinUntil(
            () => input.UpdateCount >= 3 &&
              op.Telemetry.InputFault?.Contains(
                nameof(ThrowingUpdateAudioInput),
                StringComparison.Ordinal) == true,
            TimeSpan.FromSeconds(3)),
          "persistent input update failure was not quarantined and reported");

        int quarantinedCount = input.UpdateCount;
        Thread.Sleep(100);
        Assert.IsTrue(input.UpdateCount == quarantinedCount && !input.Active,
          "quarantined input continued updating");

        op.Enabled = false;
        op.Enabled = true;
        Assert.IsTrue(SpinWait.SpinUntil(
            () => input.UpdateCount >= quarantinedCount + 3,
            TimeSpan.FromSeconds(3)),
          "engine restart did not reset the input quarantine");
      } finally {
        op.Enabled = false;
      }
    }

    [TestMethod]
    public void InputActivationQuarantine() {
      var input = new ThrowingActivationAudioInput();
      var op = CreateOperator(input);
      try {
        op.Enabled = true;
        Assert.IsTrue(SpinWait.SpinUntil(
            () => input.ActivationAttempts == 1 &&
              op.Telemetry.InputFault?.Contains(
                nameof(ThrowingActivationAudioInput),
                StringComparison.Ordinal) == true,
            TimeSpan.FromSeconds(3)),
          "input activation failure was not isolated and reported");

        Thread.Sleep(100);
        Assert.IsTrue(input.ActivationAttempts == 1 && input.UpdateCount == 0,
          "failed input activation was retried or updated in the same run");
      } finally {
        op.Enabled = false;
      }
    }

    [TestMethod]
    public void FaultTelemetryCatalog() {
      string[] keys = TelemetryCatalog.Items
        .Select(item => item.Key)
        .ToArray();
      Assert.IsTrue(keys.Contains("visualizerFault", StringComparer.Ordinal) &&
          keys.Contains("inputFault", StringComparer.Ordinal) &&
          keys.Contains("outputFault", StringComparer.Ordinal),
        "component fault telemetry is missing from the web event catalog");
    }

    private static Operator CreateOperator(IAudioLevelInput input) {
      var config = ConfigurationWithLayers(
        Layer("background", "failure-policy-background"));
      config.domeSimulationEnabled = true;
      return new Operator(
        config,
        new InlineGateway(),
        new FailureInputFactory(input),
        connectHardware: false);
    }

    private sealed class FailureInputFactory : ISpectrumInputFactory {
      private readonly IAudioLevelInput audio;

      public FailureInputFactory(IAudioLevelInput audio) {
        this.audio = audio;
      }

      public IAudioLevelInput CreateAudioInput(
        Configuration config,
        BeatBroadcaster beat
      ) => this.audio;

      public IMidiControlInput CreateMidiInput(
        Configuration config,
        BeatBroadcaster beat,
        ApplicationStateDispatcher stateDispatcher
      ) => new FakeMidiControlInput();
    }

    private sealed class ThrowingUpdateAudioInput : IAudioLevelInput {
      private volatile bool active;
      private int updateCount;

      public bool Active {
        get => this.active;
        set => this.active = value;
      }
      public bool Enabled => true;
      public float Volume => 0;
      public int UpdateCount => Volatile.Read(ref this.updateCount);

      public void OperatorUpdate() {
        Interlocked.Increment(ref this.updateCount);
        throw new InvalidOperationException("test update failure");
      }
    }

    private sealed class ThrowingActivationAudioInput : IAudioLevelInput {
      private int activationAttempts;
      private int updateCount;

      public bool Active {
        get => false;
        set {
          if (value) {
            Interlocked.Increment(ref this.activationAttempts);
            throw new InvalidOperationException("test activation failure");
          }
        }
      }
      public bool Enabled => true;
      public float Volume => 0;
      public int ActivationAttempts =>
        Volatile.Read(ref this.activationAttempts);
      public int UpdateCount => Volatile.Read(ref this.updateCount);

      public void OperatorUpdate() =>
        Interlocked.Increment(ref this.updateCount);
    }
  }
}
