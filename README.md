# Spectrum

Spectrum is a lighting-control application written in C#. Its desktop frontend
and physical audio/MIDI adapters are Windows-only. The lighting engine, browser
controller, simulator, OPC output, orientation inputs, and a headless host now
build as portable .NET 10 code for Linux.

## Installation

The complete desktop application and show audio/MIDI stack currently require a
64-bit Windows computer.

1. Install [Visual Studio Community](https://visualstudio.microsoft.com/vs/community/)
   with the **.NET desktop development** and **Desktop development with C++**
   workloads.
2. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0),
   [Git for Windows](https://git-scm.com/download/win), and
   [uv](https://docs.astral.sh/uv/getting-started/installation/).
3. Clone the repository. A recursive clone is preferred, but the build script
   also initializes missing submodules:

   ```powershell
   git -c core.autocrlf=false clone --recursive https://github.com/campmindshark/spectrum
   cd spectrum
   ```

4. Run the checkout-to-artifact build:

   ```powershell
   .\build.ps1
   ```

The first build needs Internet access. It provisions an isolated CPython 3.11
environment under `Madmom/.build-env`, compiles the Cython extensions with MSVC, runs
the Python and .NET verification suites, and writes these ignored artifacts:

- `artifacts/wheels/`: the CPython 3.11 x64 Madmom wheel.
- `artifacts/Spectrum/`: the self-contained .NET application and standalone
  Python runtime.
- `artifacts/Spectrum-win-x64.zip`: the portable release directory as an
  archive.

For a faster developer/CI build that omits the portable application, run
`.\build.ps1 -SkipPortable`. To build only the Python component, run
`.\Madmom\scripts\build.ps1`.

### Linux headless host and portable verification

The `Spectrum.Host` console application runs the engine and browser controller
with audio and MIDI disabled. With the .NET 10 SDK installed:

```shell
dotnet run --project Host/Spectrum.Host.csproj -c Release -- --data-dir ./spectrum-data
```

Open `http://localhost:8080` to use the controller or browser dome simulator.
`--port` changes the listener port. Without `--data-dir`, configuration follows
`SPECTRUM_DATA_DIR`, `XDG_CONFIG_HOME/spectrum`, then `~/.config/spectrum`.
`Ctrl+C` and `SIGTERM` perform an ordered shutdown and flush pending changes.

The host is an OPC/simulator MVP, not full Linux show-hardware parity: Linux
audio, beat tracking, and MIDI backends remain to be implemented. The portable
regression suite runs with:

```shell
dotnet run --project Tests/Portability/Spectrum.Portability.Tests.csproj -c Release
```

To create the same self-contained `linux-x64` directory used by CI and tagged
releases:

```shell
dotnet publish Host/Spectrum.Host.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -o artifacts/Spectrum-linux-x64
```

The publish directory includes the browser assets, packaged default
configuration, license, and a hardened systemd unit with installation notes in
[`deploy/linux/README.md`](deploy/linux/README.md). Tagged releases attach this
host as `Spectrum-linux-x64.tar.gz` alongside the complete Windows desktop archive.
The Ubuntu CI job launches the published executable, waits for its HTTP API,
persists a setting, sends `SIGTERM`, and verifies the clean shutdown and saved
configuration.

The remaining hardware and packaging work is tracked in
[`docs/linux_port.md`](docs/linux_port.md).

## Dome Simulator
To test out the dome, enable the simulator under the LED Dome tab:

<img alt="Simulator Settings" src="https://user-images.githubusercontent.com/671052/63136544-847d7d80-bfa0-11e9-81e9-bba208a135fc.png" height=350>

When it's working, you should see something like the following:

<img alt="SDome Simulator" src="https://user-images.githubusercontent.com/671052/63136574-9c550180-bfa0-11e9-9d50-6d1cf4cc347c.png" height=500>
