# Spectrum development cheat sheet

Terse offline reference for code changes and incident diagnosis. For reasoning,
procedures, and recovery detail, use the
[On-playa Development and Troubleshooting Guide](on-playa-development-and-troubleshooting.md).

## Do not learn this the hard way

- The packaged default enables dome output at `192.168.1.69:7890`, maximum
  brightness `1`, current brightness `0.356915762888129`.
- The headless host starts the engine automatically. A new empty data directory
  can therefore send OPC immediately.
- For a safe scratch run, copy `Core/Configuration/spectrum_default_config.xml`
  to a private data directory as `spectrum_config.xml`, then set
  `domeEnabled=false`, `domeMaxBrightness` low, and `domeTestPattern=0`.
- The web UI/API has no authentication. Keep it on a trusted show network.
- `build.ps1` deletes `artifacts/` before building. Copy any known-good package
  first.

## Magic constants: rates and failure policy

| Meaning | Value | Source of truth | Also check |
| --- | ---: | --- | --- |
| Operator ceiling | 400 Hz | `Core/Runtime/Operator.cs` — `MaxFramesPerSecond` | Comments in timing-sensitive visualizers |
| OPC send ceiling | 200 Hz | `LEDs/OPCAPI.cs` — `MaxRefreshRateHz` | Independent of operator rate |
| Browser simulator ceiling | 60 FPS | `LEDs/DomeSimulatorPublisher.cs` — `WebFramesPerSecond` | `Web/WebDomeSimulator.cs`, `Web/wwwroot/dome-renderer.js` |
| OPC reconnect delay | 250 ms | `LEDs/OPCAPI.cs` — `ReconnectDelayMs` | OPC tests |
| OPC connect timeout | 2,000 ms | `LEDs/OPCAPI.cs` — `ConnectTimeoutMs` | OPC tests |
| Visualizer quarantine | 10 consecutive failures | `Core/Runtime/Operator.cs` — `VisualizerQuarantineThreshold` | Runtime-failure tests |
| Input/output quarantine | 3 consecutive failures | `Core/Runtime/Operator.cs` — `DeviceQuarantineThreshold` | Activation failure is immediate |
| Configuration-save debounce | 100 ms | Passed by `Host/Program.cs` and `Spectrum/Windows/WindowsSpectrumApplication.cs` | `Base/ConfigurationPersistenceCoordinator.cs` |
| Advisory-lock lease | 15 s | `Web/AdvisoryLockManager.cs` — constructor default | Browser lock renewal |
| Calibration watchdog | 3 s | `Web/WebServer.cs` — `CalibrationWatchdogInterval` | Advisory-lock behavior |
| Kestrel stop timeout | 2 s | `Web/WebServer.cs` — `StopAsync(...)` | Shutdown tests |

A quarantined component stays quarantined until the engine is restarted.
A successful update clears a transient failure streak.

## Magic constants: inputs and protocols

| Meaning | Value | Source of truth | Also check |
| --- | ---: | --- | --- |
| Web listener | TCP 8080 | Desktop: `Spectrum/Windows/WindowsSpectrumApplication.cs`; headless: `Host/Program.cs` | Headless `--port` overrides it |
| Pro DJ Link beat listener | UDP 50001 | `Core/ProDjLinkInput.cs` — `DefaultPort` | Passive listener; no virtual CDJ |
| Pro DJ Link source stale time | 2 s | `Core/ProDjLinkInput.cs` — `DeviceTimeoutMs` | Mixer device 33 is preferred |
| Plausible Pro DJ Link BPM | 20–500 | `Core/ProDjLinkInput.cs` | Packet offsets and magic header live there too |
| Orientation/wand listener | UDP 5005 | `Core/Inputs/OrientationInput.cs` — `DEVICE_LISTEN_PORT` | Installation/firewall rules |
| Orientation device stale time | 1,000 ms | `Core/Inputs/OrientationInput.cs` — `DEVICE_TIMEOUT_MS` | Device presentation |
| Wand radio ceiling | 200 Hz | `Core/Inputs/OrientationInput.cs` — `WandMaxTransmitRateHz` | Mirrored in `Web/wwwroot/wands*.js` |
| Serial receiver alive time | 1,500 ms | `Core/Inputs/WandSerialReceiver.cs` — `RECEIVER_ALIVE_MS` | Mirrored in `Web/wwwroot/wands.js` |
| Serial baud | 115200 nominal | `Core/Inputs/WandSerialReceiver.cs` | USB CDC ignores the nominal baud |
| Serial read timeout | 200 ms | `Core/Inputs/WandSerialReceiver.cs` — `ReadTimeoutMs` | Retry/idle waits are 1 s |
| Linux ALSA format | S16_LE, 44.1 kHz, stereo then mono | `Platform.Linux/AlsaAudioLevelInput.cs` | `SampleRate` |
| Linux ALSA block | 1,024 frames | `Platform.Linux/AlsaAudioLevelInput.cs` — `FramesPerRead` | Target latency is configured nearby |
| ALSA retry | 1 s | `Platform.Linux/AlsaAudioLevelInput.cs` | Capture diagnostics |
| Madmom child restart | 2 s | `Audio/MadmomHandler.cs`, `Platform.Linux/MadmomPcmBeatTracker.cs` | Both platform implementations |
| Tap-tempo conclusion | 2,000 ms | `Base/BeatBroadcaster.cs` — `tapTempoConclusionTime` | Mirrored by `TAP_CONCLUSION_MS` in `Web/wwwroot/app.js` |
| Madmom beat stale time | 2,500 ms | `Base/BeatBroadcaster.cs` — `madmomBeatTimeout` | Tempo-source selection |

## Dome geometry and output mapping

| Fact | Value/location |
| --- | --- |
| Logical dome | 190 struts; 7,580 LEDs |
| Strut lengths | 34, 40, 42, or 44 LEDs in `LEDs/DomeWiringLayout.cs` |
| Controllers | 5 boxes |
| Ports | 8 per box |
| Cable endpoints | 10 (A/B per box); 4 strands each |
| Raw installed geometry | `LEDs/DomeWiringLayout.cs` |
| Cable/controller and port permutations | `LEDs/DomeOutputMapper.cs` |
| Persisted permutations | `domeCableMapping`, `domePortMappings` in configuration |
| OPC framing/address parsing | `LEDs/OPCAPI.cs` |
| Runtime transport lifecycle | `LEDs/DomeOpcTransport.cs` |
| Known-good wire expectations | `Tests/LayerPipeline.TestSupport/OPCWireTests.cs`, `Tests/Fixtures/` |

An OPC address is `host:port` or `host:port:channel`. With no channel, Spectrum
appends channel `0`. Controller boxes occupy contiguous regions inside that
channel's pixel buffer.

## Files that move or preserve state

| Item | Location/owner |
| --- | --- |
| Primary configuration | `spectrum_config.xml` |
| Last saved backup | `spectrum_old_config.xml` |
| Packaged fallback | `spectrum_default_config.xml` beside the executable |
| Config path rules | `Core/SpectrumConfigurationPaths.cs` |
| Load priority and session | `Core/SpectrumConfigurationSession.cs` |
| Atomic temp/replace/backup save | `Base/ConfigurationFileStore.cs` |
| Save debounce/final save | `Base/ConfigurationPersistenceCoordinator.cs` |
| Windows window state | `spectrum_window_state.json` beside the executable |
| Windows fatal error log | `%LOCALAPPDATA%\Spectrum\Logs\spectrum-errors.log` |

Headless data-directory priority:

1. `--data-dir`
2. `SPECTRUM_DATA_DIR`
3. Windows: `%LOCALAPPDATA%\Spectrum`
4. Linux/macOS: `$XDG_CONFIG_HOME/spectrum` or `~/.config/spectrum`

Load priority is primary, backup, packaged default, then code fallback. Copy all
three evidence files before starting a suspect installation: a clean exit may
save or rotate configuration.

## “Where do I change…?”

| Change | Start here |
| --- | --- |
| Persisted property | `Core/Configuration/SpectrumConfigurationDocument.cs` |
| Mutable runtime property and notifications | `Core/Configuration/SpectrumConfiguration.cs` |
| Web metadata, validation, restart requirement | `Core/Configuration/SpectrumConfigurationSchema.cs` |
| Thread-safe runtime projection | `Base/RuntimeConfigurationSnapshots.cs` |
| Visualizer implementation | `Core/Visualizers/` |
| Visualizer key/factory | `Core/Layers/DomeLayerCatalog.cs` |
| Visualizer controls/defaults | `Core/Layers/LayerParameterSchemas.cs` |
| Control-to-options compilation | `Core/Layers/LayerRendererOptionsCompiler.cs` |
| Layer stack validation | `Base/LayerPipeline.cs` |
| Blend math | `Base/DomeBlend*.cs`, `LEDs/DomeCompositor.cs` |
| Color/alpha contract | `docs/color_semantics.md` |
| REST/SSE/WebSocket behavior | Route modules under `Web/`; lifecycle in `Web/WebServer.cs` |
| Browser UI | `Web/wwwroot/` — no Node/npm build |
| Desktop composition/platform adapters | `Spectrum/Windows/WindowsSpectrumApplication.cs` |
| Headless composition/CLI | `Host/Program.cs` |
| Windows audio/Madmom | `Audio/AudioInput.cs`, `Audio/MadmomHandler.cs` |
| Linux audio/Madmom | `Platform.Linux/AlsaAudioLevelInput.cs`, `Platform.Linux/MadmomPcmBeatTracker.cs` |
| MIDI | `MIDI/MidiInput.cs` and `Base/MIDI/`; Windows only |
| Pro DJ Link | `Core/ProDjLinkInput.cs` |
| Wands/orientation | `Core/Inputs/`, protocol types under `Core/Protocols/` |
| Build/package behavior | `build.ps1`, `.github/workflows/msbuild.yml` |
| Linux service/install | `deploy/linux/` |

For a new or changed layer, touch all four layer surfaces: implementation,
catalog, parameter schema, and options compiler. Add shared regression coverage
under `Tests/LayerPipeline.TestSupport/`.

## Runtime facts that explain surprising bugs

- Configuration collections must be replaced through their APIs; do not mutate
  backing collections from worker threads.
- One application-state dispatcher owns mutable state. Worker code consumes
  immutable snapshots.
- The desktop starts the web server but waits for the operator to start the
  engine. The headless host starts both.
- A candidate layer-plan compile failure leaves the previous valid plan active.
  Check telemetry `layerPlanError`.
- The renderer, browser simulator, and OPC sender are separate boundaries.
  Healthy operator FPS does not imply healthy OPC FPS.
- OPC reconnect is non-blocking and keeps only the newest pending frame.
- Audio-device selection and `domeOutputInSeparateThread` require an engine
  restart. Most show controls are live.
- Linux MIDI is intentionally a disabled adapter.
- Contained runtime failures generally go to telemetry and `Debug.WriteLine`;
  the Windows error log is for last-chance application/process failures.
- Release packaging needs the `Madmom` submodule, models, native extension, and
  staged Python runtime. A plain `dotnet publish` directory is incomplete for
  beat tracking.

## Fast commands

Required toolchain: .NET SDK `10.0.302` with roll-forward disabled; C# 14.

```powershell
dotnet --version
dotnet build Spectrum.sln -c Debug --no-restore

dotnet test Tests\Portability\Spectrum.Portability.Tests.csproj -c Debug --no-restore
dotnet test Tests\PortableLayerPipeline\Spectrum.PortableLayerPipeline.Tests.csproj -c Debug --no-restore
dotnet test Tests\Spectrum.LayerPipeline.Tests.csproj -c Debug --no-restore
```

Validate a private configuration without starting the engine:

```powershell
dotnet run --project Host\Spectrum.Host.csproj -c Debug --no-restore -- `
  --data-dir C:\tmp\spectrum-safe `
  --check
```

Run it on a non-show port only after installing a safe private config:

```powershell
dotnet run --project Host\Spectrum.Host.csproj -c Debug --no-restore -- `
  --data-dir C:\tmp\spectrum-safe `
  --port 18080
```

Headless exit codes: `0` success/help/clean shutdown, `1` startup/runtime
failure, `2` invalid arguments.

## Fast local probes

```powershell
Invoke-RestMethod http://127.0.0.1:8080/api/operator
Invoke-RestMethod http://127.0.0.1:8080/api/maintenance/runtime
Invoke-RestMethod http://127.0.0.1:8080/api/maintenance/audio
Invoke-RestMethod http://127.0.0.1:8080/api/maintenance/wands/serial
Invoke-RestMethod http://127.0.0.1:8080/api/maintenance/locks
Test-NetConnection 192.168.1.69 -Port 7890
```

High-value read order:

1. operator enabled/FPS,
2. `layerPlanError` and quarantined-component faults,
3. simulator frame,
4. OPC connected/FPS,
5. external controller and physical mapping.

## Before handing control back

- relevant test suite green;
- intended configuration loaded and saved;
- no runtime or layer-plan fault;
- simulator correct;
- OPC FPS nonzero if output is enabled;
- brightness and maximum brightness deliberate;
- test pattern `0` / `None`;
- intended scene loaded;
- known-good release and configuration still available.
