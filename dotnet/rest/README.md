# Work IQ REST Sample

A minimal, single-file interactive client for the [Microsoft 365 Copilot Chat REST API](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/overview) — the REST interface for conversational AI grounded in Microsoft 365 data.

Supports both **synchronous** and **streaming** (SSE) modes against the **Work IQ Gateway**.

## API reference

| Operation | Method | Path | Docs |
|-----------|--------|------|------|
| Create conversation | `POST` | `/rest/beta/conversations` | [Docs](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotroot-post-conversations) |
| Chat (sync) | `POST` | `/rest/beta/conversations/{id}/chat` | [Docs](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotconversation-chat) |
| Chat (stream) | `POST` | `/rest/beta/conversations/{id}/chatOverStream` | [Docs](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotconversation-chatoverstream) |

The sample appends `/rest/beta` to `--endpoint` automatically. You only need to supply the host.

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

### Default — talk to the Work IQ Gateway

```bash
dotnet run -- --token WAM --appid <APP_ID> --tenant <TENANT_ID>
```

Type a message, see a response, type `quit` to exit.

### Streaming mode

Add `--stream` to switch from `/chat` (sync) to `/chatOverStream` (SSE).

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

── READY — Synchronous mode (Work IQ Gateway) ──
Type a message. 'quit' to exit.

You > What's on my calendar today?
  Creating conversation...
  Conversation: a1b2c3d4-...
Agent > Here's your schedule:
  - 9:00 AM — ...
  Citations: 3  Annotations: 2
  (1234 ms)

You > quit
```

If the `── TOKEN ──` block shows `aud` matching the Work IQ Gateway and `scp` includes `WorkIQAgent.Ask`, your auth is working.

## Flags

| Flag | Description |
|------|-------------|
| `--token, -t` | `WAM` for Windows broker auth, or a pre-obtained JWT string |
| `--appid, -a` | Entra app client ID (required with `WAM`) |
| `--tenant, -T` | Tenant ID or domain. Required with `WAM` for single-tenant apps; defaults to `common` for multi-tenant. |
| `--account` | Account hint for WAM (e.g., `user@contoso.com`) |
| `--endpoint, -e` | Override the gateway host (scheme + authority only, no path) |
| `--stream` | Use streaming mode (`/chatOverStream` via SSE) |
| `--header, -H` | Custom request header, e.g., `-H "x-foo: bar"` (repeatable) |
| `--show-token` | Print the raw JWT after decoding |
| `-v, --verbosity` | `0` response only, `1` default, `2` full wire, `3` all response headers |

## How it works

### Synchronous mode

```
Client                                     Gateway
  |                                           |
  |-- POST .../conversations ----------------->|  (creates conversation)
  |<-- 201 { "id": "conv-123" } --------------|
  |                                           |
  |-- POST .../conv-123/chat ---------------->|
  |   { "message": { "text": "..." } }        |
  |<-- 200 { "messages": [...] } -------------|  (complete response)
```

### Streaming mode (`--stream`)

```
Client                                     Gateway
  |                                           |
  |-- POST .../conversations ----------------->|
  |<-- 201 { "id": "conv-123" } --------------|
  |                                           |
  |-- POST .../conv-123/chatOverStream ------->|
  |<-- 200 text/event-stream -----------------|
  |    data: { "messages": [...] }            |  (partial)
  |    data: { "messages": [...] }            |  (more text)
  |    data: { "messages": [...] }            |  (complete)
```

Each SSE event contains the **full conversation state** (cumulative, not incremental). The sample computes deltas by comparing against the previous event's text and prints only the new content as it arrives.

## Multi-turn conversations

The sample reuses the conversation ID across turns — each message continues the same conversation with full context:

```
You > What meetings do I have tomorrow?
Agent > You have 3 meetings scheduled...

You > Which one has the most attendees?
Agent > The "Q3 Planning" meeting has 12 attendees...
```

## Request/response format

### Request body

```json
{
  "message": { "text": "What meetings do I have tomorrow?" },
  "locationHint": { "timeZone": "America/Los_Angeles" }
}
```

### Sync response

```json
{
  "id": "conv-123",
  "state": "active",
  "messages": [
    { "text": "What meetings do I have tomorrow?" },
    { "text": "You have 3 meetings...", "attributions": [...] }
  ]
}
```

### Streaming response (SSE)

```
data: { "id": "conv-123", "messages": [{ "text": "You have" }] }
data: { "id": "conv-123", "messages": [{ "text": "You have 3 meetings" }] }
data: { "id": "conv-123", "messages": [{ "text": "You have 3 meetings scheduled..." }] }
```

## REST-specific notes

- **No external SDK required.** Unlike the A2A sample, this calls the REST API directly with `HttpClient`. Just JSON over HTTP.
- **Streaming is cumulative, not incremental.** Each SSE event contains the full conversation state. The sample computes deltas by diffing against the previous text.

## NuGet dependencies

| Package | Purpose |
|---------|---------|
| `Microsoft.Identity.Client` | MSAL token acquisition |
| `Microsoft.Identity.Client.Broker` | Windows WAM broker |
| `System.IdentityModel.Tokens.Jwt` | JWT decoding for diagnostics |

No A2A SDK needed — pure `HttpClient` + JSON.

## Sample-specific troubleshooting

| Symptom | Fix |
|---------|-----|
| `400 Invalid request, no valid route` | Pass `--endpoint` as host-only; the sample appends `/rest/beta` |
| `401 Unauthorized` | Token audience doesn't match the Work IQ Gateway. Verify the `aud` claim in the `── TOKEN ──` block. |

See the [root README](../../README.md#troubleshooting) for the full troubleshooting matrix (WAM setup, single-tenant apps, Copilot license, etc).

## Resources

- [Chat API Overview](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/overview)
- [Create Conversation](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotroot-post-conversations)
- [Chat (sync)](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotconversation-chat)
- [Chat Over Stream (SSE)](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotconversation-chatoverstream)
- [Work IQ Overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/workiq-overview)
