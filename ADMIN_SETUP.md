# Admin Setup — Work IQ Samples

**Who this is for**: tenant admin (Cloud Application Administrator or above). A developer on your team is running the official Microsoft Work IQ / Copilot Chat API samples and needs a Microsoft Entra app registration configured in your tenant.

**Time**: ~5 minutes.

**Result**: an **App ID** and **Tenant ID** you hand to the developer. They'll pass these as `--appid` and `--tenant` to the sample commands.

---

## Option A — One command (recommended)

From the root of this repo:

```bash
# Bash / WSL / macOS / Linux
scripts/admin-setup.sh
```

```powershell
# Windows PowerShell / PowerShell 7+
scripts\admin-setup.ps1
```

Flags:

| Flag | Meaning |
|------|---------|
| `--name "My Name"` / `-Name` | Custom app display name. Default: `Work IQ Samples Client`. |
| `--tenant <id>` / `-Tenant` | Target tenant (must match your current `az login` context). |
| `--multi-tenant` / `-MultiTenant` | Configure as multi-tenant (`AzureADMultipleOrgs`). Default is single-tenant. |
| `--dry-run` / `-DryRun` | Print commands without running them. |
| `--verbose` / `-VerboseLog` | Echo each `az` command. |

The script is idempotent: running it twice with the same name offers to update the existing app instead of creating a duplicate.

When it finishes, it prints the **App ID** and **Tenant ID**. Give those two values to the developer.

---

## Option B — Azure CLI, step by step

If you want to see exactly what the script does (or need to customize), these are the six commands. Replace `<APP_ID>` with the value returned in step 2.

```bash
# 1. Ensure the Work IQ service principal exists in your tenant (JIT provisioning).
#    Harmless if it already exists.
az ad sp create --id fdcc1f02-fc51-4226-8753-f668596af7f7

# 2. Create the app registration as a single-tenant public client.
APP_ID=$(az ad app create \
  --display-name "Work IQ Samples Client" \
  --sign-in-audience AzureADMyOrg \
  --is-fallback-public-client true \
  --query appId -o tsv)
echo "App ID: $APP_ID"

# 3. Create the service principal for the app itself.
az ad sp create --id $APP_ID

# 4. Configure the three public-client redirect URIs.
#    - http://localhost: browser interactive fallback
#    - nativeclient: legacy MSAL native
#    - brokerplugin: WAM broker on Windows (required for .NET WAM auth)
az ad app update --id $APP_ID \
  --public-client-redirect-uris \
    "http://localhost" \
    "https://login.microsoftonline.com/common/oauth2/nativeclient" \
    "ms-appx-web://microsoft.aad.brokerplugin/$APP_ID"

# 5. Add the delegated permission for the Work IQ Gateway.
az ad app permission add --id $APP_ID \
  --api fdcc1f02-fc51-4226-8753-f668596af7f7 \
  --api-permissions "0b1715fd-f4bf-4c63-b16d-5be31f9847c2=Scope"
# Adds: WorkIQAgent.Ask

# 6. Grant admin consent for the delegated permission added above.
az ad app permission admin-consent --id $APP_ID

# Print the tenant ID for the developer.
az account show --query tenantId -o tsv
```

---

## Option C — Azure Portal (UI)

1. Go to **Microsoft Entra ID** → **App registrations** → **New registration**.
2. **Name**: `Work IQ Samples Client` (or anything).
3. **Supported account types**: *Accounts in this organizational directory only*.
4. Leave **Redirect URI** blank for now. Click **Register**.
5. On the new app's **Overview**, copy the **Application (client) ID** — this is the App ID.
6. **Authentication** → **Add a platform** → **Mobile and desktop applications**.
   - Check: `https://login.microsoftonline.com/common/oauth2/nativeclient`.
   - **Custom redirect URIs**: add these two:
     - `http://localhost`
     - `ms-appx-web://microsoft.aad.brokerplugin/<APP_ID>` (paste the App ID from step 5).
   - Under **Advanced settings** → **Allow public client flows**: **Yes**.
   - **Save**.
7. **API permissions** → **Add a permission** → *APIs my organization uses* → search **Work IQ** → *Delegated permissions* → check **WorkIQAgent.Ask** → **Add permissions**.
8. Back on **API permissions**, click **Grant admin consent for [your tenant]**. Confirm. The row should show ✔️ Granted.
9. **Microsoft Entra ID** → **Overview** → copy the **Tenant ID**.

Step 8 also JIT-provisions the Work IQ service principal. If it doesn't (rare), run:

```bash
az ad sp create --id fdcc1f02-fc51-4226-8753-f668596af7f7
```

---

## Work IQ service principal — why that extra step?

Microsoft publishes Work IQ as a multi-tenant app (`fdcc1f02-fc51-4226-8753-f668596af7f7`). For a client app in your tenant to request tokens against Work IQ, a **service principal** for the Work IQ app must exist in your tenant. This is normally created automatically the first time an admin consents to any Work IQ permission, but `az ad sp create --id fdcc1f02-...` forces it explicitly.

---

## Security posture

- The app registration is a **public client** — no client secret, no certificate.
- Auth flows used by the samples are **user-delegated**: the signed-in user's own permissions determine what data is returned.
- For **service-to-service** scenarios (unattended, daemon, backend jobs), these samples are not the right starting point. Use a confidential-client app with certificates and `.default` application scopes, and follow [application-permissions](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-client-creds-grant-flow).
- Default is **single-tenant** — only users in your own tenant can sign in. Use `--multi-tenant` only if you have a specific cross-tenant scenario.

---

## What to share back with the developer

1. **App ID** — the GUID from the script's final output (or portal step 5).
2. **Tenant ID** — the GUID from `az account show --query tenantId -o tsv` (or portal step 9).

The developer will use these in the sample's `--appid` and `--tenant` flags.

---

## Sample-specific scripts (already in the repo)

The unified `scripts/admin-setup.sh` above covers all four .NET samples and the Rust CLI — they all need the same Work IQ Gateway permission (`WorkIQAgent.Ask`). The two folders below contain their own setup helpers for language-specific reasons:

- **`rust/a2a/setup-app-registration.sh`** — same Work IQ Gateway setup as the unified script, minus the WAM redirect URIs (Rust auth uses MSAL broker / browser PKCE / device code, not WAM redirects).
- **`swift/a2a/setup-app-registration.sh`** — adds an iOS-specific redirect URI (`msauth.app.blueglass.A2A-Chat://auth`) and generates `Configuration.plist`. Run this **after** the unified script (or instead of, if you only need the iOS app); the iOS redirect URI is specific to the app bundle.

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| `ERROR: Not logged in to Azure CLI` | Run `az login` (use `--tenant <id>` if multi-tenant). |
| `ERROR: Insufficient privileges to complete the operation` | You need Cloud Application Administrator or higher. |
| `admin-consent` fails with "service principal not found" for Work IQ | Run `az ad sp create --id fdcc1f02-fc51-4226-8753-f668596af7f7` first. |
| `az ad app permission add` prints a warning about `grant` | Harmless. The next step (`admin-consent`) covers it. |
| Developer gets `IncorrectConfiguration` on WAM | Redirect URIs were not applied (step 4 / portal step 6). Re-run and confirm all three URIs are present. |
| Developer gets `403 Forbidden` without a scope message | User is missing the Microsoft 365 Copilot license. Assign it; wait 15–30 min for propagation. |
