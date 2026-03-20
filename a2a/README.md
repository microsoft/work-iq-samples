# WorkIQ A2A Sample

A minimal, single-file interactive client for communicating with WorkIQ agents using the [Agent2Agent (A2A) protocol](https://a2a-protocol.org).

> **Preview (via Microsoft Graph):** During preview, WorkIQ is accessed through the Microsoft Graph API at `graph.microsoft.com`. When WorkIQ's dedicated infrastructure is available, the endpoint will move to `workiq.svc.cloud.dev.microsoft` with its own app registration (audience: `fdcc1f02-fc51-4226-8753-f668596af7f7`) and scopes. The code and instructions in this sample will be updated at that time.

## What is A2A?

The **Agent2Agent (A2A) Protocol** is an open standard (originally by Google, now under the Linux Foundation) for communication between AI agents. It defines JSON-RPC methods for sending messages, managing tasks, and streaming responses via Server-Sent Events.

- 📖 [A2A Protocol Specification](https://a2a-protocol.org/latest/specification/)
- 🧑‍💻 [A2A .NET SDK](https://github.com/a2aproject/a2a-dotnet) — the NuGet package used by this sample
- 📦 [NuGet: A2A](https://www.nuget.org/packages/A2A/)

## NuGet Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| [`A2A`](https://www.nuget.org/packages/A2A/) | 0.3.3-preview | A2A protocol client (JSON-RPC, message/send, message/stream) |
| `Microsoft.Identity.Client` | 4.83.1 | MSAL token acquisition |
| `Microsoft.Identity.Client.Broker` | 4.83.1 | Windows WAM (Web Account Manager) broker |
| `System.IdentityModel.Tokens.Jwt` | 8.16.0 | JWT token decoding for diagnostics |

> **Note:** This sample uses A2A SDK v0.3.3-preview. The A2A spec has since moved to v1.0. A [migration guide](https://github.com/a2aproject/a2a-dotnet/blob/main/docs/migration-guide-v1.md) and backward-compatible `A2A.V0_3` package are available if you need to upgrade.

## Gateway Support

| Gateway | Flag | Status | Endpoint |
|---------|------|--------|----------|
| Microsoft Graph RP | `--graph` | ✅ Working | `https://graph.microsoft.com/rp/workiq/` |
| WorkIQ Gateway | `--workiq` | 🚧 Coming soon | TBD |

## Usage

```bash
# Build
dotnet build

# Run with WAM (Windows broker auth — recommended)
dotnet run -- --graph --token WAM --appid <your-app-client-id>

# Run with WAM + account hint (skips account picker)
dotnet run -- --graph --token WAM --appid <your-app-client-id> --account user@contoso.com

# Run with a pre-obtained JWT token
dotnet run -- --graph --token eyJ0eXAiOiJKV1Qi...

# Get a token via WAM, print it, then reuse it
dotnet run -- --graph --token WAM --appid <your-app-client-id> --show-token
# Copy the printed token, then:
dotnet run -- --graph --token <paste-token-here>
```

### Parameters

| Flag | Description |
|------|-------------|
| `--graph` | Use Microsoft Graph RP gateway (required, or `--workiq`) |
| `--workiq` | Use WorkIQ Gateway — not yet implemented |
| `--token`, `-t` | Bearer JWT token, or `WAM` for Windows broker auth |
| `--appid`, `-a` | Azure AD app client ID (required with `--token WAM`) |
| `--account` | Account hint for WAM (e.g. `user@contoso.com`) |
| `--endpoint`, `-e` | Override the default gateway endpoint URL |
| `--show-token` | Print the raw JWT after decoding (for reuse) |

## How It Works

```
┌──────────────┐     JSON-RPC POST       ┌──────────────────┐
│  This Sample │ ──────────────────────►  │  Microsoft Graph │
│  (A2A Client)│ ◄──────────────────────  │  Copilot API     │
└──────────────┘   AgentMessage response  └──────────────────┘
```

1. **Auth**: Acquires a token with `audience: https://graph.microsoft.com` via WAM or accepts a pre-obtained JWT
2. **A2A Client**: Creates an `A2AClient` from the [A2A .NET SDK](https://github.com/a2aproject/a2a-dotnet) pointed at the Graph RP endpoint
3. **Send**: Sends `message/send` (sync) or `message/stream` (streaming) JSON-RPC requests with user input as `TextPart`
4. **Receive**: Parses `AgentMessage` or `AgentTask` responses, extracts text parts and citations from metadata
5. **Multi-turn**: Maintains `contextId` across turns for conversation continuity

## A2A Protocol Compliance

### What works today

| A2A Capability | Status | Notes |
|---------------|--------|-------|
| `message/send` (sync) | ✅ Works | Full request/response cycle |
| `message/stream` (SSE) | ✅ Works | Incremental streaming via `TaskStatusUpdateEvent` |
| Multi-turn (`contextId`) | ✅ Works | Conversation state maintained across turns |
| `TextPart` messages | ✅ Works | User and agent text messages |
| Citations | ✅ Works | Via Microsoft-specific `metadata["attributions"]` (see [Citations](#citations-microsoft-specific-extension)) |

### Known limitations

The following A2A capabilities are **not yet available** and are being actively developed.

#### Agent card retrieval

Per the [A2A spec](https://a2a-protocol.org/latest/specification/), an A2A client typically fetches an agent card from `/.well-known/agent.json` to discover capabilities and the endpoint URL. This is **not yet supported**. The sample connects directly to the Microsoft 365 Copilot agent endpoint.

**Coming soon**: Agent card support with correct endpoint discovery.

#### Agent discovery

Agent discovery — finding which agents are available — is **not part of the A2A protocol**. The A2A spec defines communication with a known agent, not how to discover agents. A Microsoft-specific agent listing mechanism is planned but **not yet available**.

**Current approach**: This sample connects directly to the default **Microsoft 365 Copilot** agent. Support for discovering and selecting other agents is coming soon.

#### Platform support

| Platform | Auth method | Status |
|----------|------------|--------|
| **Windows** | WAM broker (silent SSO) | ✅ Tested |
| **macOS** | Interactive browser login | 🔄 Untested (should work) |
| **Linux / WSL** | Interactive browser login | 🔄 Untested (should work) |

On Windows, `--token WAM` uses the Windows Account Manager for silent SSO (no browser popup). On macOS/Linux, MSAL falls back to interactive browser authentication. You can also use `--token <JWT>` on any platform with a pre-obtained token.

#### Token caching
Tokens are held **in memory only** for the duration of the session — they are not persisted to disk. Each run requires a fresh authentication. For production applications, consider using [MSAL token cache serialization](https://learn.microsoft.com/en-us/entra/msal/dotnet/how-to/token-cache-serialization) with platform-appropriate encryption (DPAPI on Windows, Keychain on macOS, keyring on Linux).

#### Summary

| A2A Feature | Status | Current Approach |
|------------|--------|-----------------|
| `message/send` (sync) | ✅ Available | Standard A2A JSON-RPC |
| `message/stream` (SSE) | ✅ Available | Standard A2A streaming |
| Multi-turn (`contextId`) | ✅ Available | Maintained across turns |
| Agent card (`/.well-known/agent.json`) | 🚧 Coming soon | Use endpoint URL directly |
| Agent discovery / listing | 🚧 Coming soon | Connect to M365 Copilot agent directly |
| macOS / Linux / WSL auth | 🔄 Untested | Interactive browser login (WAM is Windows-only) |

> **For now**, use the Microsoft 365 Copilot agent endpoint (`https://graph.microsoft.com/rp/workiq/`) directly. Agent card retrieval, agent discovery, and cross-platform auth will be enabled in upcoming releases.

## Gotchas

### Graph RP streaming support
Both `message/send` (sync) and `message/stream` (SSE streaming) work via Graph RP. Use `--stream` for incremental response output.

### App ID is required with WAM
When using `--token WAM`, you must provide `--appid` with your Azure AD app registration's client ID. The app must have appropriate Microsoft Graph delegated permissions.

### Required permissions (all seven are mandatory)
The Chat API requires **all** of these delegated permissions to be consented on your app registration:
`Sites.Read.All`, `Mail.Read`, `People.Read.All`, `OnlineMeetingTranscript.Read.All`, `Chat.Read`, `ChannelMessage.Read.All`, `ExternalItem.Read.All`. Missing any one will result in auth errors. See [permissions docs](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotconversation-chat#permissions).

### Microsoft 365 Copilot license required
The Chat API is only available to users with a Microsoft 365 Copilot add-on license. Users without the license will get access denied errors.

### Token audience must be `https://graph.microsoft.com`
The Graph RP rejects tokens with other audiences. If using a pre-obtained token, ensure it was acquired with `scope: https://graph.microsoft.com/.default`.

### A2A SDK version
This sample uses `A2A` v0.3.3-preview which implements the [A2A v0.3 spec](https://a2a-protocol.org). The protocol has since moved to v1.0 with breaking changes (renamed types, new methods). See the [migration guide](https://github.com/a2aproject/a2a-dotnet/blob/main/docs/migration-guide-v1.md) when upgrading.

## Citations (Microsoft-specific extension)

> **Important:** Citations are delivered via a **Microsoft-specific extension** to the A2A protocol — not part of the [A2A spec](https://a2a-protocol.org). This format is specific to Microsoft 365 Copilot and is subject to change. When WorkIQ moves to its dedicated gateway, citations may be delivered using A2A-native `DataPart` or a formal A2A extension mechanism.

### How citations work in A2A

The standard A2A protocol defines `TextPart`, `FilePart`, and `DataPart` as message parts, but has no built-in citation type. Microsoft 365 Copilot delivers citations via the `AgentMessage.Metadata` dictionary under the key `"attributions"`.

### Citation schema

```json
// AgentMessage.Metadata["attributions"]
[
  {
    "attributionType": "Citation",          // "Citation" or "Annotation"
    "attributionSource": "Model",           // "Model" (cited in response) or "Grounding" (used but not cited)
    "providerDisplayName": "Q3 Planning",   // Display name of the source
    "seeMoreWebUrl": "https://...",         // Link to the source
    "imageWebUrl": "",                      // Optional image URL
    "imageFavIcon": "",                     // Optional favicon
    "imageWidth": 0,                        // Optional image dimensions
    "imageHeight": 0
  }
]
```

### Attribution types

| Type | Meaning | Example |
|------|---------|---------|
| `Citation` | Source explicitly referenced in the response text (e.g., `[1]`) | A meeting, email, or document |
| `Annotation` | Entity recognized in the response but not numbered | A person mention, team name |

### Attribution sources

| Source | Meaning |
|--------|---------|
| `Model` | Used by the model to generate the response (cited) |
| `Grounding` | Used for context/grounding but not directly cited |

### Parsing in code

```csharp
// After receiving an AgentMessage (sync or streaming)
if (agentMessage.Metadata?.TryGetValue("attributions", out var attrs) == true
    && attrs.ValueKind == JsonValueKind.Array)
{
    foreach (var attr in attrs.EnumerateArray())
    {
        var type = attr.GetProperty("attributionType").ToString();     // "Citation" or "Annotation"
        var source = attr.GetProperty("attributionSource").ToString(); // "Model" or "Grounding"
        var name = attr.GetProperty("providerDisplayName").GetString();
        var url = attr.GetProperty("seeMoreWebUrl").GetString();
        
        Console.WriteLine($"[{type}] {name} → {url}");
    }
}
```

### Sample output (`-v 2`)

```
Agent > You have 3 meetings scheduled tomorrow...
  (22500 ms)
  Citations: 3  Annotations: 2
    📄 [Citation/Model] Q3 Planning Meeting
       https://teams.microsoft.com/l/meeting/details?eventId=...
    📄 [Citation/Model] Weekly Team Standup
       https://teams.microsoft.com/l/meeting/details?eventId=...
    🔗 [Annotation/Model] Jane Smith
       https://www.office.com/search?q=Jane+Smith...
```

### Future direction

When WorkIQ transitions to its dedicated gateway, citations will likely move to one of:
1. **A2A `DataPart`** — structured data parts in the message (A2A-native)
2. **A2A extension** — formal extension mechanism per A2A spec
3. **Same metadata format** — carried forward for backward compatibility

Monitor the [WorkIQ API documentation](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility) for updates.

## Wire Diagnostics

The sample includes a `WireLog` HTTP handler that prints full request/response diagnostics:

```
  ▶ POST https://graph.microsoft.com/rp/workiq/
    Authorization: Bearer ...(3089c)
    Content-Type: application/json
    Body: {"jsonrpc":"2.0","method":"message/send","params":{...}}

  ◀ 200 OK
    request-id: d7d0989c-...
    Content-Type: application/json
```

For errors (4xx/5xx), the full response body is printed in red.

## Resources

- [A2A Protocol Specification](https://a2a-protocol.org/latest/specification/)
- [A2A .NET SDK (GitHub)](https://github.com/a2aproject/a2a-dotnet)
- [A2A NuGet Package](https://www.nuget.org/packages/A2A/)
- [Microsoft Graph Copilot Chat API](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotconversation-chat)
- [WorkIQ CLI](https://github.com/nicholasgasior/work-iq-cli) _(reference implementation)_
