# Spectrum - No Madmom edition v1.0.11

Spectrum is a Windows application written in C# intended for running lighting installations.

## Installation

Note that it is currently only possible to build and run Spectrum on a 64-bit Windows computer.

1. Install [Visual Studio 2022 Community](https://visualstudio.microsoft.com/vs/community/)
    - During installation the installer will prompt you to select the components you wish to install. Make sure you select ".NET desktop development", and "Desktop development with C++".
    - Note that if you have an existing VS2019 installation, you will likely still have to update it to the latest build using this installer. We've had people report issues when running on very recent (but not the latest) builds.
    - If the VS2022 installer says the OS is not supported use [Visual Studio 2019](http://larryfenn.com/vs_Community.exe).
2. Install [Git for Windows](https://git-scm.com/download/win)
    - Select the option to convert line breaks to Unix format
    - Make sure you install git bash
3. Clone this repo: `git clone https://github.com/campmindshark/spectrum`
5. Open the Spectrum solution in Visual Studio and run it!
    - You will need Internet access for the first build, so if you're heading to TTITD, please compile beforehand, once for Release and once for Debug.

## Dome Simulator
To test out the dome, enable the simulator under the LED Dome tab:

<img alt="Simulator Settings" src="https://user-images.githubusercontent.com/671052/63136544-847d7d80-bfa0-11e9-81e9-bba208a135fc.png" height=350>

When it's working, you should see something like the following:

<img alt="SDome Simulator" src="https://user-images.githubusercontent.com/671052/63136574-9c550180-bfa0-11e9-9d50-6d1cf4cc347c.png" height=500>
