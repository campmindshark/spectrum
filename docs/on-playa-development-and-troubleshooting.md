# On-playa development and troubleshooting

This guide is for the person who has a working knowledge of C# and .NET but
does not already carry the Spectrum architecture in their head. It is intended
to be useful with no Internet access, under time pressure, and with real show
hardware nearby.

The [Dome User Manual](dome-user-manual.md) explains how to operate Spectrum.
This document explains how to diagnose and, when necessary, change it.
For a compact source-and-constant index, use the
[Spectrum Development Cheat Sheet](on-playa-development-cheat-sheet.md).

## Read this before touching a live system

Spectrum is a real-time lighting controller. A code change can affect a large,
bright physical installation immediately.

- Stop the engine before editing configuration, replacing binaries, changing
  wiring, or changing the OPC address.
- Set the current brightness and maximum brightness low before the first
  hardware test.
- Ensure the dome test pattern is `None` before returning control to the show.
- Tell the operator before using a test pattern. Several patterns flash large
  areas of the dome.
- Treat a working release directory and the live configuration as recovery
  assets. Copy them before changing either.
- Prefer the browser simulator or native simulator for the first test.
- Do not expose the browser controller to an untrusted network. Spectrum has no
  authentication.
- Do not make a speculative refactor during a show. Make the smallest change
  that explains and fixes the observed failure, keep a rollback, and record the
  exact diff.

One subtle safety issue deserves special emphasis: the packaged default
configuration currently enables dome output and points at
`192.168.1.69:7890`. The Windows application does not start the engine
automatically, but the headless host does. A headless host started with a new
empty data directory can therefore load the packaged default and attempt OPC
output as soon as it starts. For isolated development, make a private copy of
`Core/Configuration/spectrum_default_config.xml`, change `domeEnabled` to
`false`, and use that copy as `spectrum_config.xml` in the development data
directory before starting the host.

## If something is broken during a show

Use this order. It preserves evidence and minimizes the number of simultaneous
changes.

1. Put the system in a safe state.
   Stop the engine or disable dome output. If the physical output is unsafe,
   remove power from the LED output side according to the installation's normal
   procedure.
2. Record the current facts.
   Note the time, visible symptom, operator FPS, dome OPC FPS, runtime faults,
   audio status, selected scene, selected tempo source, and whether the
   simulator matches the physical dome.
3. Decide which boundary is failing.
   Browser, engine, renderer, input, OPC transport, mapping, controller, and
   LEDs are separate boundaries. Do not change layer code to fix a dead
   controller, or mapping to fix a black composite.
4. Try one reversible recovery.
   Restarting the engine clears quarantined runtime components. Restarting the
   process also rebuilds the web host and all long-lived services. Power-cycle
   external hardware only when the evidence points outside Spectrum.
5. If code must change, reproduce with hardware output disabled.
   Build only the affected projects, run the closest regression suite, use the
   simulator, and deploy with a preserved previous release.
6. After recovery, restore show-safe settings.
   Test pattern `None`, intended scene loaded, brightness ceiling checked, and
   no runtime fault text present.

Do not erase or overwrite a suspect configuration before copying it. Do not
clean `artifacts/` until the last known-good package has been copied elsewhere.
The root `build.ps1` script deliberately cleans `artifacts/` at the start.

## Offline readiness checklist

The best on-playa fix is prepared before departure. A source checkout by itself
is not a complete offline development environment.

### Carry these artifacts

- A clean recursive checkout at the deployed commit, including the `Madmom`
  submodule and model files.
- The exact deployed Windows portable directory or Linux release archive.
- A second known-good release from before the latest change.
- A copy of the live `spectrum_config.xml` and
  `spectrum_old_config.xml`.
- The `.NET 10.0.302` SDK installer. `global.json` disables roll-forward, so a
  different .NET 10 feature band is not accepted.
- On Windows, the Visual Studio installer/layout containing:
  - .NET desktop development,
  - Desktop development with C++,
  - the x64 MSVC compiler and Windows SDK.
- Git for Windows, PowerShell, and `uv 0.11.26`.
- On Linux, the self-contained Spectrum archive plus the target system's
  packages or offline package media for:
  - `libasound.so.2`,
  - `libportaudio.so.2`,
  - `libstdc++.so.6` with `GLIBCXX_3.4.22` or newer,
  - `curl`, `jq`, `ss`, `awk`, and `grep` for qualification,
  - optionally `arecord`, `tcpdump`, and `ffmpeg` for deeper diagnosis.
- A text editor, a diff tool, and a way to move files between the development
  machine and the show host.

### Warm every dependency cache

Before leaving Internet access:

```powershell
dotnet --version
dotnet restore Spectrum.sln
.\build.ps1
```

The expected SDK version is `10.0.302`. The full build proves that NuGet,
CPython 3.11.15, the pinned Python packages, MSVC, Madmom models, and packaging
inputs are present. It also creates a self-contained Windows release under
`artifacts/Spectrum`.

Run the exact commands that will be used offline with the network disconnected
or disabled. A cached package is useful only if the chosen command can actually
resolve it. In particular, the Madmom scripts recreate environments and may
ask `uv` for packages even when an older environment already exists. Prefer a
known-good packaged Madmom runtime to rebuilding the Python component on site.

For Linux, build and qualify the release on a machine compatible with the
target before departure:

```shell
dotnet publish Host/Spectrum.Host.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -o artifacts/Spectrum-linux-x64

bash deploy/linux/qualify-runtime.sh \
  "$PWD/artifacts/Spectrum-linux-x64"
```

The published Linux directory also needs the staged `Madmom/runtime` produced
by `Madmom/scripts/build.sh`; the CI workflow in
`.github/workflows/msbuild.yml` is the exact packaging reference.

### Record the deployed identity

Save this with the release:

```powershell
git rev-parse HEAD
git status --short
dotnet --version
uv --version
git -C Madmom rev-parse HEAD
```

The expected Madmom commit is also stored as the gitlink in the parent
repository. Compare it with:

```powershell
git ls-tree HEAD Madmom
```

If the two hashes differ, the checkout is not the source tree represented by
the parent commit.

## Runtime choices

Spectrum has two process frontends that share the same portable engine.

| Frontend | Entry point | Platform inputs | UI | Startup behavior |
| --- | --- | --- | --- | --- |
| Windows desktop | `Spectrum/Spectrum.csproj` | WASAPI audio, Windows MIDI, Pro DJ Link UDP, orientation UDP/serial | WPF plus browser | Web host starts with the window; engine waits for the operator |
| Headless host | `Host/Spectrum.Host.csproj` | ALSA audio and Linux Madmom, disabled MIDI, Pro DJ Link UDP, orientation UDP/serial | Browser only | Web host and engine start automatically |

The browser controller, layer engine, compositor, OPC output, web simulator,
configuration model, Pro DJ Link listener, and orientation protocol are shared.
The native simulator and VJ HUD are Windows-only. Linux MIDI is deliberately a
disabled adapter.

On Windows, use the desktop process when diagnosing WASAPI, MIDI, WPF, or native
windows. Use the headless host when diagnosing portable engine or browser code
without those native adapters. On Linux, use the headless host.

## Repository map

The project boundaries are meaningful. Place a change in the narrowest project
that owns the behavior.

| Directory/project | Owns | Does not own |
| --- | --- | --- |
| `Base` | Contracts, immutable snapshots, frame and topology types, layer-stack validation, blend definitions, persistence foundation, dispatcher contracts | Platform APIs, concrete renderers, web server |
| `Core` / `Spectrum.Core` | `SpectrumConfiguration`, process lifecycle, `Operator`, layer metadata and renderer factories, visualizers, orientation and Pro DJ Link inputs | WPF, WASAPI, native MIDI, ASP.NET routing |
| `LEDs` | Dome topology, composition, output mapping, simulator publication, OPC transport | Renderer catalog, UI, audio capture |
| `Web` / `Spectrum.Web` | Kestrel, REST/SSE/WebSocket APIs, advisory locks, browser assets and controllers | Platform input implementation |
| `Audio` | Windows WASAPI capture and Windows Madmom child management | Linux audio |
| `MIDI` | Windows MIDI device and binding implementation | Linux MIDI |
| `Platform.Linux` | ALSA discovery/capture and PCM-fed Linux Madmom child | Portable engine and browser UI |
| `Spectrum` | WPF composition root, desktop controls, native simulator/HUD/setup windows | Headless service |
| `Host` | Console composition root, CLI parsing, signal handling | Render behavior |
| `Tests` | Portable, portable layer-pipeline, and Windows regression suites | Production source |
| `deploy/linux` | systemd unit, install/operation notes, runtime and service qualification | Application implementation |
| `Madmom` | Python beat-tracking submodule and native extension build | .NET process orchestration |

Production projects compile source from their own directories. CI enforces that
with `.github/scripts/verify-source-ownership.ps1`. If shared behavior is
needed, move the abstraction to an actual shared project rather than linking a
source file from a sibling tree.

### Project dependency shape

The portable dependency direction is approximately:

```text
Base
  ├── LEDs
  ├── Platform.Linux
  ├── Audio (Windows)
  └── MIDI (Windows)

Base + LEDs
  └── Spectrum.Core

Base + LEDs + Spectrum.Core
  └── Spectrum.Web

Spectrum.Core + Spectrum.Web + Platform.Linux
  └── Spectrum.Host

Spectrum.Core + Spectrum.Web + LEDs + Audio + MIDI
  └── Spectrum desktop
```

Keep Windows-only references out of `Base`, `LEDs`, `Core`, `Web`, `Host`, and
`Platform.Linux`. A portability failure often means a native adapter leaked
across this boundary.

## How the process is assembled

### Windows

`Spectrum/Windows/MainWindow.xaml.cs` creates
`WindowsSpectrumApplication`. That class:

1. selects configuration paths beside the executable,
2. creates the WPF-backed application-state dispatcher,
3. loads `SpectrumConfiguration`,
4. constructs `Operator` with `WindowsSpectrumInputFactory`,
5. constructs `SpectrumWebHost` on port 8080, and
6. starts the web service.

The operator remains disabled until the user starts it. Closing the main window
stops the operator, disposes UI controllers, stops the web service, disposes the
runtime, and performs the final configuration save.

### Headless

`Host/Program.cs`:

1. parses `--data-dir`, `--port`, `--check`, and `--help`,
2. resolves a writable configuration directory,
3. creates a dedicated application-state thread,
4. chooses `LinuxSpectrumInputFactory` on Linux or disabled inputs elsewhere,
5. constructs the same `Operator` and `SpectrumWebHost`,
6. starts Kestrel,
7. enables the operator automatically, and
8. waits for `Ctrl+C` or `SIGTERM`.

`--check` loads configuration and validates composition without starting the
web service or engine. It is the safest first check of a new binary or suspect
configuration:

```shell
./Spectrum.Host --check --data-dir /path/to/copied-data
```

Exit codes are:

- `0`: help, successful check, or clean shutdown,
- `1`: startup/runtime failure, including a failed web listener,
- `2`: invalid command-line arguments.

## State ownership and threading

Most difficult Spectrum bugs are ownership bugs rather than algorithm bugs.

### One writer for persisted state

`SpectrumConfiguration` is mutable and raises `PropertyChanged`, but production
code treats one dispatcher thread as its owner:

- WPF uses `DispatcherApplicationStateDispatcher`.
- The headless host uses
  `DedicatedThreadApplicationStateDispatcher`.
- Web requests and MIDI callbacks send commands through that dispatcher.
- `SpectrumConfiguration` has a final guard that posts an off-thread scalar
  setter back to the owner thread.

Do not mutate `Configuration` collections in place. Use the
`ConfigurationEditor` replacement methods. They deep-copy the document-shaped
objects, compile immutable views, publish the new generation, and then notify
observers.

For a compound update, perform the whole change as one owner-thread operation.
Scene recall uses `IDomeShowStateConfiguration.ApplyDomeShowState` so layers,
palettes, and global effects become visible as one generation.

### Immutable runtime snapshots

The operator thread never walks the mutable serializer graph on every frame.
`SpectrumConfiguration` publishes immutable subsystem snapshots:

- `DomeRuntimeFrameSnapshot`
- `DomeShowStateSnapshot`
- `AudioSettingsSnapshot`
- `MidiSettingsSnapshot`
- `OrientationSettingsSnapshot`
- `DomeOutputSettingsSnapshot`
- `BeatSettingsSnapshot`
- `SceneRetentionSnapshot`

A configuration setter must publish the snapshot consumed by the affected
runtime. If a new setting appears in XML and the UI but does not affect the
engine, this publication step is the first place to inspect.

### The operator loop

`Core/Runtime/Operator.cs` owns the engine thread. Its frame order is:

```text
apply pending layer/show generation
capture one runtime generation for the frame
build the active output/visualizer/input schedule
activate or deactivate devices
update active inputs
capture one orientation generation
run active visualizers
update active outputs
publish operator FPS
```

The loop is capped at 400 Hz. The OPC wire is independently capped at 200 Hz.
The browser simulator is sampled at up to 60 FPS.

Diagnostics such as test patterns have higher scheduling priority than normal
layers. A non-`None` test pattern can make a perfectly healthy layer stack
appear missing.

### Failure containment and quarantine

The engine catches failures at component boundaries:

- a visualizer is quarantined after 10 consecutive `Visualize` failures,
- an input is quarantined after 3 consecutive `OperatorUpdate` failures,
- an output is quarantined after 3 consecutive `OperatorUpdate` failures,
- an activation failure is quarantined immediately for that engine run,
- a successful update clears a transient consecutive-failure streak.

Quarantined components remain isolated until the engine is restarted. Stopping
and starting the engine calls `ResetComponentFailures`; merely waiting does not
clear a quarantine.

The resulting messages are published as:

- `visualizerFault`
- `inputFault`
- `outputFault`

A failed layer-generation construction is different. The operator rejects the
candidate, keeps the previous working render plan, and publishes
`layerPlanError`. This is why a bad layer change can appear not to take effect
while the previous look continues to render.

### Shutdown order

`SpectrumHost` stops:

1. the browser service,
2. the runtime,
3. configuration persistence.

The final persistence step attempts a save even if no debounce is pending.
Linux `SIGTERM` and Windows window close both follow an ordered shutdown. Prefer
those paths to killing the process. `SIGKILL`, Task Manager's end-process
operation, or power loss can discard the most recent debounced changes.

## Frame and output flow

The normal data path is:

```text
input state
  ↓
layer renderer's persistent DomeFrame
  ↓
compiled render plan + compositor
  ↓
completed logical DomeFrame
  ├── native simulator mailbox
  ├── browser simulator mailbox
  └── logical-to-installed wiring map
        ↓
      OPC dense RGB frame
        ↓
      TCP controller
        ↓
      control boxes / strips / LEDs
```

This gives several useful diagnostic separations:

- Browser controls failing while a current look continues means the web
  boundary may be broken while the engine is healthy.
- A correct simulator with wrong physical pixels means composition is healthy;
  investigate mapping, OPC addressing, controller configuration, or wiring.
- Both simulators and hardware showing the same wrong effect points toward
  layers, palettes, blend operations, tempo/input data, or test-pattern state.
- A live engine with zero OPC FPS can still be rendering correctly; OPC FPS
  counts frames actually sent, not frames produced.

### Layer model

Each configured layer has:

- a stable instance ID,
- a stable renderer ID,
- a blend-operation ID,
- opacity and enabled state,
- renderer parameter overrides,
- operation parameter overrides,
- optional operator notes.

Instance IDs identify occurrences, not renderer kinds. Two `twinkle` layers
must have different instance IDs. The ID retains renderer trails and manual
fire/clear counters across plan updates and scene recall.

The stack is stored bottom-to-top: index 0 is the background and the last entry
is frontmost. The browser displays the frontmost row first, so its visual order
is the reverse of the stored compositing order.

Renderer state belongs to `LayerRendererStore`, keyed by instance ID. A
renderer change for the same instance replaces the renderer. Scene definitions
retain instance IDs so live renderer state can survive scene recall when the
same layer occurrence returns.

### Color and blend rules

Before changing composition, read [Dome frame color semantics](color_semantics.md).
The important non-obvious rules are:

- channels are display-encoded bytes, not linear-light values,
- RGB is unassociated from the separate alpha/mask channel,
- `Over` uses source alpha; Add, Screen, Lighten, and Multiply do not,
- adjustment operations use alpha as a mask,
- the composite begins as transparent black,
- Multiply or an adjustment as the first meaningful layer may intentionally
  produce black,
- global hue rotation advances persistent renderer pixels after the current
  completed frame is published.

A compositor change is a visual contract change. Update explicit regression
fixtures rather than silently accepting different pixels.

### OPC behavior

`LEDDomeOutput` maps logical pixels to installed controller addresses.
`OPCAPI` then:

- maintains dense double-buffered RGB data per OPC channel,
- connects asynchronously,
- retries every 250 ms after failure,
- treats a connect attempt as timed out after 2 seconds,
- sends the newest pending frame after reconnect,
- handles partial socket writes,
- disables Nagle's algorithm,
- caps output at 200 Hz.

A missing controller should not stall the operator. Zero OPC FPS with healthy
operator FPS is therefore an output-path symptom, not proof that the engine is
stuck.

`domeOutputInSeparateThread` chooses whether the OPC update happens on its own
thread. Changing it restarts the engine. It is an advanced diagnostic, not a
normal show control.

## Configuration and recovery

### File locations

Windows portable desktop stores these beside the executable:

- `spectrum_config.xml`
- `spectrum_old_config.xml`
- `spectrum_default_config.xml`
- `spectrum_window_state.json` for native window placement

The headless host selects its data directory in this order:

1. `--data-dir`,
2. `SPECTRUM_DATA_DIR`,
3. `XDG_CONFIG_HOME/spectrum`,
4. `~/.config/spectrum` on Unix,
5. local application data under `Spectrum` on Windows.

The packaged default remains beside the executable.

The production systemd unit explicitly uses `/var/lib/spectrum`.

### Load and save semantics

Load order is:

1. primary configuration,
2. recovery backup,
3. packaged default,
4. a code-created empty configuration.

Failures in one candidate do not prevent trying the next. The headless host
writes each failed path and parse error to stderr/journald. The Windows host
reports load failures to the debugger output, while last-chance application and
save failures are appended to:

```text
%LOCALAPPDATA%\Spectrum\Logs\spectrum-errors.log
```

Saves are debounced by 100 ms. Serialization happens on the application-state
owner thread. The file store writes `spectrum_config.xml.tmp`, flushes it, then
atomically replaces the primary while rotating the former primary to
`spectrum_old_config.xml`. A graceful shutdown performs a final save.

If the process reports that it loaded the backup, copy the primary, backup, and
current process output before exiting. A later save rotates files again. The
live in-memory configuration is the recovered state, but the disk evidence can
change during graceful shutdown.

### Safe manual configuration editing

Prefer the UI or API. They validate values, publish the correct snapshot, and
save atomically.

If XML must be edited:

1. stop the engine,
2. stop the process cleanly,
3. copy both primary and backup to timestamped files,
4. edit a copy,
5. validate it with `Spectrum.Host --check --data-dir <copy-directory>`,
6. start with physical output disconnected or disabled,
7. inspect `/api/maintenance/runtime`, and
8. only then enable hardware output.

Do not add a collection item without a stable layer instance ID. Do not invent
blend or renderer IDs; use the catalog values exposed by `/api/layers` or the
source catalogs.

## Building without Internet

### Verify the toolchain

```powershell
dotnet --version
dotnet --list-sdks
git --version
uv --version
```

The repository requires exactly .NET SDK `10.0.302`. C# language version 14,
nullable reference types, analyzers, and warnings-as-errors are enabled.

The repository formatting contract is:

- LF line endings,
- spaces,
- 2-space indentation,
- same-line opening braces.

### Fast Windows build

After dependencies have already been restored:

```powershell
dotnet build Spectrum.sln -c Debug --no-restore
```

This builds production and test projects in the current solution. Use Debug
while diagnosing because many contained component errors are written with
`Debug.WriteLine`; those calls are not a durable Release log.

For a narrower change:

```powershell
dotnet build Host\Spectrum.Host.csproj -c Debug --no-restore
dotnet build Spectrum\Spectrum.csproj -c Debug --no-restore
```

### Fast Linux build

Do not build the Windows solution on Linux. Build the portable host and suites:

```shell
dotnet build Host/Spectrum.Host.csproj -c Debug --no-restore
dotnet build \
  Tests/Portability/Spectrum.Portability.Tests.csproj \
  -c Debug --no-restore
dotnet build \
  Tests/PortableLayerPipeline/Spectrum.PortableLayerPipeline.Tests.csproj \
  -c Debug --no-restore
```

### Full Windows build

```powershell
.\build.ps1
```

The script:

1. initializes submodules,
2. deletes and recreates `artifacts/`,
3. builds the solution,
4. runs portable and Windows tests,
5. publishes a self-contained Windows application,
6. rebuilds and tests Madmom,
7. stages a portable CPython runtime, and
8. creates `artifacts/Spectrum-win-x64.zip`.

Useful switches:

```powershell
.\build.ps1 -SkipPortable
.\build.ps1 -SkipTests -SkipPortable
.\build.ps1 -SkipSubmodules
```

`-SkipPortable` still builds the Python component. For a C#-only on-site edit,
targeted `dotnet build` is faster and does not delete the known-good artifacts
directory.

### Madmom-only build

Windows:

```powershell
.\Madmom\scripts\build.ps1
```

Linux:

```shell
bash Madmom/scripts/build.sh \
  --environment-directory artifacts/madmom-linux-build-env \
  --wheel-directory artifacts/wheels/linux-x64 \
  --portable-runtime-directory artifacts/madmom-linux-runtime
```

These scripts clear and recreate environments and native build output. Do not
run them casually when the packaged tracker is already working and the issue is
in C#.

## Running safely from source

### Validate a headless composition

Windows PowerShell:

```powershell
$devData = Join-Path $env:TEMP "spectrum-dev-data"
New-Item -ItemType Directory -Force $devData | Out-Null
Copy-Item `
  Core\Configuration\spectrum_default_config.xml `
  (Join-Path $devData "spectrum_config.xml")
```

Edit the copied file and set:

```xml
<domeEnabled>false</domeEnabled>
<domeBrightness>0.05</domeBrightness>
<domeMaxBrightness>0.1</domeMaxBrightness>
```

Then:

```powershell
dotnet run `
  --project Host\Spectrum.Host.csproj `
  -c Debug `
  --no-restore `
  -- `
  --check `
  --data-dir $devData
```

Linux:

```shell
dev_data=$(mktemp -d)
cp Core/Configuration/spectrum_default_config.xml \
  "$dev_data/spectrum_config.xml"
# Edit the copied file and disable dome output before continuing.

dotnet run \
  --project Host/Spectrum.Host.csproj \
  -c Debug \
  --no-restore \
  -- \
  --check \
  --data-dir "$dev_data"
```

When `--check` succeeds, run on an unused port:

```powershell
dotnet run `
  --project Host\Spectrum.Host.csproj `
  -c Debug `
  --no-restore `
  -- `
  --port 18080 `
  --data-dir $devData
```

Use `http://127.0.0.1:18080`. The host enables the engine automatically, so
verify again that this private configuration has hardware output disabled.

### Run the Windows desktop

```powershell
dotnet run `
  --project Spectrum\Spectrum.csproj `
  -c Debug `
  --no-restore
```

The development output directory receives the packaged default configuration.
The engine is initially stopped. Before pressing Start, select the development
audio device, disable dome output, and lower brightness.

Visual Studio is useful for WPF and `Debug.WriteLine` output, but a debugger can
change timing. Reproduce frame-rate problems once without the debugger.

## Tests

Run the smallest relevant suite first, then the broader suites.

Portable platform, host, persistence, HTTP, Linux adapter, and OPC checks:

```powershell
dotnet test `
  Tests\Portability\Spectrum.Portability.Tests.csproj `
  -c Debug `
  --no-restore
```

Portable layer/compositor/orchestration checks:

```powershell
dotnet test `
  Tests\PortableLayerPipeline\Spectrum.PortableLayerPipeline.Tests.csproj `
  -c Debug `
  --no-restore
```

Windows WPF/MIDI integration plus the shared layer checks:

```powershell
dotnet test `
  Tests\Spectrum.LayerPipeline.Tests.csproj `
  -c Debug `
  --no-restore
```

The portable and Windows layer suites intentionally reuse the test-support
registrations. If a behavioral fix is portable, add it under
`Tests/LayerPipeline.TestSupport` so both environments exercise it. Put
platform-specific orchestration checks in the corresponding runner.

Useful test-runner diagnostics:

```powershell
dotnet test Tests\PortableLayerPipeline\Spectrum.PortableLayerPipeline.Tests.csproj -c Debug --no-restore --list-tests
dotnet test Tests\PortableLayerPipeline\Spectrum.PortableLayerPipeline.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~LayerPipeline"
```

If `--no-restore` cannot resolve `MSTest.Sdk/4.1.0`, the offline NuGet cache is
not actually sufficient for that command or its NuGet configuration is
unreadable. A prior successful compile of the test DLL does not prove that the
test host SDK can be resolved again.

For exact OPC baseline auditing:

```powershell
Tests\Fixtures\Get-KnownGoodOpcBaseline.ps1
```

See `Tests/Fixtures/README.md` before interpreting its hashes.

## Browser and HTTP diagnostic cookbook

The API is often the fastest source of truth because it works on both
frontends and exposes state without opening a native window.

Assume:

```powershell
$spectrum = "http://127.0.0.1:8080"
```

### Read basic state

```powershell
Invoke-RestMethod "$spectrum/api/operator"
Invoke-RestMethod "$spectrum/api/maintenance/runtime"
Invoke-RestMethod "$spectrum/api/maintenance/audio"
Invoke-RestMethod "$spectrum/api/layers"
Invoke-RestMethod "$spectrum/api/palettes"
Invoke-RestMethod "$spectrum/api/scenes"
Invoke-RestMethod "$spectrum/api/maintenance/wands/serial"
Invoke-RestMethod "$spectrum/api/maintenance/locks"
```

Bash equivalents:

```shell
curl -sS http://127.0.0.1:8080/api/operator | jq
curl -sS http://127.0.0.1:8080/api/maintenance/runtime | jq
curl -sS http://127.0.0.1:8080/api/maintenance/audio | jq
```

`/api/maintenance/runtime` is the compact health endpoint:

```json
{
  "enabled": true,
  "operatorFps": 0,
  "domeOpcFps": 0,
  "layerPlanError": null,
  "visualizerFault": null,
  "inputFault": null,
  "outputFault": null
}
```

The first FPS telemetry window takes about a second. Do not diagnose a zero
read immediately after startup.

### Stop and start the engine

```powershell
Invoke-RestMethod `
  "$spectrum/api/operator" `
  -Method Put `
  -ContentType "application/json" `
  -Body '{"enabled":false}'

Invoke-RestMethod `
  "$spectrum/api/operator" `
  -Method Put `
  -ContentType "application/json" `
  -Body '{"enabled":true}'
```

This engine restart clears component quarantines. It does not restart Kestrel
or reload the configuration file from disk.

### Make output safe through the API

```powershell
Invoke-RestMethod `
  "$spectrum/api/maintenance/parameters/domeEnabled" `
  -Method Put `
  -ContentType "application/json" `
  -Body '{"value":false}'

Invoke-RestMethod `
  "$spectrum/api/maintenance/parameters/domeTestPattern" `
  -Method Put `
  -ContentType "application/json" `
  -Body '{"value":0}'

Invoke-RestMethod `
  "$spectrum/api/maintenance/parameters/domeMaxBrightness" `
  -Method Put `
  -ContentType "application/json" `
  -Body '{"value":0.1}'

Invoke-RestMethod `
  "$spectrum/api/maintenance/parameters/domeBrightness" `
  -Method Put `
  -ContentType "application/json" `
  -Body '{"value":0.05}'
```

### Observe the event stream

```shell
curl -N http://127.0.0.1:8080/api/maintenance/events
```

The stream begins with telemetry, operator state, atomic show state, and scene
state, then publishes changes. If REST reads work but the browser does not
update live, compare this stream with the browser's developer console. A proxy
that buffers server-sent events can make controls appear stale even though
ordinary HTTP works.

### Parameter protocol

List the schema rather than guessing a key or range:

```powershell
Invoke-RestMethod "$spectrum/api/parameters"
Invoke-RestMethod "$spectrum/api/maintenance/parameters"
```

Writes use:

```json
{"value": ...}
```

User-level controls use `/api/parameters/{key}`. Maintenance controls use
`/api/maintenance/parameters/{key}`. A wrong role returns 403 or 404 depending
on the key.

### Advisory locks

Dome calibration explicitly requires the `domeCalibration` lock. Test-pattern
writes participate in the `domeTest` lock. The default lease is 15 seconds and
the UI renews it by heartbeat.

A stale browser normally releases itself when its lease expires. The web
server checks calibration every 3 seconds and cancels an active calibration
whose lease is gone. Before forcing anything, inspect:

```powershell
Invoke-RestMethod "$spectrum/api/maintenance/locks"
```

## Troubleshooting by symptom

### The process will not start

Check, in order:

1. Is the exact .NET SDK present for a framework-dependent source run?
2. Does `Spectrum.Host --check --data-dir <copied-data>` pass?
3. Is the configuration directory writable by the process account?
4. Does the packaged default exist beside the host?
5. Is port 8080 already in use?
6. On Linux, are required shared libraries present?
7. Does stderr or the systemd journal show configuration, loader, or Kestrel
   failure?

Windows:

```powershell
Get-NetTCPConnection -LocalPort 8080 -ErrorAction SilentlyContinue
Get-Content `
  (Join-Path $env:LOCALAPPDATA "Spectrum\Logs\spectrum-errors.log") `
  -Tail 100 `
  -ErrorAction SilentlyContinue
```

Linux:

```shell
ss -ltnp 'sport = :8080'
systemctl status spectrum.service --no-pager
journalctl -u spectrum.service --no-pager -n 100
ldd /opt/spectrum/Spectrum.Host | grep 'not found'
```

The Windows host treats a web-listener start failure as non-fatal, so the WPF
engine can remain usable even when the browser controller failed. The headless
host has no alternative UI and exits with code 1 when Kestrel cannot start.

### Browser says Connecting or cannot load

Separate local service health from network reachability:

1. On the Spectrum machine, request
   `http://127.0.0.1:8080/api/operator`.
2. If local fails, inspect process/service and listener state.
3. If local works, request the machine's show-network address from another
   device.
4. Check host firewall, client isolation, VLAN/subnet routing, captive portals,
   and whether the client joined the correct access point.
5. Confirm no second Spectrum instance owns the port.
6. If the page loads but remains stale, inspect the SSE endpoint.

Windows network checks:

```powershell
Test-NetConnection 127.0.0.1 -Port 8080
Get-NetIPAddress -AddressFamily IPv4
```

Linux:

```shell
curl -v http://127.0.0.1:8080/api/operator
ip -brief address
ss -ltnp 'sport = :8080'
```

Kestrel listens on `0.0.0.0`, not only localhost.

### The engine is stopped or operator FPS remains zero

- Read `/api/operator` and `/api/maintenance/runtime`.
- Start the engine and wait longer than one telemetry window.
- If it immediately stops, inspect process output for an uncontained top-level
  failure.
- If enabled is true but FPS stays zero, attach a debugger or capture stacks;
  the operator thread may be blocked in a component lifecycle transition.
- Check whether shutdown is already in progress.

Ordinary missing OPC hardware should not block the operator because connection
is asynchronous. ALSA capture runs on its own worker. A persistently zero
operator FPS therefore deserves investigation beyond “the controller is off.”

### Operator FPS is low or unstable

First reduce variables:

1. Close browser and native simulators.
2. Set the test pattern to `None`.
3. Load a minimal known-good layer stack.
4. Disable physical dome output temporarily.
5. Compare Debug and Release without a debugger.
6. Watch CPU, memory, and thermal throttling.
7. Reintroduce layers one at a time.

Look for:

- a visualizer doing topology construction or allocation per pixel/per frame,
- locks taken inside pixel loops,
- `DateTime.Now`, reflection, LINQ, dictionary construction, or array cloning
  on the hot path,
- synchronous file/network/device operations in `Visualize`,
  `OperatorUpdate`, or `Flush`,
- a simulator client forcing frame packing,
- a layer whose manual Clear action can drop expensive accumulated state,
- an unexpectedly high number of enabled layers.

The operator's scheduling collections and accepted snapshots are intentionally
reused. Preserve that allocation-free direction. Use `FrameClock` for
frame-rate-independent animation rather than assuming a fixed number of engine
frames per second.

The Linux `qualify-runtime.sh` script samples operator FPS and resident memory
under concurrent HTTP reads. Its default accepted operator range is 30–425 FPS;
that is a broad health envelope, not a promise that every hardware/load
combination should run at the same rate.

### Simulator is black

Check:

- engine enabled,
- test pattern and intended layers,
- at least one enabled layer with available required inputs,
- `layerPlanError`,
- `visualizerFault`,
- browser preview not paused,
- WebSocket connection to `/api/dome-simulator/frames`,
- `webDomeSimulatorEnabled` in the configuration used at startup.

The web simulator endpoints are not mapped at all when
`webDomeSimulatorEnabled` is false. Changing that value is persisted but the
existing `SpectrumWebHost` is not reconstructed by an engine restart; restart
the process to change endpoint availability.

An empty or wholly unavailable render plan returns no new frame, preserving the
previous hold behavior. Black may therefore be an old held frame rather than a
new black composite.

### Simulator works but the physical dome is dark

The renderer and compositor are probably healthy. Check:

1. `domeEnabled` is true.
2. OPC address uses `host:port` or `host:port:channel`.
3. `domeOpcFps` becomes nonzero.
4. The controller host answers on the configured TCP port.
5. Controller, show switch, and Ethernet link are powered.
6. The controller expects OPC channel 0 or the explicitly configured channel.
7. Brightness and maximum brightness are above zero.
8. External controller/output power and fusing are healthy.

Windows:

```powershell
Test-NetConnection 192.168.1.69 -Port 7890
```

Linux:

```shell
timeout 3 bash -c '</dev/tcp/192.168.1.69/7890'
```

A successful TCP connect proves only that something is listening, not that it
is the correct OPC controller or that downstream LEDs have power.

When a controller comes online late, Spectrum should reconnect and send the
newest pending frame without an engine restart.

### Physical pixels are wrong but the simulator is right

Investigate the installed-wiring projection:

- `domeCableMapping` maps 10 controller cable halves to physical dome
  endpoints.
- `domePortMappings` contains one 8-port permutation per dome-side box.
- An empty cable mapping uses identity behavior.
- Missing or invalid port mappings fall back to identity per box.
- The guided calibration writes these structures.

Do not “correct” the hard-coded logical topology for an installation-specific
cable swap. That is what the mapping is for.

Use a diagnostic pattern only after lowering brightness and warning the crew.
When done, return the pattern to `None`.

If an OPC serialization regression is suspected, run the known-good baseline
audit and the `OPCWireTests`. The historical fixture covers 7,580 logical LEDs
and dense zero-filled strip addresses.

### The live look is wrong on both simulator and hardware

Check the high-level state before code:

- intended scene actually loaded,
- layer order and enabled flags,
- blend mode and opacity,
- named palette contents and palette index,
- global fade and hue speed,
- test pattern `None`,
- current tempo source and BPM,
- audio signal,
- spotlight wand selection,
- manual Fire/Clear state.

Remember that a scene stores the layer stack and global fade/hue speeds. It
refers to named palettes; it does not embed and restore independent copies of
those palette colors.

If a layer edit was rejected, `layerPlanError` explains why and the previous
working plan remains active.

### Configuration changes do not persist

Check:

- process account can create and replace files in the data directory,
- filesystem is not read-only or full,
- no external sync/antivirus tool is locking the file,
- the process was allowed a graceful shutdown,
- the expected data directory is actually in use,
- the Windows error log or Linux journal contains a save failure.

The headless startup banner prints the resolved primary configuration path.
Do not assume it is `~/.config/spectrum` when systemd passes
`--data-dir /var/lib/spectrum`.

On Windows, the portable desktop stores configuration beside the executable.
Running from Visual Studio, a build output directory, and a packaged release
can therefore produce three independent configurations.

### Configuration appears reset

Determine which source loaded:

```shell
./Spectrum.Host --check --data-dir /path/to/the/suspect/directory
```

The success message names the selected source. Parse failures are printed
before it.

Possible causes:

- process used a different data directory,
- primary XML was invalid and backup/default loaded,
- a new portable directory did not include the old live configuration,
- the service account could not read the expected file,
- a development build copied the packaged default to a different output tree.

Do not keep restarting until the primary and backup have been copied. Each
graceful exit attempts a save and can rotate the on-disk files.

### No audio devices or no level

Read:

```powershell
Invoke-RestMethod "$spectrum/api/maintenance/audio"
```

It reports backend, selected stable ID, active state, peak volume, last error,
and available devices.

Windows/WASAPI:

- the selected endpoint ID must still appear among active capture endpoints,
- device identity can change after driver reinstall or interface changes,
- Spectrum captures at the endpoint's mix sample rate,
- selecting a new audio device is a restart-triggering configuration change,
- missing selection or missing endpoint causes activation failure and input
  quarantine until the engine restarts.

Linux/ALSA:

- Spectrum asks ALSA for stable PCM names,
- it tries stereo S16_LE, 44.1 kHz first, then mono,
- reads 1,024 frames at a target 50 ms latency,
- the worker retries every second after open/read failure,
- the `spectrum` user needs permission to open the selected PCM,
- session-scoped PipeWire devices may not be visible to a system service.

Useful Linux checks:

```shell
cat /proc/asound/cards
arecord -L
id spectrum
sudo -u spectrum arecord -L
```

If `arecord` can enumerate as your login user but not as `spectrum`, fix
service-account access rather than changing C#.

On distributions using the `audio` group:

```shell
sudo usermod -aG audio spectrum
sudo systemctl restart spectrum.service
```

Group changes do not affect an already-running process.

### BPM is wrong or frozen

`beatInput` values are:

- `0`: Human,
- `1`: Madmom,
- `2`: Pro DJ Link.

Read the current `bpm` telemetry from the maintenance event stream or UI.

Human:

- use repeated taps,
- the API accepts a positive BPM at
  `POST /api/maintenance/tempo/tap`,
- confirm the source is Human.

Madmom on Windows:

- the runtime locator searches upward for `Madmom/runtime`,
  `Madmom/.build-env`, then legacy `Madmom/env`,
- packaged Windows Python is `Madmom/runtime/python.exe`,
- the tracker is `Madmom/runtime/Scripts/DBNBeatTracker`,
- the child receives a PortAudio device index derived from the selected WASAPI
  endpoint,
- unexpected exit schedules a restart after 2 seconds,
- tracker messages are parsed only when they begin with `BEAT:`.

Madmom on Linux:

- ALSA remains the only hardware owner,
- Spectrum downmixes captured interleaved PCM to mono and writes it to the
  child's stdin,
- the runtime is `Madmom/runtime/bin/python`,
- the tracker is `Madmom/runtime/bin/DBNBeatTracker`,
- failures are exposed through the audio endpoint and retried after 2 seconds.

Validate packaged imports without opening audio:

```shell
/opt/spectrum/Madmom/runtime/bin/python -B -c \
  'import madmom,numpy,scipy,pyaudio; print(madmom.__version__)'
test -x /opt/spectrum/Madmom/runtime/bin/DBNBeatTracker
```

On Windows:

```powershell
.\Madmom\runtime\python.exe -B -c `
  "import madmom,numpy,scipy,pyaudio; print(madmom.__version__)"
Test-Path .\Madmom\runtime\Scripts\DBNBeatTracker
```

An import failure points to packaging or native-library loading. A successful
import with no beats points later in the chain: capture signal, PCM streaming,
model execution, child lifetime, or parsing.

Pro DJ Link:

- Spectrum passively listens on UDP 50001,
- it does not announce a virtual CDJ,
- only beat packets with a valid header and plausible 20–500 BPM are used,
- mixer device 33 is preferred,
- another player is followed if the current source is stale for 2 seconds,
- bind/receive failure retries after 1 second.

Linux packet check:

```shell
ss -lunp | grep ':50001'
sudo tcpdump -ni any udp port 50001
```

If packets reach the interface but not Spectrum, inspect bind ownership and
firewall. If no packets reach the interface, investigate the DJ network before
the parser.

### Wands are missing or unstable

There are two additive transport paths:

- unauthenticated UDP on port 5005,
- USB serial through the configured receiver port.

The same validated datagram parser and device map consume both. A device should
normally use one transport.

Serial:

- Windows uses COM names.
- Linux prefers `/dev/serial/by-id/...`, then
  `/dev/serial/by-path/...`, then transient `/dev/ttyACM0`-style names.
- The port opens at nominal 115200 baud.
- DTR and RTS are enabled; DTR is essential for common ESP32 native USB CDC
  receivers.
- reads time out after 200 ms,
- open/read failures retry after 1 second,
- COBS or CRC failures drop only the frame and do not become a port error.

Read:

```powershell
Invoke-RestMethod "$spectrum/api/maintenance/wands/serial"
Invoke-RestMethod "$spectrum/api/maintenance/wands"
```

Interpret serial status:

- `No port selected`: configuration is empty.
- `Opening…`: no successful open yet.
- `Error: ...`: OS open/read error.
- `Port open — no data`: open succeeded but neither heartbeat nor frame arrived.
- `Receiver connected`: a heartbeat or data frame arrived within 1.5 seconds.

“Port open — no data” most often means receiver firmware/power/cable, the wrong
serial device, or failure to assert DTR in a replacement implementation.

On Linux, confirm the service user owns the device group:

```shell
ls -l /dev/serial/by-id /dev/serial/by-path 2>/dev/null
id spectrum
```

Commonly:

```shell
sudo usermod -aG dialout spectrum
sudo systemctl restart spectrum.service
```

UDP:

```shell
ss -lunp | grep ':5005'
sudo tcpdump -ni any udp port 5005
```

Devices time out of the live map after about 1 second. If the selected
spotlight device disappears, Spectrum queues a reset so the renderer does not
remain attached to a disconnected wand.

### MIDI does not work

MIDI is Windows-only. On Linux the adapter is intentionally disabled.

On Windows:

- `midiInputEnabled` must be true,
- configured device indices must still match installed device enumeration,
- opening a bad device can cause activation failure and input quarantine,
- device-set changes are applied on the operator thread,
- binding callback failures are contained and appended to the MIDI log,
- restart the engine after resolving an activation fault.

Use the VJ HUD/MIDI log to distinguish “no device messages” from “message
arrived but binding failed.” A binding that targets a configuration property
must use the application-state dispatcher; do not mutate configuration directly
from the Sanford callback thread.

### A control is locked

Inspect `/api/maintenance/locks`. A healthy abandoned lock expires within 15
seconds. Dome calibration also has a watchdog that cancels the flow after its
lease is absent.

Do not delete or bypass lock checks in an emergency patch. They prevent two
clients from corrupting a multi-step calibration or diagnostic operation.

### The Windows application crashed

Check:

```text
%LOCALAPPDATA%\Spectrum\Logs\spectrum-errors.log
```

The application logs:

- unhandled WPF dispatcher exceptions,
- unhandled application-domain exceptions,
- unobserved task exceptions,
- save failures,
- shutdown failures,
- reported web background-task failures.

An unhandled dispatcher exception is deliberately not marked handled. Continuing
with unknown inconsistent show state is considered less safe than terminating.

Also inspect Visual Studio's Debug output for contained runtime errors when
running a Debug build. Release packages may not retain `Debug.WriteLine`
diagnostics; the runtime telemetry fields and user-visible health endpoints are
the durable evidence for contained faults.

### The process hangs during shutdown

Normal shutdown may wait for:

- Kestrel to stop, with a 2-second stop timeout,
- the operator thread to join,
- input/output workers to stop,
- the state-owner queue to drain,
- pending configuration timer callbacks,
- the final owner-thread save.

Capture thread stacks before killing the process. A cross-thread close of
`SerialPort` is specifically avoided because it can hang; the wand receiver's
worker owns open/read/close and reacts through a desired-state flag.

For Linux:

```shell
systemctl stop spectrum.service
systemctl status spectrum.service --no-pager
journalctl -u spectrum.service --since '-5 min' --no-pager
```

The systemd unit allows 30 seconds before forced termination.

## Common code-change recipes

### Change an existing visualizer

1. Find the concrete renderer under `Core/Visualizers`.
2. Identify its `LayerKey` and its factory in
   `Core/Layers/DomeLayerCatalog.cs`.
3. Identify its option record in
   `Core/Layers/BuiltInLayerRendererOptions.cs`.
4. Identify its schema in
   `Core/Layers/LayerParameterSchemas.cs`.
5. Identify its compiler in
   `Core/Layers/LayerRendererOptionsCompiler.cs`.
6. Confirm its required inputs through `GetInputs`.
7. Make frame-rate-independent changes and avoid per-frame allocation.
8. Add a regression in the closest existing test-support module.
9. Test with the simulator and with at least two engine frame rates if timing
   matters.

Renderer code reads compiled options:

```csharp
TwinkleLayerOptions options =
  this.runtime.GetOptions<TwinkleLayerOptions>();
```

It should not search the live configuration's layer DTOs by instance ID on
every frame.

### Add a new layer visualizer

A complete built-in layer requires all of these:

1. A stable lowercase renderer ID. Never reuse an old ID for different
   semantics.
2. A `DomeLayerParam[]` schema in `LayerParameterSchemas`.
3. A typed `ILayerRendererOptions` record.
4. A compiler method in `LayerRendererOptionsCompiler`.
5. Metadata registration in `BuiltInDomeLayerCatalog`.
6. A concrete renderer implementing `DomeLayerVisualizer`.
7. A factory with its dependencies in `DomeLayerCatalog.Create`.
8. Tests proving catalog completeness, defaults, validation, and relevant
   rendering behavior.
9. User-manual documentation if operators need to understand the effect.

The two catalogs are intentionally separate:

- `BuiltInDomeLayerCatalog` is portable metadata used by validation and UIs.
- `DomeLayerCatalog` binds concrete runtime factories and input dependencies.

If one is updated without the other, catalog construction/tests should fail.

Parameter types are stored as doubles:

- Double: numeric value,
- Bool: 0 or 1,
- Enum: option index,
- Color: packed `0xRRGGBB`,
- Date: `yyyyMMdd`.

Missing overrides use descriptor defaults. The schema is consumed by both UIs,
so a parameter normally does not require hand-written WPF and browser controls.

### Add or change a scalar configuration setting

This is a cross-cutting contract. Inspect all of:

1. `Base/Configuration.cs` for the shared interface, if runtime consumers need
   it.
2. `Core/Configuration/SpectrumConfigurationSchema.cs` for stable key,
   default, validation, web role, and restart policy.
3. `Core/Configuration/SpectrumConfiguration.cs` for storage, owner-thread
   setter, `PropertyChanged`, and affected snapshot publication.
4. `Core/Configuration/SpectrumConfigurationDocument.cs` for XML shape and
   conversion.
5. `SpectrumConfiguration.CreateDocument` for save projection.
6. `Base/RuntimeConfigurationSnapshots.cs` if a background subsystem consumes
   it.
7. The relevant WPF controller/XAML only if it needs a native-only surface.
8. `ConfigurationContractTests` and state-orchestration tests.
9. `Core/Configuration/spectrum_default_config.xml` if the packaged default
   should set a non-code-default value.

`RequiresRestart` means engine reboot, not full process restart. Only
`audioDeviceID` and `domeOutputInSeparateThread` currently use that policy.
Features constructed once with the web host, such as whether simulator routes
exist, need a process restart even if the schema does not mark an engine
restart.

Preserve backward compatibility: omitted XML fields must receive safe code
defaults. Do not rename a persisted key without a migration strategy.

### Change the web UI or API

Server ownership:

- `UserApiRoutes`: ordinary show controls and compound show state,
- `MaintenanceApiRoutes`: setup, health, locks, calibration, tempo,
- `EventApiRoutes`: SSE,
- `WebDomeSimulator`: geometry and binary frame WebSocket,
- controller classes: validation and owner-thread coordination,
- `Web/wwwroot`: dependency-free browser client.

There is no Node/npm build. Static assets are copied by
`Web/Spectrum.Web.csproj`. Rebuild or republish so the changed assets reach the
output directory.

If a browser keeps old JavaScript:

- hard-reload,
- clear the site's cache,
- verify the actual asset returned with `curl`,
- update an asset's query-string cache buster in HTML when appropriate.

Keep high-rate simulator data out of the main REST/SSE control document.
Geometry is static JSON; frames are packed binary RGB over WebSocket.

Every configuration write must go through the dispatcher-backed control
service. Do not capture and mutate a serializer DTO from a request thread.

### Change an input

Implement the `Input` lifecycle:

- `Enabled`: whether configuration/runtime wants the input,
- `AlwaysActive`: whether it runs whenever the engine runs,
- `Active`: own resource/thread start and stop,
- `OperatorUpdate`: bounded work on the operator thread.

Hardware initialization in `Active = true` is inside the operator's activation
failure boundary. A thrown activation is quarantined until engine restart. A
retrying backend such as ALSA may instead activate successfully and contain
device retries on its own worker.

Never block the operator on a long network connection. Never raise an
uncontained exception from an OS callback. Validate UDP and serial input before
indexing fields.

### Change OPC or physical mapping

Read these together:

- `LEDs/DomeWiringLayout.cs`: logical topology and legacy raw addresses,
- `LEDs/DomeOutputMapper.cs`: installed cable/port projection,
- `LEDs/DomeOutputMapping.cs`: immutable compiled mapping,
- `LEDs/DomeOutputSettingsCoordinator.cs`: generation application,
- `LEDs/DomeOpcTransport.cs`: transport lifecycle,
- `LEDs/OPCAPI.cs`: wire framing and reconnect,
- `Tests/LayerPipeline.TestSupport/OPCWireTests.cs`,
- `Tests/Fixtures/README.md`.

Keep logical geometry, installation-specific mapping, and TCP serialization as
separate concepts. Verify RGB byte order, OPC payload length, channel, dense
zero fill, partial writes, reconnect, and known-good hashes.

### Change blend/compositor behavior

Read:

- `Base/DomeBlend*.cs`,
- `Base/CompositeFrameHistory.cs`,
- `LEDs/DomeCompositor.cs`,
- [Dome frame color semantics](color_semantics.md),
- `CompositeOperationTests`,
- `MotionEmbersTests`,
- `RenderPlanTests`.

Spatial operations must sample a pre-pass snapshot so iteration order does not
smear writes. Opacity zero must remain an identity. Decide explicitly whether
source alpha is coverage, a mask, or ignored for the operation.

## Linux service operations

The production unit:

- runs as `spectrum:spectrum`,
- uses `/opt/spectrum`,
- stores data under `/var/lib/spectrum`,
- listens on 8080,
- waits for network-online,
- restarts on failure after 3 seconds,
- handles shutdown with `SIGTERM`,
- uses a read-only system view plus a systemd-managed writable state
  directory.

Routine commands:

```shell
systemctl status spectrum.service --no-pager
journalctl -u spectrum.service -f
systemctl restart spectrum.service
systemctl stop spectrum.service
```

Before replacing a release:

1. stop the engine through the UI/API,
2. stop the service cleanly,
3. copy `/var/lib/spectrum/spectrum_config.xml` and its backup,
4. copy `/opt/spectrum` to a timestamped rollback directory on the same host or
   other storage,
5. qualify the new extracted directory without installing it,
6. replace the install tree only after qualification,
7. preserve root ownership and executable mode,
8. start the service and inspect health,
9. keep the old release until after a real hardware test.

Do not run `deploy/linux/qualify-systemd.sh` on a production host. It
deliberately refuses any host where the production account, paths, service, or
port already exist and is intended for a clean qualification machine.

The safe unprivileged release check is:

```shell
bash deploy/linux/qualify-runtime.sh /path/to/extracted-release
```

It uses a private temporary data directory and port 18081 by default, exercises
HTTP under load, samples runtime telemetry and memory, sends `SIGTERM`, and
removes its test-owned temporary files.

## Windows portable release and rollback

The Windows configuration lives beside the application. A new release
directory therefore does not automatically inherit the live show state.

Use versioned directories:

```text
Spectrum-2026-07-28-known-good/
Spectrum-2026-07-29-candidate/
```

For a candidate:

1. leave the known-good directory unchanged,
2. copy the live primary and backup into the candidate,
3. keep hardware output disabled for the first run,
4. start the candidate and verify configuration, browser, simulator, and
   runtime telemetry,
5. perform a low-brightness physical check,
6. close it cleanly before returning to the old release.

Do not run two copies simultaneously on port 8080. The second desktop can still
open, but its browser service will fail to bind, which makes the test
ambiguous.

Rollback is closing the candidate cleanly and starting the untouched known-good
directory with the desired copied configuration. Decide whether to keep the
candidate's newly saved configuration; binary rollback and configuration
rollback are separate decisions.

## Collecting an offline diagnostic bundle

Before making a code change, collect:

- exact timestamp and timezone,
- deployed parent and Madmom commits,
- `git status --short` and `git diff`,
- runtime endpoint JSON,
- audio endpoint JSON,
- wand serial and wand-state JSON,
- active locks,
- layer, palette, and scene state,
- primary and backup configuration,
- Windows Spectrum error log or Linux journal,
- service status and process exit code,
- relevant listener/socket state,
- a screenshot or short description of simulator versus physical output,
- hardware topology facts: host IPs, OPC endpoint, switch/link state,
- exact recovery actions already tried.

Linux example:

```shell
date --iso-8601=seconds
curl -sS http://127.0.0.1:8080/api/maintenance/runtime
curl -sS http://127.0.0.1:8080/api/maintenance/audio
curl -sS http://127.0.0.1:8080/api/maintenance/wands/serial
curl -sS http://127.0.0.1:8080/api/maintenance/locks
systemctl status spectrum.service --no-pager
journalctl -u spectrum.service --since '-30 min' --no-pager
ss -ltnp
ss -lunp
```

Copy configuration before including it in a bundle. It contains installation
addresses and show state, though Spectrum currently has no passwords or API
tokens.

## Hard-coded contracts worth knowing

| Contract | Value |
| --- | --- |
| Required .NET SDK | 10.0.302, roll-forward disabled |
| C# language version | 14 |
| Browser port | TCP 8080 by default |
| Pro DJ Link | UDP 50001 |
| Orientation/wand UDP | UDP 5005 |
| Wand maximum transmit rate | 200 Hz |
| Wand live timeout | 1.5 seconds for receiver presentation |
| Orientation device timeout | about 1 second |
| Operator cap | 400 Hz |
| OPC cap | 200 Hz |
| Browser simulator cap | 60 FPS |
| OPC reconnect delay | 250 ms |
| OPC connect timeout | 2 seconds |
| ALSA format | S16_LE, 44.1 kHz, stereo then mono |
| ALSA read block | 1,024 frames |
| ALSA retry | 1 second |
| Madmom restart delay | 2 seconds |
| Advisory-lock lease | 15 seconds |
| Calibration watchdog | 3 seconds |
| Configuration debounce | 100 ms |
| Visualizer quarantine | 10 consecutive failures |
| Input/output update quarantine | 3 consecutive failures |
| Test-pattern normal state | 0 / `None` |
| Dome geometry | 190 struts, 7,580 logical LEDs |
| Controller layout | 5 boxes, 8 ports each, 10 cable halves |

When changing one of these values, search for tests and client-side mirrors.
Some browser diagnostics intentionally duplicate a constant and include a
“must agree” comment.

## High-value source index

Use this when time is short.

| Question | Start here |
| --- | --- |
| Why did startup/load/save fail? | `Core/SpectrumHost.cs`, `Core/SpectrumConfigurationSession.cs`, `Base/ConfigurationFileStore.cs`, `Base/ConfigurationPersistenceCoordinator.cs` |
| Why did a setting not affect runtime? | `Core/Configuration/SpectrumConfiguration.cs`, `Base/RuntimeConfigurationSnapshots.cs` |
| Why did the engine stop/use a component? | `Core/Runtime/Operator.cs` |
| Why was a layer rejected? | `Base/LayerPipeline.cs`, `Core/Layers/DomeLayerCatalog.cs`, `Core/Runtime/Operator.cs` |
| Where is a layer's control defined? | `Core/Layers/LayerParameterSchemas.cs`, `Core/Layers/LayerRendererOptionsCompiler.cs` |
| Why are colors/blends wrong? | `LEDs/DomeCompositor.cs`, `Base/DomeBlend*.cs`, `docs/color_semantics.md` |
| Why is the simulator different? | `LEDs/DomeSimulatorPublisher.cs`, `Web/WebDomeSimulator.cs` |
| Why is OPC dark/disconnected? | `LEDs/DomeOpcTransport.cs`, `LEDs/OPCAPI.cs` |
| Why is physical mapping wrong? | `LEDs/DomeOutputMapper.cs`, `LEDs/DomeWiringLayout.cs`, `Web/DomeCalibrationController.cs` |
| Why is audio missing? | `Audio/AudioInput.cs` or `Platform.Linux/AlsaAudioLevelInput.cs` |
| Why is Madmom missing? | `Base/MadmomRuntimeLocator.cs`, `Audio/MadmomHandler.cs`, `Platform.Linux/MadmomPcmBeatTracker.cs` |
| Why is Pro DJ Link missing? | `Core/ProDjLinkInput.cs` |
| Why are wands missing? | `Core/Inputs/OrientationInput.cs`, `Core/Inputs/WandSerialReceiver.cs`, `Core/Protocols/*` |
| Why is MIDI missing? | `MIDI/MidiInput.cs`, `Base/MIDI/*` |
| Why is the browser stale/broken? | `Web/WebServer.cs`, route modules, `Web/ConfigEventStream.cs`, `Web/wwwroot` |
| Why is a control locked? | `Web/AdvisoryLockManager.cs`, `Web/DomeCalibrationController.cs` |
| What does CI actually ship? | `.github/workflows/msbuild.yml`, `build.ps1` |

## Final pre-show verification after any change

Do not hand the system back until all applicable checks pass:

- working tree diff reviewed and saved,
- exact binary/source commit recorded,
- relevant tests passed,
- process starts from the intended release directory,
- expected configuration source loaded,
- primary configuration saves successfully,
- no `layerPlanError`,
- no visualizer, input, or output fault,
- engine FPS is stable,
- audio device and live level are correct,
- intended tempo source and BPM are correct,
- wand receiver and devices are healthy if used,
- MIDI messages and bindings work on Windows if used,
- browser REST and SSE work from the operator device,
- simulator matches the intended look,
- OPC FPS is nonzero when hardware output is enabled,
- low-brightness physical mapping is correct,
- maximum brightness is set deliberately,
- current brightness is set deliberately,
- dome test pattern is `None`,
- intended scene is loaded,
- known-good rollback remains available.

When time is short, preserving a stable show is more important than landing a
beautiful patch. Keep the evidence, keep the rollback, and change one boundary
at a time.
