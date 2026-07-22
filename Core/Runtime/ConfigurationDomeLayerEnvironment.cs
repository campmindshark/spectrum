using Spectrum.Base;
using System.Threading;

namespace Spectrum {

  // Adapts one immutable runtime-control generation to the live values layer
  // renderers are allowed to observe.
  internal sealed class ConfigurationDomeLayerEnvironment
      : DomeLayerEnvironment {
    private DomeShowStateSnapshot frameShowState =
      DomeShowStateSnapshot.Empty;
    private DomeRuntimeFrameSnapshot frameRuntime =
      DomeRuntimeFrameSnapshot.Empty;

    public void BeginOperatorFrame(
      DomeShowStateSnapshot showState,
      DomeRuntimeFrameSnapshot runtime
    ) {
      Volatile.Write(
        ref this.frameShowState,
        showState ?? DomeShowStateSnapshot.Empty);
      Volatile.Write(
        ref this.frameRuntime,
        runtime ?? DomeRuntimeFrameSnapshot.Empty);
    }

    public double GlobalFadeSpeed =>
      Volatile.Read(ref this.frameShowState).GlobalFadeSpeed;

    // Preserve the historical multiplication and truncation order used by the
    // two random-color renderers.
    public int OutputBrightnessByte {
      get {
        DomeRuntimeFrameSnapshot runtime =
          Volatile.Read(ref this.frameRuntime);
        return (int)(
          0xFF * runtime.MaxBrightness * runtime.Brightness);
      }
    }

    public int SpotlightDeviceId =>
      Volatile.Read(ref this.frameRuntime).SpotlightDeviceId;

    public int FireGeneration(LayerInstanceId instanceId) =>
      Volatile.Read(ref this.frameRuntime).FireGeneration(instanceId.Value);

    public int ClearGeneration(LayerInstanceId instanceId) =>
      Volatile.Read(ref this.frameRuntime).ClearGeneration(instanceId.Value);
  }
}
