# Spectrum Linux service

The self-contained `linux-x64` archive runs the browser-controlled headless
host without requiring a system-wide .NET installation. Audio and MIDI inputs
are intentionally disabled in this initial Linux host; the browser simulator,
OPC output, UDP orientation input, and USB serial wand input remain available.

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

The headless MVP does not need audio or MIDI groups. For a USB serial wand
receiver, add the service account to the group that owns the device (commonly
`dialout`) and restart the service:

```shell
usermod -aG dialout spectrum
systemctl restart spectrum.service
```

When native Linux audio support is added, the account may also need membership
in the distribution's `audio` group or session-specific PipeWire access.

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
