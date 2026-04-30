# Work IQ A2A Raw Sample

A bare-minimum A2A client using only `HttpClient` and `System.Text.Json` — **no A2A SDK**. Shows exactly what goes over the wire when talking to a Work IQ agent.

This sample **calls the agent endpoint directly** — no agent card retrieval, no discovery, no capability negotiation. It assumes you already know the agent URL and sends JSON-RPC v1.0 messages to it.

Defaults target the **Work IQ Gateway** (`https://workiq.svc.cloud.microsoft/a2a/`). Use `--endpoint` + `--scope` to target a different A2A endpoint.

> **Protocol version**: This sample uses the **A2A v1.0 JSON-RPC wire format** (PROTOJSON conventions: SCREAMING_SNAKE_CASE enums, no `kind` discriminators, named result wrappers). Work IQ also accepts v0.3 wire format via the `A2A-Version: 0.3` request header for callers that haven't migrated yet. The v1.0 spec also defines a REST binding (`POST /v1/message:send`); Work IQ may expose this in a future preview update.

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

Add `--stream` to switch from `SendMessage` to `SendStreamingMessage`.

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
   - `capabilities.streaming` — whether `SendStreamingMessage` is supported

2. **Message post** — same JSON-RPC shape as before, but POSTed to `agentCard.url` (read from step 1) instead of `--endpoint`.

If `--stream` is set but the agent's card has `capabilities.streaming = false`, the sample falls back to `SendMessage` automatically and prints a note.

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

#### How to find an agent ID

Use the [WorkIQ CLI](https://www.npmjs.com/package/@microsoft/workiq) to list the agents available to your signed-in user. The list command is currently behind an `experimental` flag:

```bash
npm install -g @microsoft/workiq        # or: dotnet tool install --global WorkIQ
workiq accept-eula
workiq config set experimental=true
workiq list-agents
```

You can also copy the agent ID from the address bar in the [Microsoft 365 Copilot Chat website](https://m365.cloud.microsoft/chat/) — the segment after `/chat/agent/`. Treat the ID as an opaque string.

### With a pre-obtained JWT (any platform)

```bash
dotnet run -- --token eyJ0eXAi...
```

> **macOS / Linux users:** WAM is only available on Windows. Use `--token <JWT>` with a pre-obtained token instead.

## Expected output

```
Connected to: https://workiq.svc.cloud.microsoft/a2a/
Mode: sync (SendMessage)
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
| `--agent-id, -A` | Invoke a specific agent (fetches `.well-known/agent-card.json` and POSTs to `agentCard.url`). See [*How to find an agent ID*](#how-to-find-an-agent-id) above. |
| `--show-wire` | Pretty-print raw JSON-RPC request/response bodies and each streaming SSE `data:` event as it arrives. Useful for protocol debugging. |
| `--stream` | Use streaming mode (`SendStreamingMessage` via SSE) |
| `--all-headers` | Print every response header (default: only diagnostic ones) |

## What goes over the wire

The server accepts **A2A v1.0** JSON-RPC. All requests POST to the base URL with the method inside the JSON-RPC body.

### Sync: `POST` with method `SendMessage`

```json
{
  "jsonrpc": "2.0",
  "id": "<request-guid>",
  "method": "SendMessage",
  "params": {
    "message": {
      "role": "ROLE_USER",
      "messageId": "<message-guid>",
      "parts": [{ "text": "What meetings do I have today?" }],
      "metadata": { "Location": { "timeZoneOffset": -480, "timeZone": "America/Los_Angeles" } }
    }
  }
}
```

Response is a JSON-RPC envelope with `result.task` containing the agent's task and a `contextId` for multi-turn:

```json
{
  "jsonrpc": "2.0",
  "id": "<request-guid>",
  "result": {
    "task": {
      "id": "<task-id>",
      "contextId": "ctx-1",
      "status": { "state": "TASK_STATE_COMPLETED" },
      "artifacts": [
        {
          "artifactId": "<artifact-id>",
          "name": "Answer",
          "parts": [{ "text": "Today you have 3 meetings: ..." }]
        }
      ]
    }
  }
}
```

### Streaming: `POST` with method `SendStreamingMessage`

Same JSON-RPC request with `"method": "SendStreamingMessage"`. Response is `text/event-stream` (SSE) where each event is a JSON-RPC response carrying one of `task`, `statusUpdate`, `artifactUpdate`, or `message`:

```
data: {"jsonrpc":"2.0","id":"...","result":{"statusUpdate":{"taskId":"<t>","contextId":"ctx-1","status":{"state":"TASK_STATE_WORKING"}}}}
data: {"jsonrpc":"2.0","id":"...","result":{"artifactUpdate":{"taskId":"<t>","contextId":"ctx-1","artifact":{"artifactId":"<a>","parts":[{"text":"You"}]}}}}
data: {"jsonrpc":"2.0","id":"...","result":{"artifactUpdate":{"taskId":"<t>","contextId":"ctx-1","artifact":{"artifactId":"<a>","parts":[{"text":"You have 3 meetings..."}]}}}}
data: {"jsonrpc":"2.0","id":"...","result":{"statusUpdate":{"taskId":"<t>","contextId":"ctx-1","status":{"state":"TASK_STATE_COMPLETED"}}}}
```

Text accumulates across `artifactUpdate` events (not incremental deltas). The sample diffs against the previous text to print only new content.

### Dual-shape extractor (rollout caveat)

The Sydney "answer-as-artifact" change rolled out recently. During the brief WW rollout window, some rings still emit response text in `result.task.status.message.parts` (legacy v0.3 location) instead of `result.task.artifacts[].parts` (new). The sample's `Helpers.ExtractText` reads both shapes — preferring artifacts and falling back to status.message — so output is correct on either ring. This fallback will be removed in a follow-up release once the rollout is complete.

### Key v1.0 protocol details

- **JSON-RPC envelope required** — every request must include `jsonrpc`, `id`, `method`, `params`.
- **POST to base URL** — the method (`SendMessage`, `SendStreamingMessage`) is inside the body, not in the URL path.
- **No `kind` discriminators** — parts are flat objects with field-presence (`{"text": "..."}` not `{"kind": "text", "text": "..."}`).
- **PROTOJSON enums** — roles use `ROLE_USER` / `ROLE_AGENT`; states use `TASK_STATE_WORKING` / `TASK_STATE_COMPLETED` / `TASK_STATE_FAILED` / etc.
- **Named result wrappers** — sync responses carry `result.task` or `result.message`; streaming events use `result.statusUpdate`, `result.artifactUpdate`, `result.task`, or `result.message`.
- **Backward compatibility** — set the `A2A-Version: 0.3` request header to opt back into the v0.3 wire format.

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
