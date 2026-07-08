using System.Threading.Tasks;
using Spectrum.Base;

namespace Spectrum.Web {

  /**
   * The web control behind the maintenance "Tap" tempo button. The native VJ HUD
   * computes tempo from tap timing on the server (BeatBroadcaster.AddTap); the
   * web client instead measures its own taps in the browser and posts the
   * resulting BPM here, so a phone's taps aren't distorted by request latency.
   * This also mirrors the native Tap button's other effect: it forces the BPM
   * source to Human (beatInput 0), since a tap only means anything against the
   * human/tap tempo.
   *
   * Like OperatorController this is deliberately NOT a ParameterRegistry entry —
   * it's a momentary action against live BeatBroadcaster state, not a persisted
   * field. The mutation is marshaled through the ControlGateway so PropertyChanged
   * ("beatInput", "BPMString") fires on the UI thread exactly as a native tap
   * would, keeping the source dropdown, BPM telemetry, and other clients coherent.
   */
  public sealed class TempoController {

    private readonly Configuration config;
    private readonly BeatBroadcaster beat;
    private readonly ControlGateway gateway;

    public TempoController(
      Configuration config, BeatBroadcaster beat, ControlGateway gateway
    ) {
      this.config = config;
      this.beat = beat;
      this.gateway = gateway;
    }

    // Applies a browser-computed BPM as the human tap tempo, switching the BPM
    // source to Human to match the native Tap button.
    public async Task SetManualBPMAsync(double bpm) {
      await this.gateway.InvokeAsync(() => {
        this.config.beatInput = 0;
        this.beat.SetManualBPM(bpm);
      });
    }
  }
}
