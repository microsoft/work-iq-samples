# Work IQ Samples

Sample clients for the [Work IQ](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/workiq-overview) API — Microsoft's AI-native interface to Microsoft 365 work intelligence.

| Sample | Protocol | Description |
|--------|----------|-------------|
| [**a2a/**](a2a/) | [A2A (Agent-to-Agent)](https://a2a-protocol.org) | Interactive agent session using the open A2A protocol over JSON-RPC |
| [**rest/**](rest/) | REST | Interactive chat using the [Copilot Chat API](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/overview) with sync and streaming modes |

Both samples are single-file, minimal-dependency .NET console apps designed to be read, modified, and used as starting points for your own integration.

> **Current state**: Work IQ is accessed through the Microsoft Graph API at `graph.microsoft.com`. All samples use Graph endpoints and Graph authentication today.
>
> **What's coming**: A dedicated Work IQ gateway (`workiq.svc.cloud.microsoft`) with its own app registration, scopes (`WorkIQAgent.Ask`), and endpoint. When available, these samples will be updated with the new endpoint and auth model. Your integration code will change minimally — different token audience and endpoint URL, same protocols.

## Prerequisites

### 1. .NET SDK

[.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later.

### 2. Microsoft 365 Copilot license

The Chat API is only available to users with a **Microsoft 365 Copilot** add-on license. Users without the license will get access denied errors. See [licensing docs](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/overview#licensing).

### 3. Azure AD app registration

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

### 4. Service principal setup (new tenants)

If you're using a newly created test tenant, the Work IQ service principals may not exist yet. You'll see an error like:

```
AADSTS650052: The app is trying to access a service '...' that your organization lacks a service principal for.
```

Fix this by creating the required service principals:

```bash
# Sign into your tenant
az login --allow-no-subscriptions --tenant <your-tenant-id>

# Create the service principals
az ad sp create --id ba081686-5d24-4bc6-a0d6-d034ecffed87
az ad sp create --id ea9ffc3e-8a23-4a7d-836d-234d7c7565c1

# Grant admin consent
az ad app permission admin-consent --id ba081686-5d24-4bc6-a0d6-d034ecffed87
```

Or use the admin consent URL (creates SPs and grants consent in one step):

```
https://login.microsoftonline.com/<your-tenant-id>/adminconsent?client_id=ba081686-5d24-4bc6-a0d6-d034ecffed87
```

## Authentication

Both samples support two authentication methods:

### WAM (Windows Account Manager) — recommended on Windows

Uses the Windows broker for silent SSO. No browser popup for returning users.

```bash
dotnet run -- --token WAM --appid <your-app-client-id>

# With account hint (skips account picker)
dotnet run -- --token WAM --appid <your-app-client-id> --account user@contoso.com
```

### Pre-obtained JWT token — any platform

Acquire a token externally (e.g., via [Graph Explorer](https://developer.microsoft.com/en-us/graph/graph-explorer), `az account get-access-token`, or your own MSAL code) and pass it directly:

```bash
dotnet run -- --token eyJ0eXAiOiJKV1Qi...
```

The token must have:
- **Audience**: `https://graph.microsoft.com`
- **Scopes**: all 7 delegated permissions listed above

### Print token for reuse

```bash
dotnet run -- --token WAM --appid <your-app-client-id> --show-token
# Copy the printed token, then reuse:
dotnet run -- --token <paste-token-here>
```

## Platform support

| Platform | Auth method | Status |
|----------|------------|--------|
| **Windows** | WAM broker (silent SSO) | Tested |
| **macOS** | Interactive browser login | Should work (untested) |
| **Linux / WSL** | Interactive browser login | Should work (untested) |

On macOS/Linux, `--token WAM` is not available. Use `--token <JWT>` with a pre-obtained token, or MSAL will fall back to interactive browser authentication.

## Token caching

Tokens are held **in memory only** for the duration of the session. Each run requires fresh authentication. For production applications, consider using [MSAL token cache serialization](https://learn.microsoft.com/en-us/entra/msal/dotnet/how-to/token-cache-serialization) with platform-appropriate encryption (DPAPI on Windows, Keychain on macOS, keyring on Linux).

## Common issues

| Issue | Cause | Fix |
|-------|-------|-----|
| `AADSTS650052: lacks a service principal` | Service principal doesn't exist in your tenant | See [Service principal setup](#4-service-principal-setup-new-tenants) above |
| `AADSTS65001: consent required` | Admin hasn't consented to the required permissions | Grant admin consent in Azure portal or via the admin consent URL |
| `403 Forbidden` | Missing Copilot license or missing permissions | Verify the user has a Copilot license and all 7 permissions are consented |
| `A window handle must be configured` | Running on Windows without WAM native interop | Build with `-r win-x64`: `dotnet run -r win-x64 -- ...` |
| Empty or degraded responses | License just assigned, index not ready | Wait 15-30 minutes after license assignment for propagation |
| `401 Unauthorized` | Token audience mismatch | Ensure token audience is `https://graph.microsoft.com` |

## Resources

- [Work IQ Overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/workiq-overview)
- [Copilot Chat API](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/overview)
- [A2A Protocol Specification](https://a2a-protocol.org/latest/specification/)
- [Microsoft Graph Permissions Reference](https://learn.microsoft.com/en-us/graph/permissions-reference)
- [MSAL.NET Documentation](https://learn.microsoft.com/en-us/entra/msal/dotnet/)
