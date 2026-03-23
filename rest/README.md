# Work IQ REST Sample

A minimal, single-file interactive client for the [Microsoft 365 Copilot Chat API](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/overview) — the REST interface for conversational AI grounded in Microsoft 365 data.

Supports both **synchronous** and **streaming** (SSE) modes.

> **Prerequisites, authentication, and common issues** are covered in the [root README](../README.md). Read that first.

## API reference

| Operation | Method | Endpoint | Docs |
|-----------|--------|----------|------|
| Create conversation | `POST` | `/beta/copilot/conversations` | [Docs](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotroot-post-conversations) |
| Chat (sync) | `POST` | `/beta/copilot/conversations/{id}/chat` | [Docs](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotconversation-chat) |
| Chat (stream) | `POST` | `/beta/copilot/conversations/{id}/chatOverStream` | [Docs](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotconversation-chatoverstream) |

## Quick start

```bash
# Build
dotnet build

# Synchronous mode (default)
dotnet run -- --token WAM --appid <your-app-client-id>

# Streaming mode (SSE)
dotnet run -- --token WAM --appid <your-app-client-id> --stream

# With account hint
dotnet run -- --token WAM --appid <your-app-client-id> --account user@contoso.com
```

## Parameters

| Flag | Description |
|------|-------------|
| `--token`, `-t` | Bearer JWT token, or `WAM` for Windows broker auth |
| `--appid`, `-a` | Azure AD app client ID (required with `--token WAM`) |
| `--account` | Account hint for WAM (e.g. `user@contoso.com`) |
| `--stream` | Use streaming mode (`/chatOverStream` with SSE) |
| `--show-token` | Print the raw JWT after decoding |

## How it works

### Synchronous mode (default)

```
Client                              Microsoft Graph
  |                                       |
  |-- POST /beta/copilot/conversations -->|  (creates conversation)
  |<-- 201 { "id": "conv-123" } ---------|
  |                                       |
  |-- POST .../conv-123/chat ------------>|
  |   { "message": { "text": "..." } }   |
  |<-- 200 { "messages": [...] } ---------|  (complete response)
```

### Streaming mode (`--stream`)

```
Client                              Microsoft Graph
  |                                       |
  |-- POST /beta/copilot/conversations -->|
  |<-- 201 { "id": "conv-123" } ---------|
  |                                       |
  |-- POST .../conv-123/chatOverStream -->|
  |<-- 200 text/event-stream -------------|
  |    data: { "messages": [...] }        |  (partial)
  |    data: { "messages": [...] }        |  (more text)
  |    data: { "messages": [...] }        |  (complete)
```

Each SSE event contains the **full conversation state** (cumulative, not incremental). The sample computes deltas by comparing with the previous event's text to print only new content as it arrives.

## Multi-turn conversations

The sample reuses the conversation ID across turns. Each message continues the same conversation with full context:

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

- **No external SDK required.** Unlike the A2A sample, this calls the Graph REST API directly with `HttpClient`. Just JSON over HTTP.
- **Streaming is cumulative, not incremental.** Each SSE event contains the full conversation state. The sample computes deltas by diffing against the previous text.
- **Conversation state is `active` during streaming.** The `state` field transitions to `active` while tokens are being generated.
- **Graph Explorer doesn't support streaming.** Per [Microsoft docs](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/overview#known-limitations), use this sample or curl instead.

## NuGet dependencies

| Package | Purpose |
|---------|---------|
| `Microsoft.Identity.Client` | MSAL token acquisition |
| `Microsoft.Identity.Client.Broker` | Windows WAM broker |
| `System.IdentityModel.Tokens.Jwt` | JWT decoding for diagnostics |

No A2A SDK or Graph SDK needed — pure `HttpClient` + JSON.

## Resources

- [Chat API Overview](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/overview)
- [Create Conversation](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotroot-post-conversations)
- [Chat (sync)](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotconversation-chat)
- [Chat Over Stream (SSE)](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotconversation-chatoverstream)
- [Work IQ Overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/workiq-overview)
