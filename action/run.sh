#!/usr/bin/env bash
# Runs tinyprobe for the GitHub Action (action.yml). Inputs come as environment variables, never pasted
# into the script, so a value cannot inject shell code.
#
#   TINYPROBE_BIN     the binary
#   INPUT_PATH        workflow files or folders, separated by spaces or new lines
#   INPUT_ENV_FILE    the environment file (-e), optional
#   INPUT_VARS        variables, one name=value per line (--var), optional
#   INPUT_JUNIT       the JUnit report to write
#
# It always exits 0 and writes `exit-code` to $GITHUB_OUTPUT: the report and the summary come first, and
# the last step of the action fails the job with that code.
set -uo pipefail

args=(run)
read -r -a paths <<< "$(printf '%s' "$INPUT_PATH" | tr '\n' ' ')"
[ "${#paths[@]}" -gt 0 ] || paths=(tinyprobe/workflows)
args+=("${paths[@]}")

[ -n "${INPUT_ENV_FILE:-}" ] && args+=(-e "$INPUT_ENV_FILE")

while IFS= read -r line; do
  line="${line#"${line%%[![:space:]]*}"}"
  [ -z "$line" ] || [ "${line#\#}" != "$line" ] && continue
  args+=(--var "$line")
done <<< "${INPUT_VARS:-}"

args+=(--junit "$INPUT_JUNIT")

# The command for the log, with the values of --var left out: they are often secrets.
shown=()
for ((i = 0; i < ${#args[@]}; i++)); do
  if [ "${args[$i]}" = "--var" ]; then
    shown+=(--var "${args[$((i + 1))]%%=*}=***")
    i=$((i + 1))
  else
    shown+=("${args[$i]}")
  fi
done
echo "tinyprobe ${shown[*]}"
"$TINYPROBE_BIN" "${args[@]}"
code=$?

echo "exit-code=$code" >> "${GITHUB_OUTPUT:-/dev/null}"
exit 0
