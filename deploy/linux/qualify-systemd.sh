#!/usr/bin/env bash

# Exercise the packaged Spectrum unit on a clean systemd host. The script
# deliberately uses the production paths and service name, refuses to touch
# pre-existing state, and removes only resources created by this run.

set -euo pipefail

service_name=spectrum.service
service_user=spectrum
install_directory=/opt/spectrum
state_directory=/var/lib/spectrum
unit_path=/etc/systemd/system/spectrum.service
port=8080

if (($# != 1)); then
  printf 'Usage: deploy/linux/qualify-systemd.sh PUBLISHED_SPECTRUM_DIR\n' >&2
  exit 2
fi
if [[ ${EUID:-$(id -u)} -ne 0 ]]; then
  printf 'systemd qualification must run as root.\n' >&2
  exit 2
fi
if [[ $(realpath -m -- "$install_directory") != /opt/spectrum ]] ||
   [[ $(realpath -m -- "$state_directory") != /var/lib/spectrum ]] ||
   [[ $(realpath -m -- "$unit_path") != /etc/systemd/system/spectrum.service ]]; then
  printf 'Refusing unexpected systemd qualification targets.\n' >&2
  exit 2
fi

publish_directory=$(realpath -- "$1")
host_executable="$publish_directory/Spectrum.Host"
source_unit="$publish_directory/deploy/linux/spectrum.service"
if [[ ! -f "$host_executable" ]]; then
  printf 'Published host is missing: %s\n' "$host_executable" >&2
  exit 2
fi
if [[ ! -f "$source_unit" ]]; then
  printf 'Published systemd unit is missing: %s\n' "$source_unit" >&2
  exit 2
fi
if [[ ! -d /run/systemd/system ]] ||
   [[ $(systemctl is-system-running 2>/dev/null || true) != running ]]; then
  printf 'systemd is not running on this host.\n' >&2
  exit 2
fi

for target in "$install_directory" "$state_directory" "$unit_path"; do
  if [[ -e "$target" ]]; then
    printf 'Refusing to overwrite pre-existing path: %s\n' "$target" >&2
    exit 2
  fi
done
if getent passwd "$service_user" >/dev/null; then
  printf 'Refusing to reuse pre-existing account: %s\n' "$service_user" >&2
  exit 2
fi
if ss -ltn "sport = :$port" | grep -q LISTEN; then
  printf 'Refusing to use busy TCP port %s.\n' "$port" >&2
  exit 2
fi

created_user=false
created_install=false
created_unit=false

cleanup() {
  local result=$?
  trap - EXIT
  set +e

  if ((result != 0)); then
    printf '\nSpectrum systemd qualification failed; recent journal follows:\n' >&2
    journalctl -u "$service_name" --no-pager -n 100 >&2
  fi

  if [[ "$created_unit" == true ]]; then
    systemctl disable --now "$service_name" >/dev/null 2>&1
    rm -f -- "$unit_path"
  fi
  systemctl daemon-reload >/dev/null 2>&1
  systemctl reset-failed "$service_name" >/dev/null 2>&1

  if [[ "$created_install" == true ]] &&
     [[ -f "$install_directory/.systemd-qualification-owned" ]]; then
    rm -rf -- "$install_directory"
  fi
  if [[ "$created_user" == true ]]; then
    # The path and account were verified absent before the test-created unit
    # could create StateDirectory=spectrum.
    rm -rf -- "$state_directory"
    userdel "$service_user" >/dev/null 2>&1
  fi

  exit "$result"
}
trap cleanup EXIT

wait_for_http() {
  local expected_pid=${1:-}
  local current_pid
  for _ in {1..60}; do
    current_pid=$(systemctl show --property MainPID --value "$service_name")
    if [[ -n "$expected_pid" ]] && [[ "$current_pid" == "$expected_pid" ]]; then
      sleep 0.5
      continue
    fi
    if [[ "$current_pid" != 0 ]] &&
       curl --fail --silent "http://127.0.0.1:$port/api/operator" \
         >/dev/null; then
      printf '%s\n' "$current_pid"
      return 0
    fi
    sleep 0.5
  done
  printf 'Spectrum did not become HTTP-ready.\n' >&2
  return 1
}

printf 'Creating isolated Spectrum service account and install tree...\n'
useradd \
  --system \
  --home-dir "$state_directory" \
  --shell /usr/sbin/nologin \
  "$service_user"
created_user=true

install -d -o root -g root -m 0755 "$install_directory"
created_install=true
touch "$install_directory/.systemd-qualification-owned"
cp -a -- "$publish_directory/." "$install_directory/"
chown -R root:root "$install_directory"
chmod 0755 "$install_directory/Spectrum.Host"
install -o root -g root -m 0644 "$source_unit" "$unit_path"
created_unit=true

systemctl daemon-reload
systemctl enable --now "$service_name"
initial_pid=$(wait_for_http)
systemctl is-active --quiet "$service_name"
systemctl is-enabled --quiet "$service_name"

audio_state=$(
  curl --fail --silent \
    "http://127.0.0.1:$port/api/maintenance/audio"
)
grep --quiet --fixed-strings '"backend":"ALSA"' <<<"$audio_state"

printf 'Testing graceful restart and persisted configuration...\n'
curl --fail --silent \
  --request PUT \
  --header 'Content-Type: application/json' \
  --data '{"value":0.125}' \
  "http://127.0.0.1:$port/api/parameters/domeGlobalFadeSpeed" \
  >/dev/null
systemctl restart "$service_name"
restart_pid=$(wait_for_http "$initial_pid")
grep --quiet --fixed-strings \
  '<domeGlobalFadeSpeed>0.125</domeGlobalFadeSpeed>' \
  "$state_directory/spectrum_config.xml"

printf 'Testing Restart=on-failure crash recovery...\n'
kill -KILL "$restart_pid"
recovered_pid=$(wait_for_http "$restart_pid")
if [[ "$recovered_pid" == "$restart_pid" ]]; then
  printf 'systemd did not replace the killed process.\n' >&2
  exit 1
fi
restart_count=$(
  systemctl show --property NRestarts --value "$service_name"
)
if [[ "$restart_count" -lt 1 ]]; then
  printf 'systemd did not record a service restart.\n' >&2
  exit 1
fi

printf 'Testing clean SIGTERM stop...\n'
systemctl stop "$service_name"
if [[ $(systemctl is-active "$service_name" || true) != inactive ]]; then
  printf 'Spectrum did not reach the inactive state.\n' >&2
  exit 1
fi
grep --quiet --fixed-strings \
  '<domeGlobalFadeSpeed>0.125</domeGlobalFadeSpeed>' \
  "$state_directory/spectrum_config.xml"
service_journal=$(journalctl -u "$service_name" --no-pager)
grep --quiet --fixed-strings \
  'Stopping Spectrum headless host...' <<<"$service_journal"

printf 'Spectrum systemd qualification: PASS\n'
printf '  initial PID: %s\n' "$initial_pid"
printf '  graceful-restart PID: %s\n' "$restart_pid"
printf '  crash-recovery PID: %s\n' "$recovered_pid"
printf '  systemd restarts: %s\n' "$restart_count"
