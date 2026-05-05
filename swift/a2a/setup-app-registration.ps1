# ── A2A Chat — Azure AD App Registration Setup ──────────────────────────
# Creates a single-tenant app registration with the iOS redirect URI and
# the WorkIQAgent.Ask delegated permission needed by the A2A Chat app
# against the Work IQ Gateway.
#
# Prerequisites: az CLI, logged in (az login)
# Usage: .\setup-app-registration.ps1
#
# For multi-language testing (this sample + .NET samples), run the
# unified ..\..\scripts\admin-setup.ps1 first, then re-run this script
# (or add the iOS redirect URI manually) — only the redirect URI is
# iOS-specific.

$ErrorActionPreference = "Stop"

$DisplayName = "A2A Chat"
$RedirectUri = "msauth.app.blueglass.A2A-Chat://auth"
$SignInAudience = "AzureADMyOrg"

# Work IQ resource app
$WorkIqAppId = "fdcc1f02-fc51-4226-8753-f668596af7f7"
$WorkIqAgentAskScopeId = "0b1715fd-f4bf-4c63-b16d-5be31f9847c2"
$WorkIqEndpoint = "https://workiq.svc.cloud.microsoft/a2a/"
$WorkIqScope = "api://workiq.svc.cloud.microsoft/.default"

Write-Host "── Ensuring Work IQ service principal exists in tenant ──"
az ad sp create --id $WorkIqAppId 2>$null | Out-Null

Write-Host "── Creating app registration: $DisplayName ──"

$AppId = az ad app create `
    --display-name $DisplayName `
    --public-client-redirect-uris $RedirectUri `
    --sign-in-audience $SignInAudience `
    --query appId -o tsv

Write-Host "   App ID: $AppId"

Write-Host "── Adding WorkIQAgent.Ask delegated permission ──"

az ad app permission add --id $AppId --api $WorkIqAppId `
    --api-permissions "$WorkIqAgentAskScopeId=Scope" 2>$null | Out-Null

Write-Host "── Creating service principal for the new app ──"
az ad sp create --id $AppId --query id -o tsv 2>$null | Out-Null

Write-Host "── Granting admin consent ──"
$WorkIqSpId = az ad sp show --id $WorkIqAppId --query id -o tsv
$AppSpId = az ad sp show --id $AppId --query id -o tsv

az rest --method POST `
    --uri "https://graph.microsoft.com/v1.0/oauth2PermissionGrants" `
    --body "{`"clientId`":`"$AppSpId`",`"consentType`":`"AllPrincipals`",`"resourceId`":`"$WorkIqSpId`",`"scope`":`"WorkIQAgent.Ask`"}" `
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
    <key>RedirectUri</key>
    <string>$RedirectUri</string>
    <key>TenantId</key>
    <string>common</string>
    <key>Scopes</key>
    <array>
        <string>$WorkIqScope</string>
    </array>
    <key>Endpoint</key>
    <string>$WorkIqEndpoint</string>
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
