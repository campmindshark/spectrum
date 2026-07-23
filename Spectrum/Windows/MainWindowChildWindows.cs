using System;
using Spectrum.Web;

namespace Spectrum {

  // Owns construction and single-instance lifetime for the auxiliary operator
  // windows. MainWindow only forwards routed commands and no longer coordinates
  // four independent nullable window/event lifecycles.
  internal sealed class MainWindowChildWindows {
    private readonly SpectrumConfiguration configuration;
    private readonly Operator runtime;
    private readonly DomeCalibrationController calibration;
    private readonly AdvisoryLockManager advisoryLocks;
    private DomeSimulatorWindow? domeSimulator;
    private DomeMappingWindow? domeMapping;
    private VJHUDWindow? vjHud;
    private WandStatusWindow? wandStatus;

    internal MainWindowChildWindows(
      SpectrumConfiguration configuration,
      Operator runtime,
      DomeCalibrationController calibration,
      AdvisoryLockManager advisoryLocks
    ) {
      this.configuration = configuration;
      this.runtime = runtime;
      this.calibration = calibration;
      this.advisoryLocks = advisoryLocks;
    }

    internal void OpenVjHud() {
      if (this.vjHud != null) {
        this.vjHud.Activate();
        return;
      }
      this.vjHud = new VJHUDWindow(
        this.configuration,
        this.runtime.BeatBroadcaster,
        this.runtime.OrientationInput);
      this.vjHud.Closed += this.VjHudClosed;
      this.vjHud.Show();
    }

    internal void CloseVjHud() {
      this.vjHud?.Close();
      this.vjHud = null;
    }

    internal void OpenDomeSimulator() {
      if (this.domeSimulator != null) {
        this.domeSimulator.Activate();
        return;
      }
      this.domeSimulator = new DomeSimulatorWindow(
        this.configuration, this.runtime.DomeOutput);
      this.domeSimulator.Closed += this.DomeSimulatorClosed;
      this.domeSimulator.Show();
    }

    internal void CloseDomeSimulator() {
      this.domeSimulator?.Close();
      this.domeSimulator = null;
    }

    internal void OpenDomeMapping() {
      if (this.domeMapping != null) {
        this.domeMapping.Activate();
        return;
      }
      this.domeMapping = new DomeMappingWindow(
        this.calibration, this.advisoryLocks);
      this.domeMapping.Closed += this.DomeMappingClosed;
      this.domeMapping.Show();
    }

    internal void OpenWandStatus() {
      if (this.wandStatus != null) {
        this.wandStatus.Activate();
        return;
      }
      this.wandStatus = new WandStatusWindow(
        this.configuration,
        this.runtime.OrientationInput);
      this.wandStatus.Closed += this.WandStatusClosed;
      this.wandStatus.Show();
    }

    private void VjHudClosed(object? sender, EventArgs e) {
      this.vjHud = null;
      this.configuration.vjHUDEnabled = false;
    }

    private void DomeSimulatorClosed(object? sender, EventArgs e) {
      this.domeSimulator = null;
      this.configuration.domeSimulationEnabled = false;
    }

    private void DomeMappingClosed(object? sender, EventArgs e) {
      this.domeMapping = null;
    }

    private void WandStatusClosed(object? sender, EventArgs e) {
      this.wandStatus = null;
    }
  }
}
