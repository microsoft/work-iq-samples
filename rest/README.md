# WorkIQ REST Sample

A minimal, single-file interactive client for the [Microsoft 365 Copilot Chat API](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/overview) — the REST interface for conversational AI grounded in Microsoft 365 data.

Supports both **synchronous** and **streaming** (SSE) modes.

> **Preview (via Microsoft Graph):** During preview, WorkIQ is accessed through the Microsoft Graph API at `graph.microsoft.com`. When WorkIQ's dedicated infrastructure is available, the endpoint will move to `workiq.svc.cloud.dev.microsoft` with its own app registration (audience: `fdcc1f02-fc51-4226-8753-f668596af7f7`) and scopes. The code and instructions in this sample will be updated at that time.

## API Reference

| Operation | Method | Endpoint | Docs |
|-----------|--------|----------|------|
| Create conversation | `POST` | `/beta/copilot/conversations` | [Docs](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotroot-post-conversations) |
| Chat (sync) | `POST` | `/beta/copilot/conversations/{id}/chat` | [Docs](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotconversation-chat) |
| Chat (stream) | `POST` | `/beta/copilot/conversations/{id}/chatOverStream` | [Docs](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotconversation-chatoverstream) |

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- A [Microsoft 365 Copilot license](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/overview#licensing)
- An Azure AD app registration with **all 7** delegated permissions:

| Permission | Description |
|-----------|-------------|
| `Sites.Read.All` | Read SharePoint sites |
| `Mail.Read` | Read user mail |
| `People.Read.All` | Read people data |
| `OnlineMeetingTranscript.Read.All` | Read meeting transcripts |
| `Chat.Read` | Read Teams chats |
| `ChannelMessage.Read.All` | Read Teams channel messages |
| `ExternalItem.Read.All` | Read external connector items |

> All seven are required. See [permissions docs](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotconversation-chat#permissions).

## Usage

```bash
# Build
dotnet build

# Synchronous mode (default) — full response in one JSON payload
dotnet run -- --token WAM --appid <your-app-id>

# Streaming mode — incremental response via Server-Sent Events
dotnet run -- --token WAM --appid <your-app-id> --stream

# With account hint (skips account picker)
dotnet run -- --token WAM --appid <your-app-id> --account user@contoso.com

# With a pre-obtained JWT token
dotnet run -- --token eyJ0eXAi...

# Print token for reuse
dotnet run -- --token WAM --appid <your-app-id> --show-token
```

### Parameters

| Flag | Description |
|------|-------------|
| `--token`, `-t` | Bearer JWT token, or `WAM` for Windows broker auth |
| `--appid`, `-a` | Azure AD app client ID (required with `--token WAM`) |
| `--account` | Account hint for WAM (e.g. `user@contoso.com`) |
| `--stream` | Use streaming mode (`/chatOverStream` with SSE) |
| `--show-token` | Print the raw JWT after decoding |

## How It Works

### Synchronous Mode (default)

```
Client                          Microsoft Graph
  │                                   │
  ├── POST /beta/copilot/conversations ──►  (creates conversation)
  │◄── 201 { "id": "conv-123" } ──────┤
  │                                   │
  ├── POST .../conversations/conv-123/chat ──►
  │    { "message": { "text": "..." },      │
  │      "locationHint": { "timeZone": "..." } }
  │◄── 200 { "messages": [...] } ──────┤
  │    (complete response)             │
```

### Streaming Mode (`--stream`)

```
Client                          Microsoft Graph
  │                                   │
  ├── POST /beta/copilot/conversations ──►
  │◄── 201 { "id": "conv-123" } ──────┤
  │                                   │
  ├── POST .../conversations/conv-123/chatOverStream ──►
  │◄── 200 text/event-stream ─────────┤
  │    data: { "messages": [...] }     │  (partial)
  │    data: { "messages": [...] }     │  (more text)
  │    data: { "messages": [...] }     │  (complete)
  │                                   │
```

Each SSE `data:` event contains the full conversation state. The sample computes deltas to print only new text as it arrives.

## Multi-Turn Conversations

The sample reuses the conversation ID across turns. Each message you type continues the same conversation with full context:

```
You > What meetings do I have tomorrow?
Agent > You have 3 meetings scheduled...

You > Which one has the most attendees?
Agent > The "Q3 Planning" meeting has 12 attendees...
```

If an error occurs, the conversation is reset and a new one is created on the next message.

## Request/Response Format

### Request Body (`/chat` and `/chatOverStream`)

```json
{
  "message": { "text": "What meetings do I have tomorrow?" },
  "locationHint": { "timeZone": "America/Los_Angeles" }
}
```

### Response (`/chat` — synchronous)

```json
{
  "id": "conv-123",
  "state": "active",
  "messages": [
    { "text": "What meetings do I have tomorrow?", ... },
    { "text": "You have 3 meetings...", "attributions": [...], ... }
  ]
}
```

### Response (`/chatOverStream` — SSE)

```
data: { "id": "conv-123", "messages": [{ "text": "You have" }] }
data: { "id": "conv-123", "messages": [{ "text": "You have 3 meetings" }] }
data: { "id": "conv-123", "messages": [{ "text": "You have 3 meetings scheduled..." }] }
```

## Gotchas

### No external NuGet dependencies for the API
Unlike the A2A sample (which uses the `A2A` NuGet SDK), this sample calls the Graph REST API directly with `HttpClient`. No SDK needed — just JSON over HTTP.

### Token audience must be `https://graph.microsoft.com`
The Chat API is a native Microsoft Graph API. Tokens must have `aud: https://graph.microsoft.com`.

### Streaming response is cumulative, not incremental
Each SSE event contains the **full** conversation state (all messages, full text). The sample computes deltas by comparing with the previous event's text. This is different from typical LLM streaming where each chunk is incremental.

### Conversation state is "active" during streaming
The `state` field in SSE events is `active` while tokens are still being generated. The final event has the complete response.

### Graph Explorer doesn't support streaming
Per [Microsoft docs](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/overview#known-limitations), Graph Explorer doesn't support streamed conversations. Use this sample or curl instead.

### Platform support

| Platform | Auth method | Status |
|----------|------------|--------|
| **Windows** | WAM broker (silent SSO) | ✅ Tested |
| **macOS** | Interactive browser login | 🔄 Untested (should work) |
| **Linux / WSL** | Interactive browser login | 🔄 Untested (should work) |

On Windows, `--token WAM` uses the Windows Account Manager for silent SSO. On macOS/Linux, MSAL falls back to interactive browser authentication. You can also use `--token <JWT>` on any platform with a pre-obtained token.

### Token caching
Tokens are held **in memory only** for the duration of the session — they are not persisted to disk. Each run requires a fresh authentication. For production applications, consider using [MSAL token cache serialization](https://learn.microsoft.com/en-us/entra/msal/dotnet/how-to/token-cache-serialization) with platform-appropriate encryption (DPAPI on Windows, Keychain on macOS, keyring on Linux).

## Resources

- [Chat API Overview](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/overview)
- [Create Conversation](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotroot-post-conversations)
- [Chat (sync)](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotconversation-chat)
- [Chat Over Stream (SSE)](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotconversation-chatoverstream)
- [Permissions Reference](https://learn.microsoft.com/en-us/graph/permissions-reference)
