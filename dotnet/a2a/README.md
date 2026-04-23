# Work IQ A2A Sample

A minimal, single-file interactive client for communicating with Work IQ agents using the [Agent-to-Agent (A2A) protocol](https://a2a-protocol.org).

Uses the [A2A .NET SDK](https://github.com/a2aproject/a2a-dotnet) (NuGet: [`A2A`](https://www.nuget.org/packages/A2A/)) for JSON-RPC transport. Supports both synchronous (`message/send`) and streaming (`message/stream`) modes, against either the **Work IQ Gateway** or **Microsoft Graph**.

For the lower-level sample with no SDK (raw HTTP + JSON), see [`../a2a-raw/`](../a2a-raw/).

## What is A2A?

The **Agent-to-Agent (A2A) Protocol** is an open standard for communication between AI agents. It defines JSON-RPC methods for sending messages, managing tasks, and streaming responses via Server-Sent Events.

- [A2A Protocol Specification](https://a2a-protocol.org/latest/specification/)
- [A2A .NET SDK](https://github.com/a2aproject/a2a-dotnet)

## Prerequisites

1. **Microsoft 365 Copilot license** on your test user.
2. **An Entra app registration** configured with the right permissions and redirect URIs. One-time task.
   - If you're the tenant admin:
     ```bash
     # Bash
     ../../scripts/admin-setup.sh --workiq    # or --graph or --both
     # PowerShell
     ..\..\scripts\admin-setup.ps1 -Gateway WorkIQ
     ```
   - Otherwise, hand [`../../ADMIN_SETUP.md`](../../ADMIN_SETUP.md) to your admin. They'll give you an **App ID** and **Tenant ID**.
3. **.NET 10 SDK** or later — [download](https://dotnet.microsoft.com/download/dotnet/10.0).

## Quick start

### Against the Work IQ Gateway (default host `workiq.svc.cloud.microsoft`)

```bash
dotnet run -- --workiq --token WAM --appid <APP_ID> --tenant <TENANT_ID>
```

Type a message, see a response, type `quit` to exit.

### Against the Work IQ Gateway, a specific ring (e.g., `ppe.`)

```bash
dotnet run -- --workiq --endpoint https://ppe.workiq.svc.cloud.dev.microsoft \
  --token WAM --appid <APP_ID> --tenant <TENANT_ID>
```

`--endpoint` takes **host-only** (scheme + authority). The sample preserves the gateway's path (`/a2a/`).

### Against Microsoft Graph

```bash
dotnet run -- --graph --token WAM --appid <APP_ID> --tenant <TENANT_ID>
```

### Streaming mode

Add `--stream` to switch from `message/send` (sync) to `message/stream` (SSE).

### With a pre-obtained JWT (any platform)

```bash
dotnet run -- --graph --token eyJ0eXAi...
```

> **macOS / Linux users:** WAM is only available on Windows. Use `--token <JWT>` with a pre-obtained token instead.

## Expected output

```
── TOKEN ──
  aud              fdcc1f02-fc51-4226-8753-f668596af7f7
  appid            <APP_ID>
  tid              <TENANT_ID>
  name             <Your Name>
  scp              WorkIQAgent.Ask
  expires          ...

── READY — Work IQ Gateway — Sync — https://workiq.svc.cloud.microsoft/a2a/ ──
Type a message. 'quit' to exit.

You > Summarize my recent emails from alice.
Agent > You've exchanged 8 emails with Alice this week. Key threads:
  - ...
  Citations: 4  Annotations: 1
  (2145 ms)

You > quit
```

If the `── TOKEN ──` block shows an `aud` that matches the gateway and `scp` includes `WorkIQAgent.Ask` (or the 7 Graph scopes), auth is working.

## Flags

| Flag | Description |
|------|-------------|
| `--graph` / `--workiq` | Gateway selection. Exactly one required. |
| `--token, -t` | `WAM` for Windows broker auth, or a pre-obtained JWT string |
| `--appid, -a` | Entra app client ID (required with `WAM`) |
| `--tenant, -T` | Tenant ID or domain. Required with `WAM` for single-tenant apps; defaults to `common` for multi-tenant. |
| `--account` | Account hint for WAM (e.g., `user@contoso.com`) |
| `--endpoint, -e` | Override the gateway host (scheme + authority only, no path) |
| `--stream` | Use streaming mode (`message/stream` via SSE) |
| `--header, -H` | Custom request header (repeatable) |
| `--show-token` | Print the raw JWT after decoding |
| `-v, --verbosity` | `0` response only, `1` default, `2` full wire |

## How it works

```
┌──────────────┐   JSON-RPC POST    ┌──────────────────┐
│  This Sample │ ────────────────>  │  Gateway / Agent │
│  (A2A Client)│ <────────────────  │  (Work IQ or     │
└──────────────┘   AgentMessage     │   Graph RP)      │
                  or AgentTask      └──────────────────┘
```

1. **Auth**: acquires a token via WAM or accepts a pre-obtained JWT.
2. **A2A Client**: creates an `A2AClient` from the A2A .NET SDK pointed at the gateway endpoint.
3. **Send**: `message/send` (sync) or `message/stream` (streaming).
4. **Receive**: parses `AgentMessage` or `AgentTask`; extracts text parts and citations from `metadata["attributions"]`.
5. **Multi-turn**: maintains `contextId` across turns for conversation continuity.

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

## A2A protocol notes

- Messages use the `kind` discriminator (v0.3 format).
- Text parts come back as `TextPart`; citations and annotations live in message `Metadata["attributions"]`.
- The sample reconstructs the full accumulated text from streaming chunks by prefix-matching (Work IQ sends cumulative text per chunk, not deltas).

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

## Sample-specific troubleshooting

| Symptom | Fix |
|---------|-----|
| `400 Invalid request, no valid route` against Work IQ | Pass `--endpoint` as host-only; the sample appends `/a2a/` |
| Empty response / `[Task ... — Working]` never completes | Increase the client timeout (`Timeout = TimeSpan.FromMinutes(5)` by default in `Program.cs`). Long-running grounded queries can take 30-60s on some rings. |
| `401 Unauthorized` | Token audience doesn't match the gateway. Check the `aud` claim in the `── TOKEN ──` block. |

See the [root README](../../README.md#troubleshooting) for the full troubleshooting matrix.

## Resources

- [A2A Protocol Specification](https://a2a-protocol.org/latest/specification/)
- [A2A .NET SDK](https://github.com/a2aproject/a2a-dotnet)
- [Work IQ Overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/workiq-overview)
