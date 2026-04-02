# ── A2A Chat — Azure AD App Registration Setup ──────────────────────────
# Creates a single-tenant app registration with the required Graph API
# delegated permissions for the A2A Chat sample app.
#
# Prerequisites: az CLI, logged in (az login)
# Usage: .\setup-app-registration.ps1

$ErrorActionPreference = "Stop"

$DisplayName = "A2A Chat"
$RedirectUri = "msauth.app.blueglass.A2A-Chat://auth"
$SignInAudience = "AzureADMyOrg"
$GraphApi = "00000003-0000-0000-c000-000000000000"

# Microsoft Graph delegated permission GUIDs
$Permissions = @{
    "User.Read"                        = "e1fe6dd8-ba31-4d61-89e7-88639da4683d"
    "Sites.Read.All"                   = "205e70e5-aba6-4c52-a976-6d2d46c48043"
    "Mail.Read"                        = "570282fd-fa5c-430d-a7fd-fc8dc98a9dca"
    "People.Read.All"                  = "b89f9189-71a5-4e70-b041-9887f0bc7e4a"
    "OnlineMeetingTranscript.Read.All" = "30b87d18-ebb1-45db-97f8-82ccb1f0190c"
    "Chat.Read"                        = "f501c180-9344-439a-bca0-6cbf209fd270"
    "ChannelMessage.Read.All"          = "767156cb-16ae-4d10-8f8b-41b657c8c8c8"
    "ExternalItem.Read.All"            = "922f9392-b1b7-483c-a4be-0089be7704fb"
}

Write-Host "── Creating app registration: $DisplayName ──"

$AppId = az ad app create `
    --display-name $DisplayName `
    --public-client-redirect-uris $RedirectUri `
    --sign-in-audience $SignInAudience `
    --query appId -o tsv

Write-Host "   App ID: $AppId"

Write-Host "── Adding Graph API delegated permissions ──"

$PermArgs = $Permissions.Values | ForEach-Object { "$_=Scope" }

az ad app permission add --id $AppId --api $GraphApi `
    --api-permissions @PermArgs

foreach ($name in $Permissions.Keys | Sort-Object) {
    Write-Host "     - $name"
}

Write-Host "── Creating service principal ──"

az ad sp create --id $AppId --query id -o tsv 2>$null | Out-Null

Write-Host "── Granting admin consent ──"

$GraphSpId = az ad sp show --id $GraphApi --query id -o tsv
$AppSpId = az ad sp show --id $AppId --query id -o tsv

az rest --method POST `
    --uri "https://graph.microsoft.com/v1.0/oauth2PermissionGrants" `
    --body "{`"clientId`":`"$AppSpId`",`"consentType`":`"AllPrincipals`",`"resourceId`":`"$GraphSpId`",`"scope`":`"User.Read Sites.Read.All Mail.Read People.Read.All OnlineMeetingTranscript.Read.All Chat.Read ChannelMessage.Read.All ExternalItem.Read.All`"}" `
    -o none

Write-Host ""
Write-Host "── Generating Configuration.plist ──"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$PlistPath = Join-Path $ScriptDir "A2A Chat/Configuration.plist"
$PlistContent = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>ClientId</key>
    <string>$AppId</string>
</dict>
</plist>
"@
Set-Content -Path $PlistPath -Value $PlistContent -Encoding UTF8

Write-Host "   Created: A2A Chat/Configuration.plist"

Write-Host ""
Write-Host "── Done ──"
Write-Host "App ID: $AppId"
Write-Host "Redirect URI: $RedirectUri"
Write-Host ""
Write-Host "Configuration.plist created. Build and run in Xcode."
