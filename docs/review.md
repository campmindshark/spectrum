# Code and test cleanup review

Date: 2026-07-22

## Cleanup progress

- [x] Compiler hygiene migration is complete. Added repository-wide C# 14 and
  .NET analyzer defaults, then enabled nullable analysis and warnings-as-errors
  for `Host`, `Platform.Linux`, `Base`, `LEDs`, `Core`, `Web`, `MIDI`, `Audio`,
  `Spectrum`, and shared layer-pipeline test support. The original 38 headless
  warnings, `Base`'s 187-warning backlog, and `LEDs`' 57-warning backlog were
  replaced with explicit optional-value, process/transport-lifecycle, callback,
  serializer-DTO, immutable-view, lookup-result, simulator-mailbox, lazy
  topology, compositor-frame, and parse-result contracts. Immutable
  configuration views now filter invalid null collection entries at their
  publication boundary, and spatial blends diagnose a missing required
  destination snapshot instead of dereferencing it. The stricter `Base` API
  also exposed and fixed two Linux call sites for an absent Madmom runtime or
  audio-device ID. `Core`'s 162-warning backlog is now zero: omitted XML
  collections normalize to empty owner state, invalid null entries are filtered
  during detached graph copies, the audio-device ID is explicitly optional,
  and host, socket, serial, worker-thread, callback, lazy visualizer cache,
  topology, and effect-state lifecycles have explicit contracts. The complete
  headless graph builds with zero warnings, and all 238 tests pass. `Web`'s
  197-warning backlog is also now zero: optional API
  values are distinct from required response fields, and advisory-lock,
  parameter, palette, scene, audio-device, telemetry, browser-simulator,
  controller DTO, SSE subscriber, and web-host lifecycle contracts are explicit.
  `MIDI`'s 11-warning backlog is now zero after making its device-set lifecycle,
  callback/event availability, preset lookup, and compiled-binding validation
  explicit. `Audio`'s 28-warning backlog is now zero after making WASAPI device
  and capture ownership, backend errors, Madmom runtime discovery, and child
  process/restart-timer states explicit. Test support's 49-warning backlog is
  now zero as well: negative tests mark intentional contract violations, result
  tuples preserve optional success/error values, assertion helpers communicate
  flow guarantees, and loopback socket/capture ownership is explicit. The
  Windows application's 211-warning backlog is now zero after making WPF
  converter results, composition-root initialization, child-window/timer state,
  palette/layer view models, calibration leases, simulator frames, and MIDI
  editor selections explicit. This also fixed an invalid-new-preset path that
  continued after validation failure and dereferenced a missing preset. The
  test executables are clean as well: the Windows and portable layer-pipeline
  runners' 185- and 182-warning backlogs were replaced with explicit assertion
  flow, nullable compiler/renderer results, DTO and snapshot ownership, and
  compositor-frame requirements; portability tests' 22-warning backlog now has
  explicit runtime-discovery, stream, endpoint, host-lifecycle, and optional
  error contracts. All 13 first-party projects now enable nullable analysis and
  warnings-as-errors. A full Release solution build reports zero warnings and
  all 238 tests pass.
- [x] Replaced the calibration watchdog `async void` timer callback with one
  owned `PeriodicTimer` task. Shutdown now cancels and awaits the task, ticks
  cannot overlap, and retryable failures go to the WPF error log or headless
  stderr. Verified with a clean Release solution build and all three existing
  verification executables.
- [x] Migrated all three hand-built test harnesses to MSTest 4.1 and Microsoft
  Testing Platform. `dotnet test` discovers and passes 111 Windows
  layer-pipeline cases, 108 portable layer-pipeline cases, and 19 portability
  cases. `build.ps1` now invokes every suite through the standard test command.
  The portability runner retains its special fake-PCM child-process mode.
  (All test projects were already present in `Spectrum.sln` when cleanup began.)
- [x] Test-suite decomposition is complete. Moved six shared configuration,
  persistence, SSE, and web-contract cases and two Point Cloud geometry and
  spatial-index cases into dedicated test-support modules registered by both
  layer-pipeline targets. Moved another ten compositor and blend-operation
  cases, together with their owned fixtures, into a shared
  `CompositeOperationTests` module. Split thirteen built-in visualizer cases
  into reactive, topology, particle, and environment modules; their small
  common fixture surface now lives in the shared fixture module, and the largest
  of those subject modules is 576 lines. Twenty-five more shared cases now live
  in dedicated renderer-runtime, state-orchestration, core-pipeline, and
  render-plan modules. The final seven Windows-specific and four
  portable-specific orchestration cases live in target-owned modules rather
  than the common entry files. The Windows and portable entry files are now
  51-line discovery and registration shells, down from 5,841 and 5,474 lines,
  with no reflection-based implicit case collection. Replaced ten private
  copies of the basic assertion primitive across all three suites and the
  existing subject modules with one nullable-flow-aware helper. A full Release
  solution build succeeds with zero warnings, and `dotnet test` still passes
  all 238 tests.
- [x] Replaced formatting-sensitive JSON checks in the portability web-host
  test with `JsonDocument` property assertions, including SSE data events.
  Replaced Linux runtime `grep`/`sed` JSON parsing with typed `jq` queries and
  documented the dependency. The portability suite passes and the shell script
  passes `bash -n`.
- [x] Removed all references to the seven nonexistent documents. Removed the
  tracked `.csproj.user` files while preserving the Windows application's
  unmanaged-debug setting in shared project configuration. The Release solution
  build remains clean.
- [x] Confirmed two structural findings were already resolved in the starting
  tree: portable implementation files are physically owned by `Core` with no
  `SPECTRUM_FEATURES` or linked compilation, and the portable layer-pipeline
  project owns separate sources and a distinct assembly. No further change was
  needed for those findings.
- [x] Removed timing-based polling from the integration tests. All explicit
  test sleeps, `SpinWait` polling, `Socket.Available` polling, and sub-500 ms
  speed assertions are gone. OPC tests wait for async-connect/TCP events and
  use a zero-throttle test configuration; Pro DJ Link, ALSA, Madmom,
  dispatcher, settings-reconciliation, FPS, and allocation coverage wait for
  their corresponding state signals. Bounded waits remain only as deadlock and
  missing-event guards. All three framework suites pass after the conversion.

## Executive summary

The production build and all three .NET verification executables pass, but the
repository has substantial structural debt that the normal build does not
surface. The highest-risk cleanup targets are an asynchronous timer callback
that can escape shutdown, a hand-built test harness that can silently omit
tests, and project boundaries implemented through linked source files and
preprocessor symbols rather than real ownership.

The normal Release build reports no warnings. That result is misleading because
nullable reference analysis and current compiler analyzers are not enabled. A
forced nullable rebuild produced 599 unique compiler warnings, including 199
uninitialized-member warnings and five possible-null dereference warnings.

The review covers the first-party .NET projects, their test executables, build
scripts, and Linux qualification checks. `Madmom` is a Git submodule and was
treated as external code rather than reviewed line by line.

## Findings

### P1: Calibration watchdog work can escape shutdown

`Spectrum/Web/WebServer.cs` creates a periodic `System.Threading.Timer` whose
callback is `async void` (`ReconcileCalibrationLease`). The callback returns to
the timer at its first `await`, which has three consequences:

- `Timer.DisposeAsync()` cannot wait for the asynchronous continuation.
- A slow reconciliation can overlap a later timer tick.
- `StopAsync()` can return while reconciliation is still touching application
  state.

The callback also catches every exception without reporting it, making a
persistent failure indistinguishable from successful best-effort operation.

Replace the callback with one owned background `Task` using `PeriodicTimer` and
a `CancellationTokenSource`. Cancel and await that task during `StopAsync()`, and
report failures through the host's normal diagnostics path.

Relevant code: `Spectrum/Web/WebServer.cs:129-168`.

### P1: The test infrastructure can silently omit tests

`Tests/Program.cs` is a 5,783-line executable with a manually maintained list of
`Run(name, method)` calls. A test method that is not added to this list compiles
successfully and never runs. The test projects are console applications rather
than projects understood by `dotnet test`, and they are absent from
`Spectrum.sln`.

This loses automatic discovery, filtering, per-test timing, structured result
files, standard CI reporting, coverage integration, and native asynchronous
test support. The repository also contains nine separate implementations of a
basic `Assert` helper.

Move the suites to xUnit, NUnit, or MSTest, split the giant `Program.cs` by
subject, and add the test projects to the solution. Keep true process-level
qualification checks as integration tests, but run them through a discoverable
test or an explicitly named script.

Relevant code:

- `Tests/Program.cs:22-176`
- `Tests/Spectrum.LayerPipeline.Tests.csproj:1-13`
- `build.ps1:93-117`

### P2: Project boundaries are simulated rather than enforced

`Spectrum.Core` links most of its implementation from the physical `Spectrum`
directory and recompiles files from `Base` under the `SPECTRUM_FEATURES`
preprocessor symbol. The WPF project then excludes the same sources with a long
list of `Compile Remove` entries.

The filesystem therefore lies about ownership: editing a file under the WPF
application can change the portable core, and two files produce different type
sets depending on which project compiles them. IDE navigation, static analysis,
and future refactoring all have to understand this MSBuild trick.

Move portable sources physically into `Core`. Split built-in feature schemas and
renderer options out of the conditional `Base` files into normal `Core` files,
then remove `SPECTRUM_FEATURES`, linked compilation, and the WPF exclusion list.

Relevant code:

- `Core/Spectrum.Core.csproj:7-51`
- `Spectrum/Spectrum.csproj:48-65`
- `Base/DomeLayerSettings.cs:7-190`
- `Base/LayerRendererOptions.cs:6-18`

### P2: Compiler hygiene is disabled

None of the projects enables nullable reference analysis, shared analyzer
settings, or warnings-as-errors. A forced non-incremental Release build with
`Nullable=enable` produced 599 unique warnings:

- 199 `CS8618` uninitialized non-nullable members
- 143 `CS8625` null literals passed to non-nullable references
- 72 `CS8600` possible-null conversions
- 60 `CS8603` possible-null returns
- 39 `CS8604` possible-null arguments
- 5 `CS8602` possible-null dereferences

Add a repository-level `Directory.Build.props` for common language and analyzer
settings. Enable nullable analysis one project at a time, starting with new
portable infrastructure, and burn down warnings without indiscriminate `!`
suppression. Make warnings fatal after the backlog reaches zero.

### P2: Configuration metadata has too many sources of truth

Configuration facts are duplicated across:

- `SpectrumConfiguration` live state and defaults
- `SpectrumConfigurationDocument` serializer properties and manual copying
- `spectrum_default_config.xml`
- `SpectrumParameters` web descriptors and validation ranges
- WPF XAML control ranges and bindings
- Windows and headless reboot-property lists

`SpectrumParameters` explicitly says to keep its ranges synchronized with XAML.
That is a maintenance failure waiting to happen: adding or changing one setting
requires finding every parallel representation.

Introduce one typed configuration schema that owns stable identifiers, defaults,
validation, display metadata, persistence policy, and restart requirements.
Serialization DTOs may remain separate where XSerializer requires mutable
collections, but their mapping should be generated or exhaustively tested from
the same schema.

Relevant code:

- `Spectrum/Web/SpectrumParameters.cs:7-20`
- `Spectrum/SpectrumConfigurationDocument.cs:11-83`
- `Spectrum/SpectrumConfiguration.cs`
- `Spectrum/Windows/MainWindow.xaml.cs:64-66`
- `Host/Program.cs:68-69`

### P2: Several classes are god objects

The most obvious concentrations of unrelated responsibility are:

- `Spectrum/Windows/MainWindow.xaml.cs`: 1,616 lines. It owns host lifecycle,
  readiness diagnostics, persistence interaction, MIDI device and preset CRUD,
  MIDI binding parsing, child-window management, and serial-port UI.
- `LEDs/LEDDomeOutput.cs`: 1,311 lines and roughly 45 state fields. It owns
  physical topology, cable and port mapping, OPC transport lifecycle, renderer
  registration, simulator queues and frame pooling, frame state, palette
  sampling, and hardware writes.
- `Spectrum/Web/WebServer.cs`: 673 lines mapping nearly every HTTP endpoint plus
  web-host lifecycle and calibration watchdog behavior.

The 231-line `MidiAddBindingClicked` handler is especially repetitive. It parses
individual fields by throwing and catching broad `Exception`, duplicates the
same validation in focus handlers, and directly mutates persistence models.

Extract a MIDI binding editor/view-model, OPC transport, dome mapping service,
simulator publisher, palette sampler, and route modules. Replace exception-based
UI parsing with reusable `TryParse` validation that produces user-visible error
state.

Relevant code:

- `Spectrum/Windows/MainWindow.xaml.cs:989-1219`
- `LEDs/LEDDomeOutput.cs:174-235`
- `Spectrum/Web/WebServer.cs:171-498`

### P2: JSON tests assert text formatting instead of data

The portability web-host test checks serialized responses with `Contains`, and
the Linux runtime qualification script parses JSON with `grep` and `sed`. These
checks are coupled to serializer spelling and formatting, can be broken by a
harmless naming-policy change, and can accept malformed or unrelated content.

Deserialize responses into a DTO or `JsonDocument` and assert typed property
values. Shell qualification should use a structural JSON parser such as `jq` or
a small checked-in helper rather than regular expressions.

Relevant code:

- `Tests/Portability/Program.cs:635-660`
- `Tests/Portability/Program.cs:707-714`
- `deploy/linux/qualify-runtime.sh:169-187`

### P2: Portable tests are duplicated through linked compilation

`Tests/PortableLayerPipeline/Spectrum.PortableLayerPipeline.Tests.csproj` links
all top-level `Tests/*.cs`, assigns itself the same assembly name as the Windows
test executable, and changes behavior with `PORTABLE_LAYER_PIPELINE`. The main
test source contains repeated `#if` blocks to decide which tests exist in each
binary.

Use one multi-targeted test project when the same behavioral suite should run on
`net10.0` and `net10.0-windows`. Put genuinely platform-specific tests in
separate source files or projects rather than compiling one giant source file
under different symbols.

Relevant code:

- `Tests/PortableLayerPipeline/Spectrum.PortableLayerPipeline.Tests.csproj:4-12`
- `Tests/Program.cs:11-13`
- `Tests/Program.cs:49-52`
- `Tests/Program.cs:1607-1810`

### P2: Timing-based integration tests are unnecessarily flaky

The test executables use `Thread.Sleep`, wall-clock polling, real loopback
sockets, and machine-speed assertions such as requiring an operation to finish
within 500 ms. These tests passed locally, but loaded CI agents can fail them for
reasons unrelated to behavior.

Prefer task-completion signals, injectable clocks and retry policies, and
bounded asynchronous waits. Where a real network boundary is essential, wait on
observable protocol events rather than polling `Socket.Available` and sleeping.

Relevant code:

- `Tests/Portability/Program.cs:1083-1119`
- `Tests/Portability/Program.cs:489-542`
- `Tests/OPCWireTests.cs:897`
- `Tests/Program.cs:1768`

### P3: Documentation references are broken

First-party source comments refer to seven documents that do not exist in the
working tree and have never been tracked:

- `docs/arch_issues.md`
- `docs/caustics.md`
- `docs/layer_params_implementation.md`
- `docs/layers_inventory.md`
- `docs/prism.md`
- `docs/triggers.md`
- `docs/web_architecture.md`

Either add authoritative versions of these documents or remove the references.
Dead architectural breadcrumbs make comments less trustworthy than having no
reference at all.

### P3: User-specific Visual Studio files are tracked

`Base/Base.csproj.user` and `Spectrum/Spectrum.csproj.user` are tracked even
though `.gitignore` correctly classifies `*.user` as user-specific. Remove them
from the index. If unmanaged debugging is a project requirement, express it in a
shared launch profile or documented developer setup rather than a per-user
project file.

## Recommended cleanup order

1. Replace the `async void` watchdog with an owned, awaited background task.
2. Adopt a real test framework and add test projects to the solution without
   changing existing assertions yet.
3. Split `Tests/Program.cs` and remove the duplicated assertion helpers.
4. Make JSON and timing-sensitive integration tests structural and signal-based.
5. Physically move portable code into `Core` and remove linked conditional
   compilation.
6. Add shared compiler/analyzer settings and migrate nullable analysis one
   project at a time.
7. Centralize configuration metadata.
8. Decompose the three largest god objects along their existing responsibility
   seams.
9. Repair documentation references and remove tracked IDE-user files.

## Verification performed

The following checks passed during the review:

- `dotnet build Base/Base.csproj -c Release --no-restore -t:Rebuild`
  after enabling nullable warnings-as-errors (zero warnings)
- `dotnet build LEDs/LEDs.csproj -c Release --no-restore -t:Rebuild`
  after enabling nullable warnings-as-errors (zero warnings)
- `dotnet build Core/Spectrum.Core.csproj -c Release --no-restore -t:Rebuild`
  after enabling nullable warnings-as-errors (zero warnings)
- `dotnet build Web/Spectrum.Web.csproj -c Release --no-restore -t:Rebuild`
  after enabling nullable warnings-as-errors (zero warnings)
- `dotnet build MIDI/MIDI.csproj -c Release --no-restore -t:Rebuild
  -p:BuildProjectReferences=false` after enabling nullable warnings-as-errors
  (zero warnings)
- `dotnet build Audio/Audio.csproj -c Release --no-restore -t:Rebuild
  -p:BuildProjectReferences=false` after enabling nullable warnings-as-errors
  (zero warnings)
- `dotnet build Tests/LayerPipeline.TestSupport/Spectrum.LayerPipeline.TestSupport.csproj
  -c Release --no-restore -t:Rebuild -p:BuildProjectReferences=false` after
  enabling nullable warnings-as-errors (zero warnings)
- `dotnet build Spectrum/Spectrum.csproj -c Release --no-restore -t:Rebuild
  -p:BuildProjectReferences=false` after enabling nullable warnings-as-errors
  (zero warnings)
- `dotnet build Tests/Spectrum.LayerPipeline.Tests.csproj -c Release
  --no-restore -t:Rebuild -p:BuildProjectReferences=false` after enabling
  nullable warnings-as-errors (zero warnings)
- `dotnet build Tests/PortableLayerPipeline/Spectrum.PortableLayerPipeline.Tests.csproj
  -c Release --no-restore -t:Rebuild -p:BuildProjectReferences=false` after
  enabling nullable warnings-as-errors (zero warnings)
- `dotnet build Tests/Portability/Spectrum.Portability.Tests.csproj -c Release
  --no-restore -t:Rebuild -p:BuildProjectReferences=false` after enabling
  nullable warnings-as-errors (zero warnings)
- `dotnet build Host/Spectrum.Host.csproj -c Release --no-restore`
  after enabling shared analyzers and nullable warnings-as-errors (zero warnings)
- `dotnet build Platform.Linux/Spectrum.Platform.Linux.csproj -c Release
  --no-restore -t:Rebuild -p:BuildProjectReferences=false`
  after enabling nullable warnings-as-errors (zero warnings)
- `dotnet build Spectrum.sln -c Release --no-restore`
- `dotnet test Tests/Spectrum.LayerPipeline.Tests.csproj -c Release`
  (111 passed)
- `dotnet test Tests/PortableLayerPipeline/Spectrum.PortableLayerPipeline.Tests.csproj -c Release`
  (108 passed)
- `dotnet test Tests/Portability/Spectrum.Portability.Tests.csproj -c Release`
  (19 passed)
- `dotnet test Spectrum.sln -c Release --no-build` after the repository-wide
  nullable migration (238 passed across the three suites)

The nullable-warning count came from a non-incremental Release build with
`-p:Nullable=enable -p:WarningLevel=9999`, de-duplicated by source location and
warning text.
