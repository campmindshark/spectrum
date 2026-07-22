using System;
using System.Threading;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.LEDs;

namespace Spectrum.Web {

  /**
   * Owns the browser-control composition and lifetime independently of WPF.
   * Moving this class with the remaining runtime code will let the Windows and
   * headless frontends use the same web surface unchanged.
   */
  public sealed class SpectrumWebHost : ISpectrumHostService {
    private readonly ConfigEventStream eventStream;
    private readonly WebServer server;
    private int stopped;

    public SpectrumWebHost(
      SpectrumConfiguration config,
      ApplicationStateDispatcher stateDispatcher,
      Operator runtime,
      int port
    ) {
      if (config == null) {
        throw new ArgumentNullException(nameof(config));
      }
      if (stateDispatcher == null) {
        throw new ArgumentNullException(nameof(stateDispatcher));
      }
      if (runtime == null) {
        throw new ArgumentNullException(nameof(runtime));
      }

      ParameterRegistry registry = SpectrumParameters.BuildRegistry();
      var controls = new ControlService(
        registry, stateDispatcher, config);
      this.eventStream = new ConfigEventStream(
        registry, config, runtime, runtime.Telemetry,
        runtime.BeatBroadcaster);
      this.AdvisoryLocks = new AdvisoryLockManager();
      this.DomeCalibration = new DomeCalibrationController(
        stateDispatcher, config, runtime.DomeCalibration,
        LEDDomeOutput.NumCables);
      var wands = new WandStatusController(
        runtime.OrientationInput, stateDispatcher, config);
      var audio = new AudioDeviceController(runtime.AudioInput, config);
      var operatorControl = new OperatorController(
        runtime, stateDispatcher);
      var tempo = new TempoController(
        config, runtime.BeatBroadcaster, stateDispatcher);
      var layers = new LayersController(stateDispatcher, config);
      var scenes = new SceneController(stateDispatcher, config);
      var palettes = new PaletteController(stateDispatcher, config);
      var domeSimulator = config.webDomeSimulatorEnabled
        ? new WebDomeSimulator(runtime.DomeOutput)
        : null;
      this.server = new WebServer(
        controls, this.eventStream, this.AdvisoryLocks,
        this.DomeCalibration, wands, operatorControl, tempo, layers,
        scenes, palettes, audio, domeSimulator, port);
    }

    public AdvisoryLockManager AdvisoryLocks { get; }

    public DomeCalibrationController DomeCalibration { get; }

    public void Start() {
      try {
        this.server.Start();
      } catch {
        this.eventStream.Dispose();
        throw;
      }
    }

    public async Task StopAsync() {
      if (Interlocked.Exchange(ref this.stopped, 1) != 0) {
        return;
      }
      try {
        await this.server.StopAsync().ConfigureAwait(false);
      } finally {
        this.eventStream.Dispose();
      }
    }
  }
}
