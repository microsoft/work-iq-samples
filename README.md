# Work IQ Samples

Sample clients for the [Work IQ](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/workiq-overview) API — Microsoft's AI-native interface to Microsoft 365 work intelligence.

| Sample | Language | Platform | Protocol | Description |
|--------|----------|----------|----------|-------------|
| [**dotnet/a2a/**](dotnet/a2a/) | C# | Windows, macOS, Linux | [A2A](https://a2a-protocol.org) | Interactive agent session using the A2A protocol over JSON-RPC |
| [**dotnet/a2a-raw/**](dotnet/a2a-raw/) | C# | Windows, macOS, Linux | [A2A](https://a2a-protocol.org) | Same, but with raw `HttpClient` + JSON (no A2A SDK) |
| [**rust/a2a/**](rust/a2a/) | Rust | Windows, macOS, Linux | [A2A](https://a2a-protocol.org) | Interactive agent session with device code auth and token caching |
| [**swift/a2a/**](swift/a2a/) | Swift | iOS/iPadOS (macOS to build) | [A2A](https://a2a-protocol.org) | SwiftUI chat app with streaming responses |

---

## Gateway

All .NET samples target the **Work IQ Gateway** (`workiq.svc.cloud.microsoft`) — the dedicated entry point for Work IQ. Delegated scope: `api://workiq.svc.cloud.microsoft/WorkIQAgent.Ask`.

---

## Before you run — three prerequisites

1. **Microsoft 365 Copilot license** on the test user (license propagation can take 15–30 minutes after assignment).
2. **Entra app registration** configured in your tenant — this is a one-time setup per tenant. Details below.
3. **Your language toolchain**:
   - **dotnet/** samples: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
   - **rust/** samples: [Rust toolchain](https://rustup.rs/) (stable)
   - **swift/** samples: [Xcode 26+](https://developer.apple.com/xcode/) (macOS only)

### App registration setup

You (or your tenant admin) must create an Entra app registration with specific permissions, redirect URIs, and consent. This is a ~5-minute task.

- **If you're the admin** — from the repo root:
  ```bash
  # Bash / WSL / macOS / Linux
  scripts/admin-setup.sh
  ```
  ```powershell
  # PowerShell
  scripts\admin-setup.ps1
  ```
  See [`ADMIN_SETUP.md`](ADMIN_SETUP.md) for all flags and the manual CLI / portal paths.

- **If you're not the admin** — hand [`ADMIN_SETUP.md`](ADMIN_SETUP.md) to them. They'll give you back an **App ID** and **Tenant ID**.

After setup you'll have two values: `APP_ID` and `TENANT_ID`. Pass them to any sample via `--appid` and `--tenant`.

---

## Authentication methods

All samples support multiple authentication methods. Choose the one that fits your platform.

### WAM (Windows Account Manager) — Windows only, .NET samples

Uses the Windows broker for silent SSO. A browser-less sign-in popup appears on first use; subsequent calls in the same process are silent. Across `dotnet run` invocations you'll be re-prompted (the samples don't persist MSAL state across processes by default).

```bash
cd dotnet/a2a
dotnet run -- --token WAM --appid <APP_ID> --tenant <TENANT_ID>
```

> **Note:** WAM is only available on Windows. On macOS and Linux, MSAL falls back to an interactive browser sign-in using the `http://localhost` redirect URI — same command works.

### Device code flow — Rust, Swift

```bash
cd rust/a2a
cargo run -- --appid <APP_ID>
# Follow the on-screen instructions to authenticate in a browser.
```

### Pre-obtained JWT token — all platforms, all samples

Acquire a token externally (e.g. via your own MSAL code) and pass it directly:

```bash
dotnet run -- --token "<SOME_TOKEN>"
```

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `MsalClientException: window_handle_required` | Running `dotnet run` from a context where the console window handle can't be resolved (e.g., piped stdin, some non-interactive shells) | Run from an interactive terminal, or use `--token <jwt>` with a pre-obtained token |
| `WAM_provider_error 3399614466 / IncorrectConfiguration` | App registration missing the WAM broker redirect URI `ms-appx-web://microsoft.aad.brokerplugin/{appId}` | Ask admin to re-run setup (or add that redirect URI manually). See [`ADMIN_SETUP.md`](ADMIN_SETUP.md). |
| Same error, but redirect URI is present | Single-tenant app + `/common` authority mismatch | Pass `--tenant <TENANT_ID>` so MSAL uses the tenant-specific authority |
| `403 Forbidden` without a scope message | User is missing the Microsoft 365 Copilot license | Assign the license; wait 15–30 min for propagation |
| `400 AuthenticationError: Error authenticating with resource` | Gateway rejected the downstream auth exchange (e.g., OBO against an unconfigured downstream service) | Check the request-id in the response headers against the gateway's logs |
| WAM re-prompts for password on every `dotnet run` | MSAL in-process cache doesn't persist across processes | Expected today. A future update may add an opt-in persistent cache. |
| `AADSTS65001: consent required` | Admin hasn't consented to the required permissions | Ask admin to run `admin-consent` (step 6 of the setup) |
| `401 Unauthorized` | Token audience mismatch | Ensure the token `aud` is `fdcc1f02-...` / `api://workiq.svc.cloud.microsoft` |
| Empty or degraded responses | License just assigned, index not ready | Wait 15–30 minutes after license assignment |

---

## Resources

- [Work IQ overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/workiq-overview)
- [A2A protocol specification](https://a2a-protocol.org/latest/specification/)
- [MSAL.NET documentation](https://learn.microsoft.com/en-us/entra/msal/dotnet/)
- [`ADMIN_SETUP.md`](ADMIN_SETUP.md) — detailed admin setup guide
- [`scripts/admin-setup.sh`](scripts/admin-setup.sh) / [`scripts/admin-setup.ps1`](scripts/admin-setup.ps1) — unified app-registration scripts
