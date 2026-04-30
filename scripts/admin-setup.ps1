<#
.SYNOPSIS
  Work IQ Samples — App Registration Setup (Admin script)

.DESCRIPTION
  Creates a public-client Entra app registration with the permissions the
  .NET samples need (Work IQ Gateway). Idempotent: re-running against an
  existing app name updates it (after confirmation) instead of creating
  a duplicate.

  Requires: Azure CLI, logged in as tenant admin (az login).

.PARAMETER Name
  Display name for the app registration. Default: "Work IQ Samples Client".

.PARAMETER Tenant
  Tenant ID. If omitted, uses the current az CLI context.

.PARAMETER MultiTenant
  Configure the app as multi-tenant (AzureADMultipleOrgs).
  Default is single-tenant (AzureADMyOrg).

.PARAMETER DryRun
  Print the commands that would run, without executing them.

.PARAMETER VerboseLog
  Echo each az command before running it.

.EXAMPLE
  .\admin-setup.ps1

.EXAMPLE
  .\admin-setup.ps1 -Name "My Samples App" -MultiTenant

.NOTES
  See repository documentation for setup details.
#>

[CmdletBinding()]
param(
    [string]$Name = 'Work IQ Samples Client',

    [string]$Tenant = '',

    [switch]$MultiTenant,

    [switch]$DryRun,

    [switch]$VerboseLog
)

$ErrorActionPreference = 'Stop'

# ── Well-known IDs ──────────────────────────────────────────────────────
$WorkIqAppId = 'fdcc1f02-fc51-4226-8753-f668596af7f7'
$WorkIqAgentAskScopeId = '0b1715fd-f4bf-4c63-b16d-5be31f9847c2'

$signInAudience = if ($MultiTenant) { 'AzureADMultipleOrgs' } else { 'AzureADMyOrg' }

function Invoke-Step {
    param([string[]]$Cmd)
    if ($VerboseLog) { Write-Host "+ $($Cmd -join ' ')" -ForegroundColor DarkGray }
    if ($DryRun) {
        Write-Host "[dry-run] $($Cmd -join ' ')"
        return $null
    }
    $result = & $Cmd[0] $Cmd[1..($Cmd.Length - 1)]
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $($Cmd -join ' ')"
    }
    return $result
}

# ── Preflight ───────────────────────────────────────────────────────────
if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Error 'Azure CLI not found. Install from https://aka.ms/azcli'
    exit 1
}

try { $accountJson = az account show 2>$null } catch { $accountJson = $null }
if (-not $accountJson) {
    Write-Error 'Not logged in to Azure CLI. Run: az login'
    exit 1
}
$account = $accountJson | ConvertFrom-Json
$currentTenant = $account.tenantId
$currentUser = $account.user.name

if ($Tenant -and $Tenant -ne $currentTenant) {
    Write-Error "--Tenant $Tenant doesn't match the current az context ($currentTenant). Run: az login --tenant $Tenant"
    exit 1
}
$Tenant = $currentTenant

Write-Host 'Work IQ Samples — Admin Setup'
Write-Host "  Tenant:           $Tenant"
Write-Host "  Signed in as:     $currentUser"
Write-Host "  App name:         $Name"
Write-Host "  Sign-in audience: $signInAudience"
if ($DryRun) { Write-Host '  [DRY RUN — no changes will be made]' -ForegroundColor Yellow }
Write-Host ''

# ── Step 1: Work IQ SP JIT-provisioning ─────────────────────────────────
Write-Host '[1/6] Ensuring Work IQ service principal exists in tenant...'
try {
    Invoke-Step @('az', 'ad', 'sp', 'create', '--id', $WorkIqAppId) | Out-Null
} catch {
    # Harmless if the SP already exists
}
Write-Host '      OK'

# ── Step 2: Create or reuse app registration ────────────────────────────
Write-Host '[2/6] Creating app registration...'
$existingAppId = (az ad app list --display-name $Name --query '[0].appId' -o tsv 2>$null)
if ($existingAppId) {
    Write-Host "      Found existing app '$Name' with App ID: $existingAppId"
    $confirm = Read-Host '      Reuse and update it? (y/N)'
    if ($confirm -notmatch '^[yY]') {
        Write-Error 'Aborted. Use -Name to pick a different display name.'
        exit 1
    }
    $appId = $existingAppId
    Invoke-Step @('az', 'ad', 'app', 'update',
        '--id', $appId,
        '--sign-in-audience', $signInAudience,
        '--is-fallback-public-client', 'true') | Out-Null
} else {
    $appId = Invoke-Step @('az', 'ad', 'app', 'create',
        '--display-name', $Name,
        '--sign-in-audience', $signInAudience,
        '--is-fallback-public-client', 'true',
        '--query', 'appId', '-o', 'tsv')
}
Write-Host "      App ID: $appId"

# ── Step 3: Create SP for the app itself ────────────────────────────────
Write-Host '[3/6] Creating service principal for the app...'
try { Invoke-Step @('az', 'ad', 'sp', 'create', '--id', $appId) | Out-Null } catch { }
Write-Host '      OK'

# ── Step 4: Public-client redirect URIs ─────────────────────────────────
Write-Host '[4/6] Configuring public-client redirect URIs...'
Invoke-Step @('az', 'ad', 'app', 'update',
    '--id', $appId,
    '--public-client-redirect-uris',
    'http://localhost',
    'https://login.microsoftonline.com/common/oauth2/nativeclient',
    "ms-appx-web://microsoft.aad.brokerplugin/$appId") | Out-Null
Write-Host '      OK'

# ── Step 5: Delegated permissions ───────────────────────────────────────
Write-Host '[5/6] Adding delegated permissions...'
try {
    Invoke-Step @('az', 'ad', 'app', 'permission', 'add',
        '--id', $appId, '--api', $WorkIqAppId,
        '--api-permissions', "$WorkIqAgentAskScopeId=Scope") | Out-Null
} catch { }
Write-Host '      Work IQ: WorkIQAgent.Ask'

# ── Step 6: Admin consent (direct Microsoft Graph API, idempotent) ─────
# We deliberately do NOT use 'az ad app permission admin-consent' — it uses
# the legacy AAD Graph endpoint and silently no-ops for delegated scopes on
# fresh apps. Instead we GET existing grants, PATCH if found, POST if not.
function Resolve-SpId {
    param([string]$AppId)
    if ($DryRun) { return "<sp-id-for-$AppId>" }
    for ($i = 0; $i -lt 5; $i++) {
        $spId = az ad sp show --id $AppId --query id -o tsv 2>$null
        if ($spId) { return $spId }
        Start-Sleep -Seconds 2
    }
    throw "Could not resolve SP object ID for app $AppId after 10s"
}

function Set-Grant {
    param([string]$ClientSpId, [string]$ResourceSpId, [string]$Scopes)
    if ($DryRun) {
        Write-Host "[dry-run] ensure grant: client=$ClientSpId resource=$ResourceSpId scope='$Scopes'"
        return
    }
    $filter = "clientId eq '$ClientSpId' and resourceId eq '$ResourceSpId'"
    $existingId = az rest --method GET `
        --url "https://graph.microsoft.com/v1.0/oauth2PermissionGrants?`$filter=$filter" `
        --query 'value[0].id' -o tsv 2>$null

    # Pass JSON body via a temp file — PowerShell's native-exe quoting mangles
    # inline JSON strings, and --body @<file> is az CLI's reliable way to read a body.
    $tempFile = New-TemporaryFile
    try {
        if ($existingId -and $existingId -ne '' -and $existingId -ne 'None') {
            (@{ scope = $Scopes } | ConvertTo-Json -Compress) |
                Set-Content -Path $tempFile -NoNewline -Encoding utf8
            az rest --method PATCH `
                --uri "https://graph.microsoft.com/v1.0/oauth2PermissionGrants/$existingId" `
                --body "@$($tempFile.FullName)" -o none
            if ($LASTEXITCODE -ne 0) { throw "PATCH grant failed for client=$ClientSpId resource=$ResourceSpId" }
        } else {
            (@{
                clientId    = $ClientSpId
                consentType = 'AllPrincipals'
                resourceId  = $ResourceSpId
                scope       = $Scopes
            } | ConvertTo-Json -Compress) |
                Set-Content -Path $tempFile -NoNewline -Encoding utf8
            az rest --method POST `
                --uri 'https://graph.microsoft.com/v1.0/oauth2PermissionGrants' `
                --body "@$($tempFile.FullName)" -o none
            if ($LASTEXITCODE -ne 0) { throw "POST grant failed for client=$ClientSpId resource=$ResourceSpId" }
        }
    } finally {
        Remove-Item $tempFile -ErrorAction SilentlyContinue
    }
}

Write-Host '[6/6] Granting admin consent (direct Graph API)...'
$appSpId = Resolve-SpId -AppId $appId
$workIqSpId = Resolve-SpId -AppId $WorkIqAppId
Set-Grant -ClientSpId $appSpId -ResourceSpId $workIqSpId -Scopes 'WorkIQAgent.Ask'
Write-Host '      Work IQ: consent granted'

# ── Summary ─────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '── Done ──'
Write-Host ''
Write-Host 'Give these to your developer:'
Write-Host "  APP_ID:    $appId"
Write-Host "  TENANT_ID: $Tenant"
Write-Host ''
Write-Host 'Test commands:'
Write-Host '  # Work IQ A2A:'
Write-Host "  cd dotnet/a2a; dotnet run -- --token WAM ``"
Write-Host "      --appid $appId --tenant $Tenant"
Write-Host ''
Write-Host '  # Work IQ A2A (raw):'
Write-Host "  cd dotnet/a2a-raw; dotnet run -- --token WAM ``"
Write-Host "      --appid $appId --tenant $Tenant"
