#!/usr/bin/env bash
set -euo pipefail

# ── A2A Chat — Azure AD App Registration Setup ──────────────────────────
# Creates a single-tenant app registration with the required Graph API
# delegated permissions for the A2A Chat sample app.
#
# Prerequisites: az CLI, logged in (az login)
# Usage: ./setup-app-registration.sh

DISPLAY_NAME="A2A Chat"
REDIRECT_URI="msauth.app.blueglass.A2A-Chat://auth"
SIGN_IN_AUDIENCE="AzureADMyOrg"
GRAPH_API="00000003-0000-0000-c000-000000000000"

# Microsoft Graph delegated permission GUIDs
USER_READ="e1fe6dd8-ba31-4d61-89e7-88639da4683d"
SITES_READ_ALL="205e70e5-aba6-4c52-a976-6d2d46c48043"
MAIL_READ="570282fd-fa5c-430d-a7fd-fc8dc98a9dca"
PEOPLE_READ_ALL="b89f9189-71a5-4e70-b041-9887f0bc7e4a"
ONLINE_MEETING_TRANSCRIPT_READ_ALL="30b87d18-ebb1-45db-97f8-82ccb1f0190c"
CHAT_READ="f501c180-9344-439a-bca0-6cbf209fd270"
CHANNEL_MESSAGE_READ_ALL="767156cb-16ae-4d10-8f8b-41b657c8c8c8"
EXTERNAL_ITEM_READ_ALL="922f9392-b1b7-483c-a4be-0089be7704fb"

echo "── Creating app registration: $DISPLAY_NAME ──"

APP_ID=$(az ad app create \
    --display-name "$DISPLAY_NAME" \
    --public-client-redirect-uris "$REDIRECT_URI" \
    --sign-in-audience "$SIGN_IN_AUDIENCE" \
    --query appId -o tsv)

echo "   App ID: $APP_ID"

echo "── Adding Graph API delegated permissions ──"

az ad app permission add --id "$APP_ID" --api "$GRAPH_API" \
    --api-permissions \
        "${USER_READ}=Scope" \
        "${SITES_READ_ALL}=Scope" \
        "${MAIL_READ}=Scope" \
        "${PEOPLE_READ_ALL}=Scope" \
        "${ONLINE_MEETING_TRANSCRIPT_READ_ALL}=Scope" \
        "${CHAT_READ}=Scope" \
        "${CHANNEL_MESSAGE_READ_ALL}=Scope" \
        "${EXTERNAL_ITEM_READ_ALL}=Scope"

echo "   Permissions added:"
echo "     - User.Read"
echo "     - Sites.Read.All"
echo "     - Mail.Read"
echo "     - People.Read.All"
echo "     - OnlineMeetingTranscript.Read.All"
echo "     - Chat.Read"
echo "     - ChannelMessage.Read.All"
echo "     - ExternalItem.Read.All"

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
        \"scope\": \"User.Read Sites.Read.All Mail.Read People.Read.All OnlineMeetingTranscript.Read.All Chat.Read ChannelMessage.Read.All ExternalItem.Read.All\"
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
