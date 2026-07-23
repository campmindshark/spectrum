using System;
using System.Threading;
using Spectrum.Base;

namespace Spectrum.LEDs {

  // Owns OPC client creation, activation, transport reconfiguration, and the
  // persistent hardware frame state. LEDDomeOutput remains responsible for
  // logical topology/mapping and delegates device-address publication here.
  internal sealed class DomeOpcTransport {
    private readonly RuntimeTelemetry telemetry;
    private readonly TimeSpan? minSendInterval;
    private OPCAPI? opc;
    private bool active;
    private DomeOutputMapping? lastMapping;

    internal DomeOpcTransport(
      RuntimeTelemetry telemetry,
      TimeSpan? minSendInterval = null
    ) {
      if (minSendInterval < TimeSpan.Zero) {
        throw new ArgumentOutOfRangeException(nameof(minSendInterval));
      }
      this.telemetry = telemetry;
      this.minSendInterval = minSendInterval;
    }

    internal bool Active => this.active;
    internal bool CanWrite => this.opc != null;
    internal WaitHandle? PendingConnectWaitHandle =>
      this.opc?.PendingConnectWaitHandle;

    internal void Activate(DomeOutputSettingsSnapshot settings) {
      if (this.active) {
        return;
      }
      this.active = true;
      if (settings.Enabled &&
          (this.opc == null || !this.opc.Active)) {
        this.Initialize(settings);
      }
    }

    internal void Deactivate() {
      if (!this.active) {
        return;
      }
      this.active = false;
      if (this.opc != null) {
        this.opc.Active = false;
      }
    }

    internal void ApplySettings(DomeOutputSettingsSnapshot settings) {
      if (this.opc != null) {
        this.opc.Active = false;
      }
      if (this.active && settings.Enabled) {
        this.Initialize(settings);
      }
    }

    internal void PrepareMapping(DomeOutputMapping mapping) {
      if (this.opc != null &&
          !ReferenceEquals(mapping, this.lastMapping)) {
        this.opc.ClearPixels();
        this.lastMapping = mapping;
      }
    }

    internal void SetPixel(int pixelIndex, int color) {
      this.opc?.SetPixel(pixelIndex, color);
    }

    internal void Flush() {
      this.opc?.Flush();
    }

    internal void OperatorUpdate() {
      this.opc?.OperatorUpdate();
    }

    private void Initialize(DomeOutputSettingsSnapshot settings) {
      string opcAddress = settings.OpcAddress;
      if (opcAddress.Split(':').Length < 3) {
        opcAddress += ":0";
      }
      Action<int> publishFramesPerSecond =
        newFramesPerSecond =>
          this.telemetry.DomeBeagleboneOPCFPS = newFramesPerSecond;
      this.opc = this.minSendInterval.HasValue
        ? new OPCAPI(
            opcAddress,
            settings.OutputInSeparateThread,
            publishFramesPerSecond,
            this.minSendInterval.Value)
        : new OPCAPI(
            opcAddress,
            settings.OutputInSeparateThread,
            publishFramesPerSecond);
      this.opc.Active = this.active;
    }
  }
}
