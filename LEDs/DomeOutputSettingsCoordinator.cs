using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using Spectrum.Base;

namespace Spectrum.LEDs {

  // Owns publication and operator-thread application of the output settings
  // that reconfigure installed-wiring projection or the OPC transport.
  // Configuration notifications only mark pending work; callers that are
  // about to address pixels or flush hardware apply one immutable snapshot.
  internal sealed class DomeOutputSettingsCoordinator {
    private readonly Configuration config;
    private readonly IRuntimeSettingsConfiguration runtimeSettings;
    private readonly DomeOutputMapper outputMapper;
    private readonly DomeOpcTransport transport;
    private long appliedMappingGeneration;
    private long appliedTransportGeneration;
    private int settingsPending;

    internal DomeOutputSettingsCoordinator(
      Configuration config,
      IRuntimeSettingsConfiguration runtimeSettings,
      DomeOutputMapper outputMapper,
      DomeOpcTransport transport
    ) {
      this.config = config ??
        throw new ArgumentNullException(nameof(config));
      this.runtimeSettings = runtimeSettings ??
        throw new ArgumentNullException(nameof(runtimeSettings));
      this.outputMapper = outputMapper ??
        throw new ArgumentNullException(nameof(outputMapper));
      this.transport = transport ??
        throw new ArgumentNullException(nameof(transport));

      DomeOutputSettingsSnapshot initialSettings =
        this.runtimeSettings.DomeOutputSettingsSnapshot;
      this.outputMapper.Apply(initialSettings);
      this.appliedMappingGeneration =
        initialSettings.MappingGeneration;
      this.appliedTransportGeneration =
        initialSettings.TransportGeneration;
      this.config.PropertyChanged += this.ConfigUpdated;
    }

    internal long AppliedMappingGeneration =>
      Volatile.Read(ref this.appliedMappingGeneration);

    internal long AppliedTransportGeneration =>
      Volatile.Read(ref this.appliedTransportGeneration);

    internal event Action? SettingsApplied;

    internal bool Active {
      get { return this.transport.Active; }
      set {
        if (value == this.transport.Active) {
          return;
        }
        if (value) {
          Interlocked.Exchange(ref this.settingsPending, 1);
          this.EnsureApplied();
          this.transport.Activate(
            this.runtimeSettings.DomeOutputSettingsSnapshot);
        } else {
          this.transport.Deactivate();
        }
      }
    }

    internal void EnsureApplied() {
      if (Interlocked.Exchange(ref this.settingsPending, 0) == 0) {
        return;
      }

      bool changed = false;
      DomeOutputSettingsSnapshot settings =
        this.runtimeSettings.DomeOutputSettingsSnapshot;
      if (settings.MappingGeneration != this.appliedMappingGeneration) {
        this.outputMapper.Apply(settings);
        this.appliedMappingGeneration = settings.MappingGeneration;
        changed = true;
      }
      if (settings.TransportGeneration != this.appliedTransportGeneration) {
        this.appliedTransportGeneration = settings.TransportGeneration;
        changed = true;
        this.transport.ApplySettings(settings);
      }
      if (changed) {
        this.PublishSettingsApplied();
      }
    }

    private void ConfigUpdated(
      object? sender,
      PropertyChangedEventArgs e
    ) {
      if (e.PropertyName == nameof(this.config.domeCableMapping) ||
          e.PropertyName == nameof(this.config.domePortMappings) ||
          e.PropertyName == nameof(this.config.domeBeagleboneOPCAddress) ||
          e.PropertyName == nameof(this.config.domeOutputInSeparateThread) ||
          e.PropertyName == nameof(this.config.domeEnabled)) {
        Interlocked.Exchange(ref this.settingsPending, 1);
      }
    }

    private void PublishSettingsApplied() {
      try {
        this.SettingsApplied?.Invoke();
      } catch (Exception error) {
        Debug.WriteLine(
          "Dome output settings observer failed: " + error);
      }
    }
  }
}
