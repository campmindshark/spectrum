# Spectrum

Spectrum is a Windows application written in C# intended for running lighting installations.

## Installation

Note that it is currently only possible to build and run Spectrum on a 64-bit Windows computer.

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

## Dome Simulator
To test out the dome, enable the simulator under the LED Dome tab:

<img alt="Simulator Settings" src="https://user-images.githubusercontent.com/671052/63136544-847d7d80-bfa0-11e9-81e9-bba208a135fc.png" height=350>

When it's working, you should see something like the following:

<img alt="SDome Simulator" src="https://user-images.githubusercontent.com/671052/63136574-9c550180-bfa0-11e9-9d50-6d1cf4cc347c.png" height=500>
