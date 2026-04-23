# Work IQ A2A Raw Sample

A bare-minimum A2A client using only `HttpClient` and `System.Text.Json` — **no A2A SDK**. Shows exactly what goes over the wire when talking to a Work IQ agent.

This sample **calls the agent endpoint directly** — no agent card retrieval, no discovery, no capability negotiation. It assumes you already know the agent URL and sends JSON-RPC v0.3 messages to it.

Defaults target the **Work IQ Gateway** (`https://workiq.svc.cloud.microsoft/a2a/`). Use `--endpoint` + `--scope` to target Microsoft Graph or any other A2A endpoint.

For the SDK-based sample with agent-card handling, see [`../a2a/`](../a2a/).

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
3. **.NET 8 SDK** or later — [download](https://dotnet.microsoft.com/download/dotnet/8.0).

## Quick start

### Against the Work IQ Gateway (default — prod host)

```bash
dotnet run -- --token WAM --appid <APP_ID> --tenant <TENANT_ID>
```

Type a message, see a response, type `quit` to exit.

### Against the Work IQ Gateway, a specific ring (e.g., `ppe.`)

```bash
dotnet run -- --endpoint https://ppe.workiq.svc.cloud.dev.microsoft/a2a/ \
  --token WAM --appid <APP_ID> --tenant <TENANT_ID>
```

The raw sample takes the **full URL** in `--endpoint` (including the `/a2a/` path) — unlike the SDK sample, there are no gateway presets.

### Against Microsoft Graph

```bash
dotnet run -- --endpoint https://graph.microsoft.com/rp/workiq/ \
  --scope https://graph.microsoft.com/.default \
  --token WAM --appid <APP_ID> --tenant <TENANT_ID>
```

For Graph, override **both** `--endpoint` (path is different) and `--scope` (audience is Graph, not Work IQ).

### Streaming mode

Add `--stream` to switch from `message/send` to `message/stream`.

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
| `--scope, -s` | Token scope for WAM. Default: `api://workiq.svc.cloud.microsoft/.default`. For Graph use `https://graph.microsoft.com/.default`. |
| `--appid, -a` | Entra app client ID (required with `WAM`) |
| `--tenant, -T` | Tenant ID or domain. Required with `WAM` for single-tenant apps; defaults to `common` for multi-tenant. |
| `--account` | Account hint for WAM (e.g., `user@contoso.com`) |
| `--stream` | Use streaming mode (`message/stream` via SSE) |
| `--all-headers` | Print every response header (default: only diagnostic ones) |

## Wire format (what you'll see with a packet capture)

### Request body — sync (`message/send`)

```json
{
  "jsonrpc": "2.0",
  "id": "<uuid>",
  "method": "message/send",
  "params": {
    "message": {
      "kind": "message",
      "role": "user",
      "messageId": "<uuid>",
      "contextId": null,
      "parts": [{ "kind": "text", "text": "hello" }],
      "metadata": {
        "Location": { "timeZoneOffset": 480, "timeZone": "Pacific Standard Time" }
      }
    }
  }
}
```

### Response — `AgentMessage`

```json
{
  "jsonrpc": "2.0",
  "id": "<matches request id>",
  "result": {
    "kind": "message",
    "role": "agent",
    "contextId": "<uuid — use on next turn>",
    "parts": [{ "kind": "text", "text": "Today you have: ..." }],
    "metadata": {
      "attributions": [
        { "attributionType": "citation", "seeMoreWebUrl": "https://..." }
      ]
    }
  }
}
```

### Streaming — `message/stream`

Same request body with `"method": "message/stream"`. Response is `text/event-stream`; each event's `data:` line contains a JSON-RPC frame with `TaskStatusUpdateEvent` payloads. The sample accumulates `parts[*].text` across events.

## Sample-specific troubleshooting

| Symptom | Fix |
|---------|-----|
| `400 Invalid request, no valid route` | Your `--endpoint` path doesn't match a gateway-registered scope. Use `/a2a/` for Work IQ, `/rp/workiq/` for Graph. |
| `401 Unauthorized` | Token `aud` doesn't match the endpoint. Work IQ needs `api://workiq.svc.cloud.microsoft/.default`; Graph needs `https://graph.microsoft.com/.default`. |
| `403 Forbidden` without a scope message | User is missing the Microsoft 365 Copilot license. |

See the [root README](../../README.md#troubleshooting) for the full troubleshooting matrix.

## Why use this sample?

- **Debugging wire issues** — see exactly what the SDK sends, without indirection.
- **Porting to another language** — `Program.cs` is ~400 lines and does no magic; easy to translate.
- **Custom transport** — replace `HttpClient` with another HTTP library without wrestling with SDK abstractions.

For production client code, prefer the SDK-based sample ([`../a2a/`](../a2a/)) unless you have a specific reason not to.
