#!/bin/sh
# Performance guard: fails when the Native AOT binary gets heavier or slower than its budget.
#
#   scripts/perf.sh <tinyprobe binary> <base URL of the test server>
#
# It measures the binary's size, its start-up (--version), the loading of a workflow of 500 steps
# (inspect), and a run of those 500 steps against the test server. Times are medians, so one slow
# run on a shared CI machine does not fail the check. The budgets leave room for slower machines:
# they catch a regression of several times, not noise. Raise one only as a choice, in the PR that
# needs it. Measured on 2026-10-28 (a laptop, linux-x64): 9.09 MB, 13 ms, 120 ms, 300 ms.
set -eu

BUDGET_SIZE_BYTES=10485760   # 10 MiB
BUDGET_STARTUP_MS=50
BUDGET_INSPECT_MS=500
BUDGET_RUN_MS=2000
STEPS=500

[ $# -eq 2 ] || { echo "usage: scripts/perf.sh <tinyprobe binary> <base URL>" >&2; exit 2; }
BIN=$1
BASE_URL=${2%/}
[ -x "$BIN" ] || { echo "perf: $BIN is not an executable" >&2; exit 2; }
[ "$(date +%N)" != "N" ] || { echo "perf: needs GNU date (date +%N)" >&2; exit 2; }

WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

# 500 calls with four checks each: the engine's cost per step, not the network's.
{
  printf 'name: perf\nbase_url: %s\nendpoints:\n  status: { path: "/api/status/${code}" }\nsteps:\n' "$BASE_URL"
  i=0
  while [ "$i" -lt "$STEPS" ]; do
    printf '  - call: status\n    with: { code: 200 }\n    expect:\n      status: 200\n'
    printf '      headers: { Content-Type: { contains: json } }\n'
    printf '      body: { $.status: 200, $.message: { contains: simulated } }\n'
    i=$((i + 1))
  done
} > "$WORK/perf.yaml"

# Median time in milliseconds of <runs> runs of the command.
median_ms() {
  runs=$1
  shift
  : > "$WORK/times"
  i=0
  while [ "$i" -lt "$runs" ]; do
    start=$(date +%s%N)
    "$@" > /dev/null 2>&1 || { echo "perf: '$*' failed" >&2; exit 1; }
    end=$(date +%s%N)
    echo $(((end - start) / 1000000)) >> "$WORK/times"
    i=$((i + 1))
  done
  sort -n "$WORK/times" | sed -n "$(((runs + 1) / 2))p"
}

size=$(wc -c < "$BIN" | tr -d ' ')
startup=$(median_ms 21 "$BIN" --version)
inspect=$(median_ms 5 "$BIN" inspect "$WORK/perf.yaml")
run=$(median_ms 3 "$BIN" run "$WORK/perf.yaml" --no-color)

failed=0
report=""
check() {
  name=$1 value=$2 budget=$3 unit=$4
  if [ "$value" -le "$budget" ]; then status="ok"; else status="OVER BUDGET"; failed=1; fi
  line=$(printf '%-34s %10s %-5s budget %10s %-5s %s' "$name" "$value" "$unit" "$budget" "$unit" "$status")
  echo "$line"
  report="$report| $name | $value $unit | $budget $unit | $status |
"
}

check "binary size" "$size" "$BUDGET_SIZE_BYTES" "bytes"
check "start-up (--version)" "$startup" "$BUDGET_STARTUP_MS" "ms"
check "inspect, $STEPS steps" "$inspect" "$BUDGET_INSPECT_MS" "ms"
check "run, $STEPS steps" "$run" "$BUDGET_RUN_MS" "ms"

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  {
    echo "### Performance"
    echo
    echo "| Measure | Value | Budget | |"
    echo "|---|---|---|---|"
    printf '%s' "$report"
  } >> "$GITHUB_STEP_SUMMARY"
fi

[ "$failed" -eq 0 ] || { echo "perf: over budget. If it is intended, raise the budget in scripts/perf.sh and say why in the PR." >&2; exit 1; }
