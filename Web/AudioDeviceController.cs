using System;
using System.Collections.Generic;
using Spectrum.Base;

namespace Spectrum.Web {

  /** Thread-safe read model for audio setup and capture health. */
  public sealed class AudioDeviceController {
    private readonly IAudioLevelInput input;
    private readonly IAudioDeviceProvider? devices;
    private readonly IRuntimeSettingsConfiguration runtimeSettings;

    public AudioDeviceController(
      IAudioLevelInput input,
      Configuration config
    ) {
      this.input = input ?? throw new ArgumentNullException(nameof(input));
      this.devices = input as IAudioDeviceProvider;
      this.runtimeSettings = config as IRuntimeSettingsConfiguration ??
        throw new ArgumentException(
          "Audio setup requires immutable runtime settings.", nameof(config));
    }

    public object State() {
      IReadOnlyList<AudioCaptureDevice> available;
      string? discoveryError = null;
      if (this.devices == null) {
        available = Array.Empty<AudioCaptureDevice>();
        discoveryError = "The active audio backend does not support discovery.";
      } else {
        try {
          available = this.devices.GetAvailableDevices() ??
            Array.Empty<AudioCaptureDevice>();
        } catch (Exception error) {
          available = Array.Empty<AudioCaptureDevice>();
          discoveryError = error.Message;
        }
      }

      return new {
        backend = this.devices?.BackendName ?? "Unknown",
        selectedDeviceId =
          this.runtimeSettings.AudioSettingsSnapshot.DeviceId,
        active = this.input.Active,
        volume = this.input.Volume,
        lastError = discoveryError ?? this.devices?.LastError,
        availableDevices = available,
      };
    }
  }
}
