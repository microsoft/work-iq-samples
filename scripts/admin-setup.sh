#!/usr/bin/env bash
set -euo pipefail

# Work IQ Samples — App Registration Setup (Admin script)
#
# Creates a public-client Entra app registration with the permissions the
# .NET samples need (Work IQ Gateway). Idempotent: re-running against an
# existing app name updates it (after confirmation) instead of creating
# a duplicate.
#
# Requires: Azure CLI, logged in as tenant admin (az login).
#
# Usage:
#   ./admin-setup.sh [--name <NAME>] [--tenant <TENANT_ID>]
#                    [--multi-tenant] [--dry-run] [--verbose] [-h|--help]
#
# Defaults: name "Work IQ Samples Client", single-tenant.
#
# For the Swift iOS sample, use swift/a2a/setup-app-registration.sh
# (needs an iOS-specific redirect URI).

# ── Defaults ────────────────────────────────────────────────────────────
DISPLAY_NAME="Work IQ Samples Client"
SIGN_IN_AUDIENCE="AzureADMyOrg"   # single-tenant
TENANT_ID=""
DRY_RUN="false"
VERBOSE="false"

# ── Well-known IDs ──────────────────────────────────────────────────────
WORKIQ_APP_ID="fdcc1f02-fc51-4226-8753-f668596af7f7"
WORKIQ_AGENT_ASK_SCOPE_ID="0b1715fd-f4bf-4c63-b16d-5be31f9847c2"

# ── CLI parsing ─────────────────────────────────────────────────────────
show_help() {
  sed -n '3,19p' "$0" | sed 's/^# \{0,1\}//'
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --multi-tenant) SIGN_IN_AUDIENCE="AzureADMultipleOrgs"; shift ;;
    --name) DISPLAY_NAME="$2"; shift 2 ;;
    --tenant) TENANT_ID="$2"; shift 2 ;;
    --dry-run) DRY_RUN="true"; shift ;;
    --verbose) VERBOSE="true"; shift ;;
    -h|--help) show_help; exit 0 ;;
    *) echo "Unknown flag: $1" >&2; show_help; exit 1 ;;
  esac
done

run() {
  if [[ "$VERBOSE" == "true" ]]; then echo "+ $*" >&2; fi
  if [[ "$DRY_RUN" == "true" ]]; then echo "[dry-run] $*"; return 0; fi
  "$@"
}

# ── Preflight ───────────────────────────────────────────────────────────
if ! command -v az >/dev/null 2>&1; then
  echo "ERROR: Azure CLI not found. Install from https://aka.ms/azcli" >&2
  exit 1
fi

if ! az account show >/dev/null 2>&1; then
  echo "ERROR: Not logged in to Azure CLI. Run: az login" >&2
  exit 1
fi

CURRENT_TENANT=$(az account show --query tenantId -o tsv)
if [[ -n "$TENANT_ID" && "$TENANT_ID" != "$CURRENT_TENANT" ]]; then
  echo "ERROR: --tenant $TENANT_ID doesn't match the current az context ($CURRENT_TENANT)." >&2
  echo "       Run: az login --tenant $TENANT_ID" >&2
  exit 1
fi
TENANT_ID="$CURRENT_TENANT"
CURRENT_USER=$(az account show --query user.name -o tsv)

echo "Work IQ Samples — Admin Setup"
echo "  Tenant:         $TENANT_ID"
echo "  Signed in as:   $CURRENT_USER"
echo "  App name:       $DISPLAY_NAME"
echo "  Sign-in audience: $SIGN_IN_AUDIENCE"
if [[ "$DRY_RUN" == "true" ]]; then echo "  [DRY RUN — no changes will be made]"; fi
echo ""

# ── Step 1: Work IQ SP JIT-provisioning ─────────────────────────────────
echo "[1/6] Ensuring Work IQ service principal exists in tenant..."
# Idempotent: harmless if it already exists
run az ad sp create --id "$WORKIQ_APP_ID" >/dev/null 2>&1 || true
echo "      OK"

# ── Step 2: Create or reuse app registration ────────────────────────────
echo "[2/6] Creating app registration..."
EXISTING_APP_ID=$(az ad app list --display-name "$DISPLAY_NAME" --query "[0].appId" -o tsv 2>/dev/null || true)
if [[ -n "$EXISTING_APP_ID" ]]; then
  echo "      Found existing app '$DISPLAY_NAME' with App ID: $EXISTING_APP_ID"
  read -r -p "      Reuse and update it? (y/N): " confirm
  if [[ "$confirm" != "y" && "$confirm" != "Y" ]]; then
    echo "Aborted. Use --name to pick a different display name." >&2
    exit 1
  fi
  APP_ID="$EXISTING_APP_ID"
  run az ad app update --id "$APP_ID" \
    --sign-in-audience "$SIGN_IN_AUDIENCE" \
    --is-fallback-public-client true \
    >/dev/null
else
  APP_ID=$(run az ad app create \
    --display-name "$DISPLAY_NAME" \
    --sign-in-audience "$SIGN_IN_AUDIENCE" \
    --is-fallback-public-client true \
    --query appId -o tsv)
fi
echo "      App ID: $APP_ID"

# ── Step 3: Create SP for the app itself ────────────────────────────────
echo "[3/6] Creating service principal for the app..."
run az ad sp create --id "$APP_ID" >/dev/null 2>&1 || true
echo "      OK"

# ── Step 4: Public-client redirect URIs ─────────────────────────────────
# Three URIs needed for the .NET samples:
#   1. http://localhost                                         — browser interactive fallback
#   2. https://login.microsoftonline.com/common/oauth2/nativeclient — legacy MSAL native
#   3. ms-appx-web://microsoft.aad.brokerplugin/<app-id>        — WAM broker on Windows
echo "[4/6] Configuring public-client redirect URIs..."
run az ad app update --id "$APP_ID" \
  --public-client-redirect-uris \
    "http://localhost" \
    "https://login.microsoftonline.com/common/oauth2/nativeclient" \
    "ms-appx-web://microsoft.aad.brokerplugin/$APP_ID" \
  >/dev/null
echo "      OK"

# ── Step 5: Delegated permissions ───────────────────────────────────────
echo "[5/6] Adding delegated permissions..."
run az ad app permission add --id "$APP_ID" --api "$WORKIQ_APP_ID" \
  --api-permissions "${WORKIQ_AGENT_ASK_SCOPE_ID}=Scope" \
  2>/dev/null || true
echo "      Work IQ: WorkIQAgent.Ask"

# ── Step 6: Admin consent (direct Microsoft Graph API, idempotent) ─────
# We deliberately do NOT use 'az ad app permission admin-consent' — it uses
# the legacy AAD Graph endpoint and silently no-ops for delegated scopes on
# fresh apps. Instead we GET existing grants, PATCH if found, POST if not.
#
# The Work IQ SP may take a few seconds to become queryable after JIT, so
# we retry the SP lookups briefly.
ensure_grant() {
  local client_sp_id="$1"
  local resource_sp_id="$2"
  local scopes="$3"
  if [[ "$DRY_RUN" == "true" ]]; then
    echo "[dry-run] ensure grant: client=$client_sp_id resource=$resource_sp_id scope='$scopes'"
    return 0
  fi
  local existing_id
  existing_id=$(az rest --method GET \
    --url "https://graph.microsoft.com/v1.0/oauth2PermissionGrants?\$filter=clientId eq '$client_sp_id' and resourceId eq '$resource_sp_id'" \
    --query "value[0].id" -o tsv 2>/dev/null || true)
  if [[ -n "$existing_id" && "$existing_id" != "None" ]]; then
    az rest --method PATCH \
      --uri "https://graph.microsoft.com/v1.0/oauth2PermissionGrants/$existing_id" \
      --headers "Content-Type=application/json" \
      --body "{\"scope\": \"$scopes\"}" -o none
  else
    az rest --method POST \
      --uri "https://graph.microsoft.com/v1.0/oauth2PermissionGrants" \
      --headers "Content-Type=application/json" \
      --body "{\"clientId\": \"$client_sp_id\", \"consentType\": \"AllPrincipals\", \"resourceId\": \"$resource_sp_id\", \"scope\": \"$scopes\"}" -o none
  fi
}

resolve_sp_id() {
  local app_id="$1"
  if [[ "$DRY_RUN" == "true" ]]; then echo "<sp-id-for-$app_id>"; return 0; fi
  local sp_id
  for _ in 1 2 3 4 5; do
    sp_id=$(az ad sp show --id "$app_id" --query id -o tsv 2>/dev/null || true)
    if [[ -n "$sp_id" ]]; then echo "$sp_id"; return 0; fi
    sleep 2
  done
  echo "ERROR: could not resolve SP object ID for app $app_id after 10s" >&2
  return 1
}

echo "[6/6] Granting admin consent (direct Graph API)..."
APP_SP_ID=$(resolve_sp_id "$APP_ID")
WORKIQ_SP_ID=$(resolve_sp_id "$WORKIQ_APP_ID")
ensure_grant "$APP_SP_ID" "$WORKIQ_SP_ID" "WorkIQAgent.Ask"
echo "      Work IQ: consent granted"

# ── Summary ─────────────────────────────────────────────────────────────
echo ""
echo "── Done ──"
echo ""
echo "Give these to your developer:"
echo "  APP_ID:    $APP_ID"
echo "  TENANT_ID: $TENANT_ID"
echo ""
echo "Test commands:"
echo "  # Work IQ REST (Copilot Chat via Work IQ Gateway):"
echo "  cd dotnet/rest && dotnet run -- --token WAM \\"
echo "      --appid $APP_ID --tenant $TENANT_ID"
echo ""
echo "  # Work IQ A2A:"
echo "  cd dotnet/a2a && dotnet run -- --token WAM \\"
echo "      --appid $APP_ID --tenant $TENANT_ID"
