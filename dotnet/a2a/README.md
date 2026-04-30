# Work IQ A2A Sample

A minimal, single-file interactive client for communicating with Work IQ agents using the [Agent-to-Agent (A2A) protocol](https://a2a-protocol.org).

Uses the [A2A .NET SDK](https://github.com/a2aproject/a2a-dotnet) (NuGet: [`A2A`](https://www.nuget.org/packages/A2A/)) for JSON-RPC transport. Sends synchronous (`SendMessage`) requests against the **Work IQ Gateway**.

> **Streaming responses are coming soon** and not yet supported by this sample.

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
     ../../scripts/admin-setup.sh
     # PowerShell
     ..\..\scripts\admin-setup.ps1
     ```
   - Otherwise, hand [`../../ADMIN_SETUP.md`](../../ADMIN_SETUP.md) to your admin. They'll give you an **App ID** and **Tenant ID**.
3. **.NET 8 SDK** or later — [download](https://dotnet.microsoft.com/download/dotnet/8.0).

## Quick start

### Default — talk to the Work IQ Gateway's default agent

```bash
dotnet run -- --token WAM --appid <APP_ID> --tenant <TENANT_ID>
```

Type a message, see a response, type `quit` to exit.

### Invoking a specific agent (`--agent-id`)

Without `--agent-id`, the sample posts directly to the gateway endpoint (the default agent). To invoke a specific agent, pass `--agent-id <id>`:

```bash
dotnet run -- --token WAM --agent-id <AGENT_ID> \
  --appid <APP_ID> --tenant <TENANT_ID>
```

The sample then:

1. Fetches the agent card from `{gateway}/{agent-id}/.well-known/agent-card.json` via the A2A SDK's `A2ACardResolver`.
2. Reads `agentCard.url` and `agentCard.name` from the response.
3. Uses `agentCard.url` (not the gateway endpoint) as the target for `A2AClient`.

<a id="how-to-find-an-agent-id"></a>
#### How to find an agent ID

Use the [WorkIQ CLI](https://www.npmjs.com/package/@microsoft/workiq) to list the agents available to your signed-in user. The list command is currently behind an `experimental` flag:

```bash
npm install -g @microsoft/workiq        # or: dotnet tool install --global WorkIQ
workiq accept-eula
workiq config set experimental=true
workiq list-agents
```

You can also copy the agent ID from the address bar in the [Microsoft 365 Copilot Chat website](https://m365.cloud.microsoft/chat/) — the segment after `/chat/agent/`. Treat the ID as an opaque string.

Without `--agent-id`, the sample posts to the gateway's default agent.

### With a pre-obtained JWT (any platform)

```bash
dotnet run -- --token eyJ0eXAi...
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

If the `── TOKEN ──` block shows `aud` matching the Work IQ Gateway and `scp` includes `WorkIQAgent.Ask`, auth is working.

## Flags

| Flag | Description |
|------|-------------|
| `--token, -t` | `WAM` for Windows broker auth, or a pre-obtained JWT string |
| `--appid, -a` | Entra app client ID (required with `WAM`) |
| `--tenant, -T` | Tenant ID or domain. Required with `WAM` for single-tenant apps; defaults to `common` for multi-tenant. |
| `--account` | Account hint for WAM (e.g., `user@contoso.com`) |
| `--agent-id, -A` | Invoke a specific agent (fetches `{gateway}/{agent-id}/.well-known/agent-card.json` and posts to `agentCard.url`). See [How to find an agent ID](#how-to-find-an-agent-id) above. |
| `--show-wire` | Pretty-print raw JSON-RPC request/response bodies. Independent of `--verbosity`. Useful for protocol debugging. |
| `--header, -H` | Custom request header (repeatable) |
| `--show-token` | Print the raw access token (use only for diagnostics — treat tokens as sensitive) |
| `-v, --verbosity` | `0` response only, `1` default, `2` full wire |

## How it works

```
┌──────────────┐   JSON-RPC POST    ┌──────────────────┐
│  This Sample │ ────────────────>  │  Work IQ Gateway │
│  (A2A Client)│ <────────────────  │  / Agent         │
└──────────────┘   AgentMessage     └──────────────────┘
                  or AgentTask
```

1. **Auth**: acquires a token via WAM or accepts a pre-obtained JWT.
2. **A2A Client**: creates an `A2AClient` from the A2A .NET SDK pointed at the gateway endpoint.
3. **Send**: `SendMessage` (sync).
4. **Receive**: parses `AgentMessage` or `AgentTask`; extracts text parts and citations from `metadata["attributions"]`.
5. **Multi-turn**: maintains `contextId` across turns for conversation continuity.

## A2A protocol compliance

| A2A Capability | Status | Notes |
|---------------|--------|-------|
| `SendMessage` (sync) | Available | Full request/response cycle |
| `SendStreamingMessage` (SSE) | Coming soon | Streaming responses are not yet supported by this sample |
| Multi-turn (`contextId`) | Available | Conversation state maintained across turns |
| Text parts (`Part.FromText`) | Available | User and agent text messages |
| Citations | Available | Via Microsoft-specific `metadata["attributions"]` (see below) |
| Agent card (`/.well-known/agent.json`) | Coming soon | Connects to the gateway endpoint directly for now |
| Agent discovery / listing | Coming soon | Connects to the gateway's default agent directly for now |

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

- This sample targets **A2A v1.0** wire format: SCREAMING_SNAKE_CASE enums (`ROLE_USER`, `TASK_STATE_COMPLETED`), flat field-presence parts (no `kind` discriminator), and named result wrappers (`result.task`, `result.message`). The method name is `SendMessage`.
- Answer text comes back in `Artifact.Parts`. `Status.Message` carries citation metadata, not the final answer text.
- Citations and annotations live in `Status.Message.Metadata["attributions"]`. A subsequent change will move citations to a `DataPart` on the artifact (with media type `application/vnd.workiq-reference`); the sample will be updated when that ships.
- **Streaming responses are coming soon** and not yet supported by this sample.

## Wire diagnostics

Use `-v 2` to see full HTTP request/response details:

```
  > POST https://workiq.svc.cloud.microsoft/a2a/
    Authorization: Bearer ...(3089c)
    Content-Type: application/json
    Body: {"jsonrpc":"2.0","id":"...","method":"SendMessage","params":{"message":{...}}}

  < 200 OK
    request-id: d7d0989c-...
```

## NuGet dependencies

| Package | Purpose |
|---------|---------|
| [`A2A`](https://www.nuget.org/packages/A2A/) (1.0.0-preview2) | A2A protocol client |
| `Microsoft.Identity.Client` | MSAL token acquisition |
| `Microsoft.Identity.Client.Broker` | Windows WAM broker |
| `System.IdentityModel.Tokens.Jwt` | JWT decoding for diagnostics |

> **Note:** This sample uses A2A SDK **v1.0.0-preview2**. For the v0.3 → v1.0 wire-format and API delta, see the [SDK migration guide](https://github.com/a2aproject/a2a-dotnet/blob/main/docs/migration-guide-v1.md). Work IQ also accepts v0.3 wire format via the `A2A-Version: 0.3` request header.

## Sample-specific troubleshooting

| Symptom | Fix |
|---------|-----|
| Empty response / `[Task ... — Working]` never completes | Increase the client timeout (`Timeout = TimeSpan.FromMinutes(5)` by default in `Program.cs`). Long-running grounded queries can take 30-60s in some environments. |
| `401 Unauthorized` | Token audience doesn't match the gateway. Check the `aud` claim in the `── TOKEN ──` block. |

See the [root README](../../README.md#troubleshooting) for the full troubleshooting matrix.

## Resources

- [A2A Protocol Specification](https://a2a-protocol.org/latest/specification/)
- [A2A .NET SDK](https://github.com/a2aproject/a2a-dotnet)
- [Work IQ Overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/workiq-overview)
