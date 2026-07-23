# Spectrum Linux service

The self-contained `linux-x64` archive runs the browser-controlled headless
host without requiring a system-wide .NET installation. Audio and MIDI inputs
use different readiness levels: native ALSA audio-level capture and Madmom beat
tracking are available, while MIDI remains disabled. The browser simulator,
OPC output, UDP orientation input, and USB serial wand input remain available.

The standalone live simulator is available at
`http://<host>:8080/simulator`. It loads the dome geometry once and streams
binary RGB frames over WebSocket at up to 60 FPS. This browser simulator is the
Linux UI; the native WPF simulator window remains Windows-only.

The archive also includes a relocatable CPython 3.11 Madmom runtime with the
Linux Cython extensions and PyAudio under `Madmom/runtime`. The runtime is
packaged and qualified with both file and raw-PCM inputs. When Madmom is the
selected tempo source, the headless host starts that packaged tracker and feeds
it a mono copy of the PCM already captured by ALSA. ALSA remains the only owner
of the hardware device, so saved stable PCM names never need to be translated
to PortAudio's independently enumerated indices. Tracker startup, pipe, and
exit failures are contained, reported on the audio maintenance page, and
retried while the source remains selected.

The self-contained .NET archive still uses the distribution's ALSA shared
library and C++ runtime. The packaged Python audio adapter also uses
`libportaudio.so.2`. Ensure those libraries and `libstdc++.so.6` with
`GLIBCXX_3.4.22` or newer are installed before starting the service. Stock
Ubuntu 16.04 does not meet this loader requirement.

On Ubuntu 24.04, install the ALSA runtime with:

```shell
apt-get update
apt-get install jq libasound2t64 libportaudio2
```

`ffmpeg` is optional for live capture but required to run Madmom against audio
files, including its packaged smoke-test workflow.

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

## Target qualification

On a clean systemd host, the packaged qualification script installs the exact
unit and release tree temporarily, verifies HTTP readiness, a persisted clean
restart, `Restart=on-failure` after `SIGKILL`, and a clean `SIGTERM` stop, then
removes its test-owned service account, unit, install tree, and state. It
refuses to run if any production path, account, or port is already in use.

Before installation, the unprivileged runtime qualifier can exercise an
extracted release in place. It runs concurrent HTTP reads while sampling the
read-only `/api/maintenance/runtime` snapshot, verifies useful operator cadence
without exceeding the 400 Hz cap, bounds resident-memory growth, and requires a
clean `SIGTERM`. All test configuration and logs live in a temporary directory
that the script removes. A 60-second check is the default:

```shell
bash deploy/linux/qualify-runtime.sh "$PWD"
```

Pass a duration, unused port, and worker count for a longer target-host soak.
For example, this runs eight hours with eight HTTP workers and allows at most
128 MiB of resident-memory growth after warmup:

```shell
SPECTRUM_MAX_RSS_GROWTH_KB=131072 \
  bash deploy/linux/qualify-runtime.sh "$PWD" 28800 18081 8
```

The minimum and maximum accepted cadence default to 30 and 425 FPS. Set
`SPECTRUM_MIN_OPERATOR_FPS` or `SPECTRUM_MAX_OPERATOR_FPS` when the target has a
measured, deployment-specific performance envelope.

For the separate installed-service/systemd check, run:

```shell
sudo bash deploy/linux/qualify-systemd.sh "$PWD"
```

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

OPC output does not require the controller to be online when Spectrum starts.
Connection attempts and retries are non-blocking, and the newest flushed frame
remains pending until the controller is reachable. A controller or show network
that starts late therefore does not require restarting the Spectrum service.

For a USB serial wand receiver, add the service account to the group that owns
the device (commonly `dialout`) and restart the service:

```shell
usermod -aG dialout spectrum
systemctl restart spectrum.service
```

The maintenance page prefers persistent `/dev/serial/by-id/...` device names.
When a receiver does not publish a hardware serial number it falls back to a
`/dev/serial/by-path/...` name, then to transient kernel names such as
`/dev/ttyACM0`. Select a persistent alias when one is offered so reconnects and
reboots do not silently redirect the saved setting to a different USB device.

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
