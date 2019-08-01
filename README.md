# Spectrum

Spectrum is a Windows application written in C# intended for running lighting installations.

## Installation

Note that it is currently only possible to build and run Spectrum on a 64-bit Windows computer.

1. Install [Visual Studio 2019 Community](https://visualstudio.microsoft.com/downloads/)
    - During installation the installer will prompt you to select the components you wish to install. Make sure you select "Python development", ".NET desktop development", and "Desktop development with C++".
    - Note that if you have an existing VS2019 installation, you will likely still have to update it to the latest build using this installer. We've had people report issues when running on very recent (but not the latest) builds.
2. Install Python 3.7 x64 for Windows
    - Note that the default download on python.org is x86. You're looking for the "Windows x86-64 executable installer". The current latest version is [here](https://www.python.org/ftp/python/3.7.3/python-3.7.3-amd64.exe).
    - Make sure to select the option at the end to edit your `PATH` to include Python!
3. Install [Git for Windows](https://git-scm.com/download/win)
    - Select the option to convert line breaks to Unix format
    - Make sure you install git bash
4. Install `virtualenv` for Python
    - Open git bash and run `pip install virtualenv`
5. Recursively clone this repo: `git clone --recursive https://github.com/campmindshark/spectrum`
6. Open the Spectrum solution in Visual Studio and run it!
    - You will need Internet access for the first build, so if you're heading to TTITD, please compile beforehand, once for Release and once for Debug.
