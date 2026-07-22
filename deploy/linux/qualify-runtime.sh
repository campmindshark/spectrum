#!/usr/bin/env bash

# Exercise an extracted Spectrum Linux release without installing it. This is
# intentionally safe to run as an unprivileged user: all state stays in a
# test-owned temporary directory and the script only signals the process it
# starts. A short run belongs in CI; a longer run on the deployment host turns
# the same checks into a soak test.

set -euo pipefail

if (($# < 1 || $# > 4)); then
  printf 'Usage: deploy/linux/qualify-runtime.sh PUBLISHED_DIR [DURATION_SECONDS] [PORT] [HTTP_WORKERS]\n' >&2
  exit 2
fi

publish_directory_input=$1
duration_seconds=${2:-60}
port=${3:-18081}
http_workers=${4:-4}
minimum_operator_fps=${SPECTRUM_MIN_OPERATOR_FPS:-30}
maximum_operator_fps=${SPECTRUM_MAX_OPERATOR_FPS:-425}
maximum_rss_growth_kb=${SPECTRUM_MAX_RSS_GROWTH_KB:-262144}

require_integer() {
  local name=$1
  local value=$2
  local minimum=$3
  local maximum=$4
  if [[ ! "$value" =~ ^[0-9]+$ ]] ||
     ((value < minimum || value > maximum)); then
    printf '%s must be an integer from %s through %s.\n' \
      "$name" "$minimum" "$maximum" >&2
    exit 2
  fi
}

require_integer DURATION_SECONDS "$duration_seconds" 10 86400
require_integer PORT "$port" 1 65535
require_integer HTTP_WORKERS "$http_workers" 1 32
require_integer SPECTRUM_MIN_OPERATOR_FPS "$minimum_operator_fps" 1 10000
require_integer SPECTRUM_MAX_OPERATOR_FPS "$maximum_operator_fps" 1 10000
require_integer SPECTRUM_MAX_RSS_GROWTH_KB \
  "$maximum_rss_growth_kb" 0 16777216

if ((minimum_operator_fps > maximum_operator_fps)); then
  printf 'SPECTRUM_MIN_OPERATOR_FPS cannot exceed SPECTRUM_MAX_OPERATOR_FPS.\n' >&2
  exit 2
fi
for command_name in realpath curl ss awk sed grep mktemp; do
  if ! command -v "$command_name" >/dev/null; then
    printf 'Runtime qualification requires %s.\n' "$command_name" >&2
    exit 2
  fi
done

publish_directory=$(realpath -- "$publish_directory_input")
host_executable="$publish_directory/Spectrum.Host"
if [[ ! -x "$host_executable" ]]; then
  printf 'Published host is missing or not executable: %s\n' \
    "$host_executable" >&2
  exit 2
fi
if ss -ltn "sport = :$port" | grep -q LISTEN; then
  printf 'Refusing to use busy TCP port %s.\n' "$port" >&2
  exit 2
fi

qualification_directory=$(mktemp -d -t spectrum-runtime-qualify.XXXXXX)
data_directory="$qualification_directory/data"
log_file="$qualification_directory/host.log"
ownership_marker="$qualification_directory/.spectrum-runtime-qualification-owned"
touch "$ownership_marker"
host_pid=
load_pids=()

cleanup() {
  local result=$?
  trap - EXIT
  set +e

  for load_pid in "${load_pids[@]}"; do
    kill -TERM "$load_pid" >/dev/null 2>&1
    wait "$load_pid" >/dev/null 2>&1
  done
  if [[ -n "$host_pid" ]] && kill -0 "$host_pid" 2>/dev/null; then
    kill -TERM "$host_pid" >/dev/null 2>&1
    for _ in {1..50}; do
      kill -0 "$host_pid" 2>/dev/null || break
      sleep 0.1
    done
    if kill -0 "$host_pid" 2>/dev/null; then
      kill -KILL "$host_pid" >/dev/null 2>&1
    fi
    wait "$host_pid" >/dev/null 2>&1
  fi

  if ((result != 0)) && [[ -f "$log_file" ]]; then
    printf '\nSpectrum runtime qualification failed; host log follows:\n' >&2
    tail -n 100 "$log_file" >&2
  fi
  if [[ -f "$ownership_marker" ]]; then
    rm -rf -- "$qualification_directory"
  fi
  exit "$result"
}
trap cleanup EXIT

base_url="http://127.0.0.1:$port"
"$host_executable" \
  --data-dir "$data_directory" \
  --port "$port" \
  >"$log_file" 2>&1 &
host_pid=$!

ready=false
for _ in {1..60}; do
  if curl --fail --silent --max-time 2 \
      "$base_url/api/operator" >/dev/null; then
    ready=true
    break
  fi
  if ! kill -0 "$host_pid" 2>/dev/null; then
    printf 'Spectrum exited before becoming HTTP-ready.\n' >&2
    exit 1
  fi
  sleep 0.5
done
if [[ "$ready" != true ]]; then
  printf 'Spectrum did not become HTTP-ready.\n' >&2
  exit 1
fi

load_worker() {
  local worker_deadline=$((SECONDS + duration_seconds))
  while ((SECONDS < worker_deadline)); do
    curl --fail --silent --show-error --max-time 5 \
      --retry 2 --retry-all-errors \
      "$base_url/api/parameters" >/dev/null
    curl --fail --silent --show-error --max-time 5 \
      --retry 2 --retry-all-errors \
      "$base_url/api/layers" >/dev/null
    curl --fail --silent --show-error --max-time 5 \
      --retry 2 --retry-all-errors \
      "$base_url/api/maintenance/audio" >/dev/null
    sleep 0.05
  done
}

printf 'Running %ss runtime qualification with %s HTTP workers...\n' \
  "$duration_seconds" "$http_workers"
for ((worker = 0; worker < http_workers; worker++)); do
  load_worker &
  load_pids+=("$!")
done

sample_deadline=$((SECONDS + duration_seconds))
sample_count=0
fps_total=0
minimum_observed_fps=0
maximum_observed_fps=0
baseline_rss_kb=0
maximum_rss_kb=0

while ((SECONDS < sample_deadline)); do
  if ! kill -0 "$host_pid" 2>/dev/null; then
    printf 'Spectrum exited during runtime qualification.\n' >&2
    exit 1
  fi
  runtime_state=$(curl --fail --silent --max-time 5 \
    "$base_url/api/maintenance/runtime")
  if ! grep --quiet --fixed-strings '"enabled":true' <<<"$runtime_state"; then
    printf 'Runtime health reported a stopped engine: %s\n' \
      "$runtime_state" >&2
    exit 1
  fi
  if ! grep --quiet --fixed-strings \
      '"layerPlanError":null' <<<"$runtime_state"; then
    printf 'Runtime health reported a layer-plan failure: %s\n' \
      "$runtime_state" >&2
    exit 1
  fi
  operator_fps=$(sed -n \
    's/.*"operatorFps":\([0-9][0-9]*\).*/\1/p' <<<"$runtime_state")
  if [[ -z "$operator_fps" ]]; then
    printf 'Runtime health response omitted operatorFps: %s\n' \
      "$runtime_state" >&2
    exit 1
  fi

  # The first telemetry window may not have closed yet. Once it has, every
  # observed value must show useful progress without exceeding the 400 Hz cap
  # beyond a small measurement tolerance.
  if ((operator_fps > 0)); then
    if ((operator_fps < minimum_operator_fps ||
        operator_fps > maximum_operator_fps)); then
      printf 'Operator FPS %s fell outside the allowed range %s..%s.\n' \
        "$operator_fps" "$minimum_operator_fps" \
        "$maximum_operator_fps" >&2
      exit 1
    fi
    if ((sample_count == 0 || operator_fps < minimum_observed_fps)); then
      minimum_observed_fps=$operator_fps
    fi
    if ((operator_fps > maximum_observed_fps)); then
      maximum_observed_fps=$operator_fps
    fi
    fps_total=$((fps_total + operator_fps))
    sample_count=$((sample_count + 1))
  fi

  rss_kb=$(awk '/^VmRSS:/ { print $2; exit }' "/proc/$host_pid/status")
  if [[ -n "$rss_kb" ]]; then
    if ((baseline_rss_kb == 0 && operator_fps > 0)); then
      baseline_rss_kb=$rss_kb
    fi
    if ((rss_kb > maximum_rss_kb)); then
      maximum_rss_kb=$rss_kb
    fi
  fi
  sleep 1
done

load_failed=false
for load_pid in "${load_pids[@]}"; do
  if ! wait "$load_pid"; then
    load_failed=true
  fi
done
load_pids=()
if [[ "$load_failed" == true ]]; then
  printf 'One or more HTTP load workers failed.\n' >&2
  exit 1
fi
if ((sample_count < 2)); then
  printf 'Too few nonzero operator-FPS samples were observed: %s.\n' \
    "$sample_count" >&2
  exit 1
fi
if ((baseline_rss_kb == 0)); then
  printf 'No resident-memory samples were available for PID %s.\n' \
    "$host_pid" >&2
  exit 1
fi

rss_growth_kb=$((maximum_rss_kb - baseline_rss_kb))
if ((rss_growth_kb < 0)); then
  rss_growth_kb=0
fi
if ((rss_growth_kb > maximum_rss_growth_kb)); then
  printf 'Resident memory grew by %s KiB; limit is %s KiB.\n' \
    "$rss_growth_kb" "$maximum_rss_growth_kb" >&2
  exit 1
fi

printf 'Testing graceful SIGTERM shutdown...\n'
kill -TERM "$host_pid"
for _ in {1..100}; do
  kill -0 "$host_pid" 2>/dev/null || break
  sleep 0.1
done
if kill -0 "$host_pid" 2>/dev/null; then
  printf 'Spectrum did not stop within 10 seconds of SIGTERM.\n' >&2
  exit 1
fi
if ! wait "$host_pid"; then
  printf 'Spectrum returned a failure status after SIGTERM.\n' >&2
  exit 1
fi
host_pid=
grep --quiet --fixed-strings \
  'Stopping Spectrum headless host...' "$log_file"

average_fps=$((fps_total / sample_count))
printf 'Spectrum runtime qualification: PASS\n'
printf '  operator FPS min/avg/max: %s/%s/%s (%s samples)\n' \
  "$minimum_observed_fps" "$average_fps" "$maximum_observed_fps" \
  "$sample_count"
printf '  RSS baseline/max/growth: %s/%s/%s KiB\n' \
  "$baseline_rss_kb" "$maximum_rss_kb" "$rss_growth_kb"
printf '  concurrent HTTP workers: %s\n' "$http_workers"
