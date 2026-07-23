using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectrum.Base;

namespace Spectrum.Web {

  /**
   * The in-process Kestrel lifecycle owner for the web control surface.
   * Endpoint registration belongs to the user, maintenance, and event route
   * modules; this class owns listener startup/shutdown and background work.
   */
  public sealed class WebServer {

    private readonly AdvisoryLockManager locks;
    private readonly DomeCalibrationController calibration;
    private readonly UserApiRoutes userRoutes;
    private readonly MaintenanceApiRoutes maintenanceRoutes;
    private readonly EventApiRoutes eventRoutes;
    private readonly bool domeSimulatorEnabled;
    private readonly Action<Exception>? reportBackgroundError;
    private readonly int port;
    private WebApplication? app;
    private Task? hostLifetimeTask;

    // Watchdog that reconciles the modal dome-calibration flow with its advisory
    // lease. A web client can disappear without explicitly cancelling the flow;
    // once its lease is gone, this task returns the dome to normal rendering.
    private CancellationTokenSource? calibrationWatchdogCancellation;
    private Task? calibrationWatchdogTask;
    private static readonly TimeSpan CalibrationWatchdogInterval =
      TimeSpan.FromSeconds(3);

    public WebServer(
      ControlService controls,
      ConfigEventStream events,
      AdvisoryLockManager locks,
      DomeCalibrationController calibration,
      WandStatusController wands,
      OperatorController operatorControl,
      TempoController tempo,
      LayersController layers,
      SceneController scenes,
      PaletteController palettes,
      AudioDeviceController audio,
      WebDomeSimulator? domeSimulator,
      int port,
      Action<Exception>? reportBackgroundError = null
    ) {
      this.locks = locks;
      this.calibration = calibration;
      this.port = port;
      this.reportBackgroundError = reportBackgroundError;
      this.domeSimulatorEnabled = domeSimulator != null;

      var parameterWrites = new ParameterWriteHandler(controls, locks);
      this.userRoutes = new UserApiRoutes(
        controls,
        operatorControl,
        layers,
        scenes,
        palettes,
        wands,
        domeSimulator,
        parameterWrites);
      this.maintenanceRoutes = new MaintenanceApiRoutes(
        controls,
        audio,
        operatorControl,
        locks,
        calibration,
        wands,
        tempo,
        parameterWrites);
      this.eventRoutes = new EventApiRoutes(controls, events);
    }

    // Builds and starts Kestrel. Returns once the listener is bound.
    public void Start() {
      var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
        ContentRootPath = AppContext.BaseDirectory,
      });
      builder.Logging.ClearProviders();
      builder.WebHost.UseKestrel();
      builder.WebHost.UseUrls($"http://0.0.0.0:{this.port}");

      WebApplication app = builder.Build();
      if (this.domeSimulatorEnabled) {
        app.UseWebSockets();
      }
      app.UseDefaultFiles();
      app.UseStaticFiles();
      this.userRoutes.Map(app);
      this.maintenanceRoutes.Map(app);
      this.eventRoutes.Map(app);

      try {
        app.StartAsync().GetAwaiter().GetResult();
        this.hostLifetimeTask = app.WaitForShutdownAsync();
        this.app = app;
      } catch {
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        throw;
      }

      this.calibrationWatchdogCancellation = new CancellationTokenSource();
      this.calibrationWatchdogTask = this.RunCalibrationWatchdogAsync(
        this.calibrationWatchdogCancellation.Token);
    }

    public async Task StopAsync() {
      CancellationTokenSource? watchdogCancellation =
        this.calibrationWatchdogCancellation;
      Task? watchdogTask = this.calibrationWatchdogTask;
      if (watchdogCancellation != null) {
        watchdogCancellation.Cancel();
        try {
          if (watchdogTask != null) {
            await watchdogTask.ConfigureAwait(false);
          }
        } catch (OperationCanceledException)
            when (watchdogCancellation.IsCancellationRequested) {
          // Cancellation is the normal watchdog shutdown path.
        } finally {
          watchdogCancellation.Dispose();
          this.calibrationWatchdogCancellation = null;
          this.calibrationWatchdogTask = null;
        }
      }

      WebApplication? app = this.app;
      if (app != null) {
        await app.StopAsync(TimeSpan.FromSeconds(2))
          .ConfigureAwait(false);
        Task? hostLifetimeTask = this.hostLifetimeTask;
        if (hostLifetimeTask != null) {
          await hostLifetimeTask.ConfigureAwait(false);
          this.hostLifetimeTask = null;
        }
        await app.DisposeAsync().ConfigureAwait(false);
        this.app = null;
      }
    }

    private async Task RunCalibrationWatchdogAsync(
      CancellationToken cancellationToken
    ) {
      using var timer = new PeriodicTimer(CalibrationWatchdogInterval);
      while (await timer.WaitForNextTickAsync(cancellationToken)
          .ConfigureAwait(false)) {
        try {
          await this.ReconcileCalibrationLeaseAsync().ConfigureAwait(false);
        } catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested) {
          throw;
        } catch (Exception error) {
          try {
            this.reportBackgroundError?.Invoke(error);
          } catch {
            // A diagnostics sink must not terminate this retry loop.
          }
        }
      }
    }

    private async Task ReconcileCalibrationLeaseAsync() {
      var state = await this.calibration.StateAsync().ConfigureAwait(false);
      if (!state.active ||
          this.locks.Get(LockPolicy.DomeCalibration) != null) {
        return;
      }
      await this.calibration.CancelAsync().ConfigureAwait(false);
    }
  }
}
