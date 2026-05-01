#!/usr/bin/env bash
set -euo pipefail

# ── Work IQ A2A CLI — Azure AD App Registration Setup ────────────────────
# Creates a single-tenant public client app registration for the Rust
# sample's MSAL flow against the Work IQ Gateway.
#
# Prerequisites: az CLI, logged in (az login)
# Usage: ./setup-app-registration.sh
#
# For multi-language testing (this sample + .NET samples sharing one
# registration), use the unified ../../scripts/admin-setup.{sh,ps1}
# at the repo root instead.

DISPLAY_NAME="Work IQ A2A CLI"
SIGN_IN_AUDIENCE="AzureADMyOrg"

# Work IQ resource app
WORKIQ_APP_ID="fdcc1f02-fc51-4226-8753-f668596af7f7"
WORKIQ_AGENT_ASK_SCOPE_ID="0b1715fd-f4bf-4c63-b16d-5be31f9847c2"

echo "── Ensuring Work IQ service principal exists in tenant ──"
az ad sp create --id "$WORKIQ_APP_ID" >/dev/null 2>&1 || true

echo "── Creating app registration: $DISPLAY_NAME ──"

APP_ID=$(az ad app create \
    --display-name "$DISPLAY_NAME" \
    --sign-in-audience "$SIGN_IN_AUDIENCE" \
    --is-fallback-public-client true \
    --query appId -o tsv)

echo "   App ID: $APP_ID"

echo "── Adding WorkIQAgent.Ask delegated permission ──"

az ad app permission add --id "$APP_ID" --api "$WORKIQ_APP_ID" \
    --api-permissions "${WORKIQ_AGENT_ASK_SCOPE_ID}=Scope" \
    2>/dev/null || true

echo "── Creating service principal for the new app ──"
az ad sp create --id "$APP_ID" --query id -o tsv > /dev/null 2>&1 || true

echo "── Granting admin consent ──"
WORKIQ_SP_ID=$(az ad sp show --id "$WORKIQ_APP_ID" --query id -o tsv)
APP_SP_ID=$(az ad sp show --id "$APP_ID" --query id -o tsv)

az rest --method POST \
    --uri "https://graph.microsoft.com/v1.0/oauth2PermissionGrants" \
    --body "{
        \"clientId\": \"$APP_SP_ID\",
        \"consentType\": \"AllPrincipals\",
        \"resourceId\": \"$WORKIQ_SP_ID\",
        \"scope\": \"WorkIQAgent.Ask\"
    }" -o none

echo ""
echo "── Done ──"
echo "App ID: $APP_ID"
echo ""
echo "Use with:  cargo run -- --appid $APP_ID"
echo "Or set:    export WORKIQ_APP_ID=$APP_ID"
