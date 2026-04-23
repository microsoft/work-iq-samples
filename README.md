# Work IQ Samples

Sample clients for the [Work IQ](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/workiq-overview) API — Microsoft's AI-native interface to Microsoft 365 work intelligence.

## Samples

### .NET (`dotnet/`)

| Sample | Protocol | Description |
|--------|----------|-------------|
| [**dotnet/a2a/**](dotnet/a2a/) | [A2A](https://a2a-protocol.org) | Interactive agent session using the A2A .NET SDK |
| [**dotnet/a2a-raw/**](dotnet/a2a-raw/) | [A2A](https://a2a-protocol.org) | Same as above but with raw `HttpClient` — no SDK, just HTTP + JSON |
| [**dotnet/rest/**](dotnet/rest/) | REST | Interactive chat using the [Copilot Chat API](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/overview) with sync and streaming modes |

All .NET samples are single-file, minimal-dependency console apps (.NET 8+) designed to be read, modified, and used as starting points for your own integration.

### Rust (`rust/`)

| Sample | Protocol | Description |
|--------|----------|-------------|
| [**rust/a2a/**](rust/a2a/) | [A2A](https://a2a-protocol.org) | Interactive agent session with device code auth, token caching, and SSE streaming |

### Swift (`swift/`)

| Sample | Protocol | Description |
|--------|----------|-------------|
| [**swift/a2a/**](swift/a2a/) | [A2A](https://a2a-protocol.org) | SwiftUI iOS/iPadOS chat app using A2A v0.3 with streaming responses |

---

## Gateways

The samples target one of two gateways:

- **Work IQ Gateway** (`workiq.svc.cloud.microsoft`, and dev-ring variants `ppe.workiq.svc.cloud.dev.microsoft` / `test.workiq.svc.cloud.dev.microsoft`) — the dedicated entry point for Work IQ and Copilot Chat. Uses the Work IQ app ID + `WorkIQAgent.Ask` delegated scope.
- **Microsoft Graph** (`graph.microsoft.com`) — the public Graph surface serving the same Copilot Chat API under `/beta/copilot/*`. Uses the Microsoft Graph app ID + seven delegated Graph scopes.

Each .NET sample supports both via `--workiq` / `--graph` flags. The Rust and Swift samples target Graph today.

---

## Before you run — three prerequisites

1. **Microsoft 365 Copilot license** on the test user (license propagation can take 15–30 minutes after assignment).
2. **Entra app registration** configured in your tenant — this is a one-time setup per tenant. Details below.
3. **Your language toolchain** — .NET 8+ SDK, Rust toolchain, or Xcode 15+.

### App registration setup

You (or your tenant admin) must create an Entra app registration with specific permissions, redirect URIs, and consent. This is a ~5-minute task.

- **If you're the admin** — from the repo root:
  ```bash
  # Bash / WSL / macOS / Linux
  scripts/admin-setup.sh --workiq
  ```
  ```powershell
  # PowerShell
  scripts\admin-setup.ps1 -Gateway WorkIQ
  ```
  Use `--graph` or `--both` to configure the Graph permissions as well. See [`ADMIN_SETUP.md`](ADMIN_SETUP.md) for all flags and the manual CLI / portal paths.

- **If you're not the admin** — hand [`ADMIN_SETUP.md`](ADMIN_SETUP.md) to them. They'll give you back an **App ID** and **Tenant ID**.

After setup you'll have two values: `APP_ID` and `TENANT_ID`. Pass them to any sample via `--appid` and `--tenant`.

---

## Authentication methods

### WAM (Windows Account Manager) — recommended on Windows (.NET)

Uses the Windows broker for silent SSO. A browser-less sign-in popup appears on first use; subsequent calls in the same process are silent. Across `dotnet run` invocations you'll be re-prompted (the samples don't persist MSAL state across processes by default).

```bash
cd dotnet/rest
dotnet run -- --workiq --token WAM --appid <APP_ID> --tenant <TENANT_ID>
```

On macOS or Linux, WAM isn't available — MSAL falls back to an interactive browser sign-in using the `http://localhost` redirect URI. Same command works.

### Device code flow — any platform (Rust)

```bash
cd rust/a2a
cargo run -- --appid <APP_ID>
# Follow the on-screen instructions to authenticate in a browser.
```

### Pre-obtained JWT — any platform

Acquire a token externally and pass it directly:

```bash
# Example: from Azure CLI (audience = Graph)
TOKEN=$(az account get-access-token --resource https://graph.microsoft.com --query accessToken -o tsv)
dotnet run -- --graph --token "$TOKEN"

# Example: audience = Work IQ
TOKEN=$(az account get-access-token --resource api://workiq.svc.cloud.microsoft --query accessToken -o tsv)
dotnet run -- --workiq --token "$TOKEN"
```

Note that tokens acquired through `az account get-access-token` carry the Azure CLI's client app ID (`04b07795-...`), not your test app. Use this for quick probes, not end-to-end client-identity validation.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `MsalClientException: window_handle_required` | Running `dotnet run` from a context where the console window handle can't be resolved (e.g., piped stdin, some non-interactive shells) | Run from an interactive terminal, or use `--token <jwt>` with a pre-obtained token |
| `WAM_provider_error 3399614466 / IncorrectConfiguration` | App registration missing the WAM broker redirect URI `ms-appx-web://microsoft.aad.brokerplugin/{appId}` | Ask admin to re-run setup (or add that redirect URI manually). See [`ADMIN_SETUP.md`](ADMIN_SETUP.md). |
| Same error, but redirect URI is present | Single-tenant app + `/common` authority mismatch | Pass `--tenant <TENANT_ID>` so MSAL uses the tenant-specific authority |
| `403 Forbidden` with `Required scopes = [Sites.Read.All, Mail.Read, ...]` | Graph delegated permissions not added + admin-consented on the app | Admin runs `scripts/admin-setup.sh --graph` (or adds the 7 permissions manually + grants admin consent) |
| `403 Forbidden` without a scope message | User is missing the Microsoft 365 Copilot license | Assign the license; wait 15–30 min for propagation |
| `400 BadRequest: Invalid request, no valid route` | Using `--endpoint` with an unexpected path | Pass host-only to `--endpoint` (e.g. `https://ppe.workiq.svc.cloud.dev.microsoft`); samples append the correct path |
| `400 AuthenticationError: Error authenticating with resource` | Gateway rejected the downstream auth exchange (e.g., OBO against an unconfigured downstream service) | Check the request-id in the response headers against the gateway's logs |
| WAM re-prompts for password on every `dotnet run` | MSAL in-process cache doesn't persist across processes | Expected today. A future update may add an opt-in persistent cache; for now, open a long-lived interactive session. |
| `AADSTS65001: consent required` | Admin hasn't consented to the required permissions | Ask admin to run `admin-consent` (step 6 of the setup) |
| `az ad app permission add` prints a warning about `grant` | Informational; the permission was added but not yet consented | The next `admin-consent` step handles it |

---

## Resources

- [Work IQ overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/workiq-overview)
- [Copilot Chat API](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/overview)
- [A2A protocol specification](https://a2a-protocol.org/latest/specification/)
- [Microsoft Graph permissions reference](https://learn.microsoft.com/en-us/graph/permissions-reference)
- [MSAL.NET documentation](https://learn.microsoft.com/en-us/entra/msal/dotnet/)
- [`ADMIN_SETUP.md`](ADMIN_SETUP.md) — detailed admin setup guide
- [`scripts/admin-setup.sh`](scripts/admin-setup.sh) / [`scripts/admin-setup.ps1`](scripts/admin-setup.ps1) — unified app-registration scripts
