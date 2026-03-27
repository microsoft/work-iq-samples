# Work IQ A2A Sample

A minimal, single-file interactive client for communicating with Work IQ agents using the [Agent-to-Agent (A2A) protocol](https://a2a-protocol.org).

> **Prerequisites, authentication, and common issues** are covered in the [root README](../README.md). Read that first.

## What is A2A?

The **Agent-to-Agent (A2A) Protocol** is an open standard for communication between AI agents. It defines JSON-RPC methods for sending messages, managing tasks, and streaming responses via Server-Sent Events.

- [A2A Protocol Specification](https://a2a-protocol.org/latest/specification/)
- [A2A .NET SDK](https://github.com/a2aproject/a2a-dotnet) (NuGet: [`A2A`](https://www.nuget.org/packages/A2A/))

## Quick start

```bash
# Build
dotnet build

# Run (sync mode)
dotnet run -- --graph --token WAM --appid <your-app-client-id>

# Run (streaming mode — SSE via message/stream)
dotnet run -- --graph --token WAM --appid <your-app-client-id> --stream

# With account hint
dotnet run -- --graph --token WAM --appid <your-app-client-id> --account user@contoso.com
```

## Parameters

| Flag | Description |
|------|-------------|
| `--graph` | Use Microsoft Graph RP gateway (required) |
| `--token`, `-t` | Bearer JWT token, or `WAM` for Windows broker auth |
| `--appid`, `-a` | Azure AD app client ID (required with `--token WAM`) |
| `--account` | Account hint for WAM (e.g. `user@contoso.com`) |
| `--endpoint`, `-e` | Override the default gateway endpoint URL |
| `--header`, `-H` | Custom HTTP header in `Key: Value` format (repeatable) |
| `--show-token` | Print the raw JWT after decoding (for reuse) |
| `--stream` | Use streaming mode (SSE via `message/stream`) |
| `-v`, `--verbosity` | `0` = response only, `1` = default, `2` = full wire diagnostics |

## How it works

```
┌──────────────┐     JSON-RPC POST       ┌──────────────────┐
│  This Sample │ ──────────────────────>  │  Microsoft Graph │
│  (A2A Client)│ <──────────────────────  │  Copilot API     │
└──────────────┘   AgentMessage response  └──────────────────┘
```

1. **Auth**: Acquires a token via WAM or accepts a pre-obtained JWT
2. **A2A Client**: Creates an `A2AClient` from the [A2A .NET SDK](https://github.com/a2aproject/a2a-dotnet) pointed at the Graph RP endpoint
3. **Send**: Sends `message/send` (sync) or `message/stream` (streaming) JSON-RPC requests
4. **Receive**: Parses `AgentMessage` or `AgentTask` responses, extracts text and citations
5. **Multi-turn**: Maintains `contextId` across turns for conversation continuity

## A2A protocol compliance

| A2A Capability | Status | Notes |
|---------------|--------|-------|
| `message/send` (sync) | Available | Full request/response cycle |
| `message/stream` (SSE) | Available | Incremental streaming via `TaskStatusUpdateEvent` |
| Multi-turn (`contextId`) | Available | Conversation state maintained across turns |
| `TextPart` messages | Available | User and agent text messages |
| Citations | Available | Via Microsoft-specific `metadata["attributions"]` (see below) |
| Agent card (`/.well-known/agent.json`) | Coming soon | Connect to endpoint directly for now |
| Agent discovery / listing | Coming soon | Connects to M365 Copilot agent directly for now |

## Citations

Citations are delivered via a **Microsoft-specific extension** to the A2A protocol under `AgentMessage.Metadata["attributions"]`. This is not part of the [A2A spec](https://a2a-protocol.org) and is subject to change.

```json
[
  {
    "attributionType": "Citation",
    "attributionSource": "Model",
    "providerDisplayName": "Q3 Planning Meeting",
    "seeMoreWebUrl": "https://teams.microsoft.com/..."
  }
]
```

| Type | Meaning |
|------|---------|
| `Citation` | Source explicitly referenced in the response text (e.g., `[1]`) |
| `Annotation` | Entity recognized in the response but not numbered |

Use `-v 2` to see full citation details in the output.

### Parsing citations in code

```csharp
if (agentMessage.Metadata?.TryGetValue("attributions", out var attrs) == true
    && attrs.ValueKind == JsonValueKind.Array)
{
    foreach (var attr in attrs.EnumerateArray())
    {
        var name = attr.GetProperty("providerDisplayName").GetString();
        var url = attr.GetProperty("seeMoreWebUrl").GetString();
        Console.WriteLine($"  [{name}] {url}");
    }
}
```

## Wire diagnostics

Use `-v 2` to see full HTTP request/response details:

```
  > POST https://graph.microsoft.com/rp/workiq/
    Authorization: Bearer ...(3089c)
    Content-Type: application/json
    Body: {"jsonrpc":"2.0","method":"message/send","params":{...}}

  < 200 OK
    request-id: d7d0989c-...
```

## NuGet dependencies

| Package | Purpose |
|---------|---------|
| [`A2A`](https://www.nuget.org/packages/A2A/) (0.3.3-preview) | A2A protocol client |
| `Microsoft.Identity.Client` | MSAL token acquisition |
| `Microsoft.Identity.Client.Broker` | Windows WAM broker |
| `System.IdentityModel.Tokens.Jwt` | JWT decoding for diagnostics |

> **Note:** This sample uses A2A SDK v0.3.3-preview. The spec has since moved to v1.0. See the [migration guide](https://github.com/a2aproject/a2a-dotnet/blob/main/docs/migration-guide-v1.md) when upgrading.

## Resources

- [A2A Protocol Specification](https://a2a-protocol.org/latest/specification/)
- [A2A .NET SDK](https://github.com/a2aproject/a2a-dotnet)
- [Work IQ Overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/workiq-overview)
