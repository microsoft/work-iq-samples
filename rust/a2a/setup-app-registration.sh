#!/usr/bin/env bash
set -euo pipefail

# ── Work IQ A2A CLI — Azure AD App Registration Setup ────────────────────
# Creates a single-tenant public client app registration for device code
# flow authentication against Microsoft Graph / Work IQ.
#
# Prerequisites: az CLI, logged in (az login)
# Usage: ./setup-app-registration.sh

DISPLAY_NAME="Work IQ A2A CLI"
SIGN_IN_AUDIENCE="AzureADMyOrg"
GRAPH_API="00000003-0000-0000-c000-000000000000"

# Microsoft Graph delegated permission GUIDs
USER_READ="e1fe6dd8-ba31-4d61-89e7-88639da4683d"

echo "── Creating app registration: $DISPLAY_NAME ──"

APP_ID=$(az ad app create \
    --display-name "$DISPLAY_NAME" \
    --sign-in-audience "$SIGN_IN_AUDIENCE" \
    --is-fallback-public-client true \
    --query appId -o tsv)

echo "   App ID: $APP_ID"

echo "── Adding Graph API delegated permissions ──"

az ad app permission add --id "$APP_ID" --api "$GRAPH_API" \
    --api-permissions \
        "${USER_READ}=Scope"

echo "   Permissions added:"
echo "     - User.Read"

echo "── Creating service principal ──"

az ad sp create --id "$APP_ID" --query id -o tsv > /dev/null 2>&1 || true

echo "── Granting admin consent ──"

GRAPH_SP_ID=$(az ad sp show --id "$GRAPH_API" --query id -o tsv)
APP_SP_ID=$(az ad sp show --id "$APP_ID" --query id -o tsv)

az rest --method POST \
    --uri "https://graph.microsoft.com/v1.0/oauth2PermissionGrants" \
    --body "{
        \"clientId\": \"$APP_SP_ID\",
        \"consentType\": \"AllPrincipals\",
        \"resourceId\": \"$GRAPH_SP_ID\",
        \"scope\": \"User.Read\"
    }" -o none

echo ""
echo "── Done ──"
echo "App ID: $APP_ID"
echo ""
echo "Use with:  cargo run -- --appid $APP_ID"
echo "Or set:    export WORKIQ_APP_ID=$APP_ID"
