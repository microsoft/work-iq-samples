# Work IQ A2A Raw Sample

A bare-minimum A2A client using only `HttpClient` and `System.Text.Json` — **no A2A SDK**. Shows exactly what goes over the wire when talking to a Work IQ agent.

This sample **calls the agent endpoint directly** — no agent card retrieval, no discovery, no capability negotiation. It assumes you already know the agent URL and sends JSON-RPC v0.3 messages to it.

Defaults target the **Work IQ Gateway** (`https://workiq.svc.cloud.microsoft/a2a/`). Use `--endpoint` + `--scope` to target a different A2A endpoint.

> **Protocol version**: This sample uses the **A2A v0.3 JSON-RPC wire format**, which is what the Work IQ server currently supports. The A2A spec has since moved to v1.0 with a REST-style API (different URL paths, no JSON-RPC envelope). This sample will be updated to v1.0 when the server is upgraded.

Use this sample when you want to understand the A2A protocol at the HTTP level, or when you don't want to take a dependency on the [A2A .NET SDK](https://github.com/a2aproject/a2a-dotnet). For the SDK-based sample with agent-card handling, see [`../a2a/`](../a2a/).

## What's different from the `a2a/` sample?

| | `a2a/` (SDK) | `a2a-raw/` (this sample) |
|--|-------------|--------------------------|
| **Dependencies** | A2A NuGet SDK + MSAL | MSAL only |
| **Protocol handling** | SDK manages JSON-RPC, SSE parsing, types | Raw `HttpClient` + `JsonDocument` |
| **Lines of code** | ~480 | ~280 |
| **Best for** | Production apps, full A2A features | Learning, debugging, minimal integration |

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
3. **.NET 10 SDK** or later — [download](https://dotnet.microsoft.com/download/dotnet/10.0).

## Quick start

### Against the Work IQ Gateway (default — prod host)

```bash
dotnet run -- --token WAM --appid <APP_ID> --tenant <TENANT_ID>
```

Type a message, see a response, type `quit` to exit.

### Streaming mode

Add `--stream` to switch from `message/send` to `message/stream`.

### Invoking a specific agent (`--agent-id`)

Without `--agent-id`, the sample POSTs directly to `--endpoint` (the gateway's default agent). To target a specific agent:

```bash
dotnet run -- --agent-id <AGENT_ID> --token WAM --appid <APP_ID> --tenant <TENANT_ID>
```

The sample then does **two raw HTTP calls** — illustrating exactly what a non-.NET / no-SDK port would do:

1. **Agent card fetch**:
   ```
   GET {endpoint}/{agent-id}/.well-known/agent-card.json
   Authorization: Bearer <token>
   ```
   Response is the standard A2A agent card JSON. The sample parses three fields:
   - `url` — where to POST messages for this agent
   - `name` — friendly name (for logging)
   - `capabilities.streaming` — whether `message/stream` is supported

2. **Message post** — same JSON-RPC shape as before, but POSTed to `agentCard.url` (read from step 1) instead of `--endpoint`.

If `--stream` is set but the agent's card has `capabilities.streaming = false`, the sample falls back to `message/send` automatically and prints a note.

#### Agent card wire format (what the GET returns)

```json
{
  "name": "Researcher Agent",
  "description": "...",
  "url": "https://substrate.office.com/m365Copilot/agents/<agent-id>/",
  "version": "1.0",
  "capabilities": {
    "streaming": true,
    "pushNotifications": false
  },
  "defaultInputModes": ["text/plain"],
  "defaultOutputModes": ["text/plain"],
  "skills": [...]
}
```

This is the [A2A AgentCard schema](https://a2a-protocol.org/latest/specification/#agent-card). Useful as a porting reference if you're implementing this in another language.

#### How to find an agent ID — `--list-agents`

Agent IDs are stable identifiers exposed by the gateway's agent registry (`{endpoint}/.agents`, a Work IQ / Sydney extension — not part of the A2A spec). Pass `--list-agents` to fetch and print the registry, then exit:

```bash
dotnet run -- --token WAM --appid <APP_ID> --tenant <TENANT_ID> --list-agents
```

The sample does a single `GET {endpoint}/.agents` with the bearer token and prints the `{agentId, name, provider}` rows. Sample output:

```
Agents at https://workiq.svc.cloud.microsoft/a2a/:

  AGENT ID                  NAME              PROVIDER
  bizchat-as-gpt-scenario   BizChat           Microsoft
  researcher-v1             Researcher        Microsoft

5 agents.
```

Equivalent raw curl:

```bash
curl -H "Authorization: Bearer <token>" {endpoint}/.agents
```

### With a pre-obtained JWT (any platform)

```bash
dotnet run -- --token eyJ0eXAi...
```

> **macOS / Linux users:** WAM is only available on Windows. Use `--token <JWT>` with a pre-obtained token instead.

## Expected output

```
Connected to: https://workiq.svc.cloud.microsoft/a2a/
Mode: sync (message/send)
Type a message. 'quit' to exit.

You > What's on my schedule today?
Agent > Today you have:
  - 9:00 AM — team standup
  - 11:00 AM — review with Dana
  - 2:00 PM — customer call
  [200 OK]
  request-id: a1b2c3d4-...
  x-ms-ags-diagnostic: {"ServerInfo":{"DataCenter":"...","Slice":"R"}}

You > quit
```

## Flags

| Flag | Description |
|------|-------------|
| `--token, -t` | `WAM` for Windows broker auth, or a pre-obtained JWT string. **Required.** |
| `--endpoint, -e` | Full agent URL. Default: `https://workiq.svc.cloud.microsoft/a2a/` |
| `--scope, -s` | Token scope for WAM. Default: `api://workiq.svc.cloud.microsoft/.default` |
| `--appid, -a` | Entra app client ID (required with `WAM`) |
| `--tenant, -T` | Tenant ID or domain. Required with `WAM` for single-tenant apps; defaults to `common` for multi-tenant. |
| `--account` | Account hint for WAM (e.g., `user@contoso.com`) |
| `--agent-id, -A` | Invoke a specific agent (fetches `.well-known/agent-card.json` and POSTs to `agentCard.url`) |
| `--list-agents` | GET `{endpoint}/.agents` and print, then exit (no chat loop). Use to discover agent IDs. |
| `--stream` | Use streaming mode (`message/stream` via SSE) |
| `--all-headers` | Print every response header (default: only diagnostic ones) |

## What goes over the wire

The server uses **A2A v0.3** which is JSON-RPC based. All requests POST to the base URL with the method inside the JSON-RPC body.

### Sync: `POST` with method `message/send`

```json
{
  "jsonrpc": "2.0",
  "id": "unique-request-id",
  "method": "message/send",
  "params": {
    "message": {
      "kind": "message",
      "role": "user",
      "messageId": "guid",
      "parts": [{ "kind": "text", "text": "What meetings do I have today?" }],
      "contextId": null,
      "metadata": { "Location": { "timeZoneOffset": -480, "timeZone": "America/Los_Angeles" } }
    }
  }
}
```

Response is a JSON-RPC response with `result` containing the agent's message and `contextId` for multi-turn.

### Streaming: `POST` with method `message/stream`

Same JSON-RPC request with `"method": "message/stream"`. Response is `text/event-stream` (SSE) where each event is a JSON-RPC response:

```
data: {"jsonrpc":"2.0","id":"...","result":{"status":{"state":"working","message":{"parts":[{"kind":"text","text":"You"}]}},"contextId":"ctx-1"}}
data: {"jsonrpc":"2.0","id":"...","result":{"status":{"state":"working","message":{"parts":[{"kind":"text","text":"You have"}]}},"contextId":"ctx-1"}}
data: {"jsonrpc":"2.0","id":"...","result":{"status":{"state":"completed","message":{"parts":[{"kind":"text","text":"You have 3 meetings..."}]}},"contextId":"ctx-1"}}
```

Text accumulates across events (not incremental deltas). The sample diffs against the previous text to print only new content.

### Key v0.3 protocol details

- **JSON-RPC envelope required**: Every request must include `jsonrpc`, `id`, `method`, `params`
- **POST to base URL**: The method (`message/send`, `message/stream`) is inside the body, not in the URL path
- **`kind` discriminators required**: Messages need `"kind": "message"`, parts need `"kind": "text"` — server rejects without these

## NuGet dependencies

| Package | Purpose |
|---------|---------|
| `Microsoft.Identity.Client` | MSAL token acquisition |
| `Microsoft.Identity.Client.Broker` | Windows WAM broker |

That's it — no A2A SDK, no JWT decoder.

## Sample-specific troubleshooting

| Symptom | Fix |
|---------|-----|
| `400 Invalid request, no valid route` | Your `--endpoint` path doesn't match a gateway-registered scope. Use `/a2a/` for the Work IQ Gateway. |
| `401 Unauthorized` | Token `aud` doesn't match the endpoint. The Work IQ Gateway needs `api://workiq.svc.cloud.microsoft/.default`. |
| `403 Forbidden` without a scope message | User is missing the Microsoft 365 Copilot license. |

See the [root README](../../README.md#troubleshooting) for the full troubleshooting matrix.

## Resources

- [A2A Protocol Specification](https://a2a-protocol.org/latest/specification/)
- [Work IQ Overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/workiq-overview)
