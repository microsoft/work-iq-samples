#!/usr/bin/env bash
set -euo pipefail

# Work IQ Samples — End-to-End Test Script
#
# Creates a fresh app registration via admin-setup.sh, then runs each
# .NET sample across the (mode) matrix against the Work IQ Gateway and
# deletes the app on exit. Each invocation is interactive: send a
# message when prompted, type 'quit' to move on to the next.
#
# Requires: Azure CLI (logged in), .NET 10 SDK.
#
# Usage:
#   scripts/test-e2e.sh --account user@tenant.com
#   scripts/test-e2e.sh --account user@tenant.com --modes sync
#   scripts/test-e2e.sh --account user@tenant.com --samples rest,a2a --keep-app
#
# Flags:
#   --account <email>    Required. The user signing in via WAM.
#   --samples <list>     Comma-separated: rest,a2a,a2a-raw (default: all)
#   --modes <list>       Comma-separated: sync,stream (default: both)
#   --name <name>        App display name (default: "WIQ E2E Test <timestamp>")
#   --tenant <id>        Tenant ID (auto-detected from az account show)
#   --skip-build         Skip `dotnet build` (assume already built)
#   --skip-setup         Skip admin-setup; use --appid for an existing app
#   --appid <guid>       Existing App ID (requires --skip-setup)
#   --keep-app           Don't delete the app on exit
#   --log-dir <path>     Where to write per-run logs (default: ./test-logs)
#   --agent-id <id>      Pass --agent-id to a2a + a2a-raw runs (REST ignores it)

# ── Defaults ────────────────────────────────────────────────────────────
ACCOUNT=""
SAMPLES="rest,a2a,a2a-raw"
MODES="sync,stream"
APP_NAME="WIQ E2E Test $(date +%Y%m%d-%H%M%S)"
TENANT_ID=""
SKIP_BUILD="false"
SKIP_SETUP="false"
APP_ID=""
KEEP_APP="false"
LOG_DIR="./test-logs"
AGENT_ID=""

# ── CLI parsing ─────────────────────────────────────────────────────────
show_help() { sed -n '3,29p' "$0" | sed 's/^# \{0,1\}//'; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --account) ACCOUNT="$2"; shift 2 ;;
    --samples) SAMPLES="$2"; shift 2 ;;
    --modes) MODES="$2"; shift 2 ;;
    --name) APP_NAME="$2"; shift 2 ;;
    --tenant) TENANT_ID="$2"; shift 2 ;;
    --skip-build) SKIP_BUILD="true"; shift ;;
    --skip-setup) SKIP_SETUP="true"; shift ;;
    --appid) APP_ID="$2"; shift 2 ;;
    --keep-app) KEEP_APP="true"; shift ;;
    --log-dir) LOG_DIR="$2"; shift 2 ;;
    --agent-id) AGENT_ID="$2"; shift 2 ;;
    -h|--help) show_help; exit 0 ;;
    *) echo "Unknown flag: $1" >&2; show_help; exit 1 ;;
  esac
done

# ── Preflight ───────────────────────────────────────────────────────────
if [[ -z "$ACCOUNT" ]]; then
  echo "ERROR: --account is required (e.g., user@contoso.com). Used for WAM login hint." >&2
  exit 1
fi

if ! command -v az >/dev/null 2>&1; then
  echo "ERROR: Azure CLI not found. Install from https://aka.ms/azcli" >&2
  exit 1
fi

if ! az account show >/dev/null 2>&1; then
  echo "ERROR: Not logged in to Azure CLI. Run: az login" >&2
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: dotnet CLI not found. Install .NET 10 SDK." >&2
  exit 1
fi

if [[ -z "$TENANT_ID" ]]; then
  TENANT_ID=$(az account show --query tenantId -o tsv)
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

if [[ "$SKIP_SETUP" == "true" && -z "$APP_ID" ]]; then
  echo "ERROR: --skip-setup requires --appid <existing-app-guid>" >&2
  exit 1
fi

mkdir -p "$LOG_DIR"

# ── Cleanup trap ────────────────────────────────────────────────────────
# Guard against re-entry: EXIT fires after INT's `exit`, which would call
# cleanup twice (and the second delete would fail with "doesn't exist").
CLEANUP_DONE="false"
cleanup() {
  local exit_code=${1:-$?}
  if [[ "$CLEANUP_DONE" == "true" ]]; then exit $exit_code; fi
  CLEANUP_DONE="true"
  echo ""
  echo "── Cleanup ──"
  if [[ "$KEEP_APP" == "true" ]]; then
    echo "  --keep-app: leaving app $APP_ID in place"
    echo "  Manual delete: az ad app delete --id $APP_ID"
  elif [[ -n "$APP_ID" && "$SKIP_SETUP" == "false" ]]; then
    echo "  Deleting app $APP_ID..."
    az ad app delete --id "$APP_ID" 2>&1 | tail -3 || echo "  (warn) delete failed; clean up manually"
  else
    echo "  (no app to delete — used --skip-setup)"
  fi
  exit $exit_code
}
trap cleanup EXIT
trap 'cleanup 130' INT TERM

# ── Header ──────────────────────────────────────────────────────────────
echo "Work IQ Samples — End-to-End Test"
echo "  Tenant:    $TENANT_ID"
echo "  Account:   $ACCOUNT"
echo "  Samples:   $SAMPLES"
echo "  Modes:     $MODES"
echo "  App name:  $APP_NAME"
echo "  Log dir:   $LOG_DIR"
echo ""

# ── Step 1: Create app registration (or use existing) ───────────────────
if [[ "$SKIP_SETUP" == "true" ]]; then
  echo "── Step 1: Using existing app $APP_ID (--skip-setup) ──"
else
  echo "── Step 1: Creating app registration via admin-setup.sh ──"
  "$SCRIPT_DIR/admin-setup.sh" --name "$APP_NAME" --tenant "$TENANT_ID" 2>&1 | tee "$LOG_DIR/00-admin-setup.log" | tail -30
  APP_ID=$(az ad app list --display-name "$APP_NAME" --query "[0].appId" -o tsv)
  if [[ -z "$APP_ID" ]]; then
    echo "ERROR: Could not look up created App ID by name '$APP_NAME'." >&2
    exit 1
  fi
  echo ""
  echo "  App ID: $APP_ID"
fi
echo ""

# ── Step 2: Build solution ──────────────────────────────────────────────
if [[ "$SKIP_BUILD" == "true" ]]; then
  echo "── Step 2: Skipping build (--skip-build) ──"
else
  echo "── Step 2: Building dotnet/WorkIQSamples.sln ──"
  (cd "$REPO_ROOT" && dotnet build dotnet/WorkIQSamples.sln --nologo -v q 2>&1 | tail -5)
fi
echo ""

# ── Step 3: Run matrix ──────────────────────────────────────────────────
# Parse comma-separated lists into bash arrays
IFS=',' read -ra SAMPLE_ARR <<<"$SAMPLES"
IFS=',' read -ra MODE_ARR <<<"$MODES"

# Pre-compute totals for nice counters
TOTAL=0
for s in "${SAMPLE_ARR[@]}"; do
  for m in "${MODE_ARR[@]}"; do
    TOTAL=$((TOTAL + 1))
  done
done

echo "── Step 3: Running sample matrix ($TOTAL runs) ──"
echo ""
echo "  INSTRUCTIONS:"
echo "    Each run starts a sample in interactive mode."
echo "    1. WAM will prompt on first sign-in. Pick account: $ACCOUNT"
echo "    2. Type a test message (e.g., 'what meetings do I have today?')"
echo "    3. Wait for the agent response."
echo "    4. Type 'quit' to exit and move to the next run."
echo "    5. Output from each run is logged to $LOG_DIR/"
echo ""
read -r -p "Press Enter to start the matrix, or Ctrl+C to abort..."
echo ""

RUN_IDX=0
FAILED=()

run_sample() {
  local sample="$1"
  local mode="$2"
  RUN_IDX=$((RUN_IDX + 1))
  local label="$RUN_IDX/$TOTAL  $sample / $mode"
  local log_file="$LOG_DIR/$(printf '%02d' $RUN_IDX)-${sample}-${mode}.log"

  # Streaming is not yet supported by the a2a / a2a-raw samples (coming soon).
  # Skip those combinations rather than failing the run.
  if [[ "$mode" == "stream" && ( "$sample" == "a2a" || "$sample" == "a2a-raw" ) ]]; then
    echo ""
    echo "  [$label]  SKIP — streaming responses are coming soon to this sample"
    return 0
  fi

  # Build the command per sample
  local project="$REPO_ROOT/dotnet/$sample"
  local stream_flag=""
  [[ "$mode" == "stream" ]] && stream_flag="--stream"

  # When --agent-id is provided, append it to a2a / a2a-raw commands (REST doesn't support it)
  local agent_flag=""
  [[ -n "$AGENT_ID" && "$sample" != "rest" ]] && agent_flag="--agent-id $AGENT_ID"

  local cmd
  case "$sample" in
    rest|a2a|a2a-raw)
      cmd=(dotnet run --project "$project" --
           --token WAM
           --appid "$APP_ID"
           --tenant "$TENANT_ID"
           --account "$ACCOUNT"
           $stream_flag
           $agent_flag)
      ;;
    *)
      echo "  SKIP: unknown sample '$sample'" >&2
      return 0
      ;;
  esac

  echo ""
  echo "─────────────────────────────────────────────────────"
  echo "  [$label]"
  echo "  Log: $log_file"
  echo "  (type 'quit' to end run; Ctrl+C skips this run)"
  echo "─────────────────────────────────────────────────────"

  # Install a local SIGINT trap so Ctrl+C kills ONLY the sample (dotnet
  # receives SIGINT via the terminal's foreground process group and exits);
  # the script itself keeps going. Without this, Ctrl+C would fire the
  # outer cleanup trap and abort the whole matrix.
  local sample_interrupted="false"
  trap 'sample_interrupted=true; echo ""; echo "  [Ctrl+C — skipping this run]"' INT

  # Run interactively; tee output to a log so we can review afterward.
  # `set +e` around the run so a sample failure doesn't abort the script.
  set +e
  "${cmd[@]}" 2>&1 | tee "$log_file"
  local rc=${PIPESTATUS[0]}
  set -e

  # Restore the script-level SIGINT trap (a second Ctrl+C between runs
  # aborts the whole script and triggers cleanup).
  trap 'cleanup 130' INT

  if [[ "$sample_interrupted" == "true" ]]; then
    FAILED+=("$label (interrupted)")
  elif [[ $rc -ne 0 ]]; then
    echo ""
    echo "  ⚠ Sample exited with code $rc (logged to $log_file)"
    FAILED+=("$label (exit $rc)")
  fi
}

for sample in "${SAMPLE_ARR[@]}"; do
  for mode in "${MODE_ARR[@]}"; do
    run_sample "$sample" "$mode"
  done
done

# ── Summary ─────────────────────────────────────────────────────────────
echo ""
echo "─────────────────────────────────────────────────────"
echo "  E2E Summary"
echo "─────────────────────────────────────────────────────"
echo "  Total runs:  $TOTAL"
echo "  Failures:    ${#FAILED[@]}"
if [[ ${#FAILED[@]} -gt 0 ]]; then
  for f in "${FAILED[@]}"; do echo "    - $f"; done
fi
echo "  Logs:        $LOG_DIR/"
echo ""
