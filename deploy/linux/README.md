# Spectrum Linux service

The self-contained `linux-x64` archive runs the browser-controlled headless
host without requiring a system-wide .NET installation. Audio and MIDI inputs
use different readiness levels: native ALSA audio-level capture is available,
while MIDI remains disabled. The browser simulator, OPC output, UDP orientation
input, and USB serial wand input remain available.

The self-contained .NET archive still uses the distribution's ALSA shared
library and C++ runtime. Ensure `libasound.so.2` and `libstdc++.so.6` with
`GLIBCXX_3.4.22` or newer are installed before starting the service. Stock
Ubuntu 16.04 does not meet this loader requirement.

On Ubuntu 24.04, install the ALSA runtime with:

```shell
apt-get update
apt-get install libasound2t64
```

## Install

Run these commands as root after extracting the release archive to a temporary
directory:

```shell
useradd --system --home-dir /var/lib/spectrum --shell /usr/sbin/nologin spectrum
install -d -o root -g root -m 0755 /opt/spectrum
cp -a . /opt/spectrum/
chown -R root:root /opt/spectrum
chmod 0755 /opt/spectrum/Spectrum.Host
install -o root -g root -m 0644 \
  /opt/spectrum/deploy/linux/spectrum.service \
  /etc/systemd/system/spectrum.service
systemctl daemon-reload
systemctl enable --now spectrum.service
```

The unit stores configuration and its recovery backup under
`/var/lib/spectrum`, listens on port 8080, waits for `network-online.target`,
and restarts after an unexpected failure. `systemd` creates the state directory
for the unprivileged `spectrum` user.

Open `http://<host>:8080` from the trusted lighting network. Spectrum currently
has no authentication, so do not expose this port directly to the Internet.

## Hardware permissions

Use the browser maintenance page to select the ALSA PCM capture device. It saves
the stable ALSA name in `audioDeviceID`, shows peak level and capture errors, and
restarts the engine after a selection change. The capture worker keeps retrying
if the interface is missing or unplugged.

The service account needs permission to open the selected capture device. On
distributions that grant direct ALSA hardware access through the `audio` group:

```shell
usermod -aG audio spectrum
systemctl restart spectrum.service
```

Session-scoped PipeWire configurations may require a user service or an explicit
ALSA device/ACL instead of group access; qualify this on the target distribution.

For a USB serial wand receiver, add the service account to the group that owns
the device (commonly `dialout`) and restart the service:

```shell
usermod -aG dialout spectrum
systemctl restart spectrum.service
```


## Operations

```shell
systemctl status spectrum.service
journalctl -u spectrum.service -f
systemctl restart spectrum.service
systemctl stop spectrum.service
```

To use a different port or data directory, create a systemd override with
`systemctl edit spectrum.service` and replace `ExecStart`. A replacement must
first clear the original command:

```ini
[Service]
ExecStart=
ExecStart=/opt/spectrum/Spectrum.Host --data-dir /srv/spectrum --port 8090
ReadWritePaths=/srv/spectrum
```
