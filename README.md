# Work IQ Samples

Sample clients for the [Work IQ](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/workiq-overview) API — Microsoft's AI-native interface to Microsoft 365 work intelligence.

| Sample | Language | Platform | Protocol | Description |
|--------|----------|----------|----------|-------------|
| [**dotnet/a2a/**](dotnet/a2a/) | C# | Windows, macOS, Linux | [A2A](https://a2a-protocol.org) | Interactive agent session using the A2A protocol over JSON-RPC |
| [**dotnet/a2a-raw/**](dotnet/a2a-raw/) | C# | Windows, macOS, Linux | [A2A](https://a2a-protocol.org) | Same, but with raw `HttpClient` + JSON (no A2A SDK) |
| [**dotnet/rest/**](dotnet/rest/) | C# | Windows, macOS, Linux | REST | Interactive chat using the [Copilot Chat API](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/overview) |
| [**rust/a2a/**](rust/a2a/) | Rust | Windows, macOS, Linux | [A2A](https://a2a-protocol.org) | Interactive agent session with device code auth and token caching |
| [**swift/a2a/**](swift/a2a/) | Swift | iOS/iPadOS (macOS to build) | [A2A](https://a2a-protocol.org) | SwiftUI chat app with streaming responses |

> **Current state**: Work IQ is accessed through the Microsoft Graph API at `graph.microsoft.com`. All samples use Graph endpoints and Graph authentication today.
>
> **What's coming**: A dedicated Work IQ gateway (`workiq.svc.cloud.microsoft`) with its own app registration, scopes (`WorkIQAgent.Ask`), and endpoint. When available, these samples will be updated with the new endpoint and auth model. Your integration code will change minimally — different token audience and endpoint URL, same protocols.

## Prerequisites

### 1. Microsoft 365 Copilot license

The Chat API is only available to users with a **Microsoft 365 Copilot** add-on license. Users without the license will get access denied errors. See [licensing docs](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/overview#licensing).

### 2. Azure AD app registration

Register an app in [Azure portal](https://portal.azure.com) > Microsoft Entra ID > App registrations:

- **Supported account types**: Accounts in any organizational directory (multi-tenant)
- **Redirect URI**: `http://localhost` (type: Web) for browser-based auth, or leave empty for WAM
- **API permissions**: Add the following **delegated** Microsoft Graph permissions:

| Permission | Description |
|-----------|-------------|
| `Sites.Read.All` | Read SharePoint sites |
| `Mail.Read` | Read user mail |
| `People.Read.All` | Read people data |
| `OnlineMeetingTranscript.Read.All` | Read meeting transcripts |
| `Chat.Read` | Read Teams chats |
| `ChannelMessage.Read.All` | Read Teams channel messages |
| `ExternalItem.Read.All` | Read external connector items |

**All seven are required.** Missing any one will result in auth errors. See [permissions docs](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotconversation-chat#permissions).

After adding permissions, click **Grant admin consent for [your tenant]**.

### 3. Language-specific SDKs

- **dotnet/** samples: [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- **rust/** samples: [Rust toolchain](https://rustup.rs/) (stable)
- **swift/** samples: [Xcode 26+](https://developer.apple.com/xcode/) (macOS only)

## Authentication

All samples support multiple authentication methods. Choose the one that fits your platform:

### WAM (Windows Account Manager) — Windows only, .NET samples

Uses the Windows broker for silent SSO. No browser popup for returning users.

```bash
cd dotnet/a2a
dotnet run -- --graph --token WAM --appid <your-app-client-id>
```

> **Note:** WAM is only available on Windows. On macOS and Linux, use a pre-obtained JWT token instead (see below).

### Device code flow — all platforms (Rust, Swift)

The Rust CLI and Swift app use device code flow, which works on any platform with a web browser.

```bash
cd rust/a2a
cargo run -- --appid <your-app-client-id>
# Follow the on-screen instructions to authenticate in a browser
```

### Pre-obtained JWT token — all platforms, all samples

Acquire a token externally (e.g., via [Graph Explorer](https://developer.microsoft.com/en-us/graph/graph-explorer), `az account get-access-token`, or your own MSAL code) and pass it directly. This works on Windows, macOS, and Linux.

```bash
# .NET samples
cd dotnet/a2a
dotnet run -- --graph --token eyJ0eXAiOiJKV1Qi...

# Rust
cd rust/a2a
cargo run -- --token eyJ0eXAiOiJKV1Qi...
```

The token must have:
- **Audience**: `https://graph.microsoft.com`
- **Scopes**: all 7 delegated permissions listed above

## Common issues

| Issue | Cause | Fix |
|-------|-------|-----|
| `AADSTS65001: consent required` | Admin hasn't consented to the required permissions | Grant admin consent in Azure portal: Entra ID > App registrations > your app > API permissions > Grant admin consent |
| `403 Forbidden` | Missing Copilot license or missing permissions | Verify the user has a Copilot license and all 7 permissions are consented |
| Empty or degraded responses | License just assigned, index not ready | Wait 15-30 minutes after license assignment for propagation |
| `401 Unauthorized` | Token audience mismatch | Ensure token audience is `https://graph.microsoft.com` |

## Resources

- [Work IQ Overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/workiq-overview)
- [Copilot Chat API](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/overview)
- [A2A Protocol Specification](https://a2a-protocol.org/latest/specification/)
- [Microsoft Graph Permissions Reference](https://learn.microsoft.com/en-us/graph/permissions-reference)
- [MSAL.NET Documentation](https://learn.microsoft.com/en-us/entra/msal/dotnet/)
