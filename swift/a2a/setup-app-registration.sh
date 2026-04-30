#!/usr/bin/env bash
set -euo pipefail

# ── A2A Chat — Azure AD App Registration Setup ──────────────────────────
# Creates a single-tenant app registration with the iOS redirect URI and
# the WorkIQAgent.Ask delegated permission needed by the A2A Chat app
# against the Work IQ Gateway.
#
# Prerequisites: az CLI, logged in (az login)
# Usage: ./setup-app-registration.sh
#
# For multi-language testing (this sample + .NET samples), run the
# unified ../../scripts/admin-setup.sh first, then re-run this script
# (or add the iOS redirect URI manually) — only the redirect URI is
# iOS-specific.

DISPLAY_NAME="A2A Chat"
REDIRECT_URI="msauth.app.blueglass.A2A-Chat://auth"
SIGN_IN_AUDIENCE="AzureADMyOrg"

# Work IQ resource app
WORKIQ_APP_ID="fdcc1f02-fc51-4226-8753-f668596af7f7"
WORKIQ_AGENT_ASK_SCOPE_ID="0b1715fd-f4bf-4c63-b16d-5be31f9847c2"
WORKIQ_ENDPOINT="https://workiq.svc.cloud.microsoft/a2a/"
WORKIQ_SCOPE="api://workiq.svc.cloud.microsoft/.default"

echo "── Ensuring Work IQ service principal exists in tenant ──"
az ad sp create --id "$WORKIQ_APP_ID" >/dev/null 2>&1 || true

echo "── Creating app registration: $DISPLAY_NAME ──"

APP_ID=$(az ad app create \
    --display-name "$DISPLAY_NAME" \
    --public-client-redirect-uris "$REDIRECT_URI" \
    --sign-in-audience "$SIGN_IN_AUDIENCE" \
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
echo "── Generating Configuration.plist ──"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cat > "$SCRIPT_DIR/A2A Chat/Configuration.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>ClientId</key>
    <string>$APP_ID</string>
    <key>RedirectUri</key>
    <string>$REDIRECT_URI</string>
    <key>TenantId</key>
    <string>common</string>
    <key>Scopes</key>
    <array>
        <string>$WORKIQ_SCOPE</string>
    </array>
    <key>Endpoint</key>
    <string>$WORKIQ_ENDPOINT</string>
</dict>
</plist>
PLIST

echo "   Created: A2A Chat/Configuration.plist"

echo ""
echo "── Done ──"
echo "App ID: $APP_ID"
echo "Redirect URI: $REDIRECT_URI"
echo ""
echo "Configuration.plist created. Build and run in Xcode."
