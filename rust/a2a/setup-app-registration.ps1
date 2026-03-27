# ── Work IQ A2A CLI — Azure AD App Registration Setup ────────────────────
# Creates a single-tenant public client app registration for device code
# flow authentication against Microsoft Graph / Work IQ.
#
# Prerequisites: az CLI, logged in (az login)
# Usage: .\setup-app-registration.ps1

$ErrorActionPreference = "Stop"

$DisplayName = "Work IQ A2A CLI"
$SignInAudience = "AzureADMyOrg"
$GraphApi = "00000003-0000-0000-c000-000000000000"

# Microsoft Graph delegated permission GUIDs
$Permissions = @{
    "User.Read" = "e1fe6dd8-ba31-4d61-89e7-88639da4683d"
}

Write-Host "── Creating app registration: $DisplayName ──"

$AppId = az ad app create `
    --display-name $DisplayName `
    --sign-in-audience $SignInAudience `
    --is-fallback-public-client true `
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
    --body "{`"clientId`":`"$AppSpId`",`"consentType`":`"AllPrincipals`",`"resourceId`":`"$GraphSpId`",`"scope`":`"User.Read`"}" `
    -o none

Write-Host ""
Write-Host "── Done ──"
Write-Host "App ID: $AppId"
Write-Host ""
Write-Host "Use with:  cargo run -- --appid $AppId"
Write-Host "Or set:    `$env:WORKIQ_APP_ID = '$AppId'"
