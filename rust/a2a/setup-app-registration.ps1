# ── Work IQ A2A CLI — Azure AD App Registration Setup ────────────────────
# Creates a single-tenant public client app registration for the Rust
# sample's MSAL flow against the Work IQ Gateway.
#
# Prerequisites: az CLI, logged in (az login)
# Usage: .\setup-app-registration.ps1
#
# For multi-language testing (this sample + .NET samples sharing one
# registration), use the unified ..\..\scripts\admin-setup.ps1 at the
# repo root instead.

$ErrorActionPreference = "Stop"

$DisplayName = "Work IQ A2A CLI"
$SignInAudience = "AzureADMyOrg"

# Work IQ resource app
$WorkIqAppId = "fdcc1f02-fc51-4226-8753-f668596af7f7"
$WorkIqAgentAskScopeId = "0b1715fd-f4bf-4c63-b16d-5be31f9847c2"

Write-Host "── Ensuring Work IQ service principal exists in tenant ──"
az ad sp create --id $WorkIqAppId 2>$null | Out-Null

Write-Host "── Creating app registration: $DisplayName ──"

$AppId = az ad app create `
    --display-name $DisplayName `
    --sign-in-audience $SignInAudience `
    --is-fallback-public-client true `
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
Write-Host "── Done ──"
Write-Host "App ID: $AppId"
Write-Host ""
Write-Host "Use with:  cargo run -- --appid $AppId"
Write-Host "Or set:    `$env:WORKIQ_APP_ID = '$AppId'"
