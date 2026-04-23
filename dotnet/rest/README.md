# Work IQ REST Sample

A minimal, single-file interactive client for the [Microsoft 365 Copilot Chat REST API](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/overview) — the REST interface for conversational AI grounded in Microsoft 365 data.

Supports both **synchronous** and **streaming** (SSE) modes, against either the **Work IQ Gateway** or **Microsoft Graph**.

## API reference

| Operation | Method | Path (via Work IQ) | Path (via Graph) | Docs |
|-----------|--------|--------------------|------------------|------|
| Create conversation | `POST` | `/rest/beta/conversations` | `/beta/copilot/conversations` | [Docs](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotroot-post-conversations) |
| Chat (sync) | `POST` | `/rest/beta/conversations/{id}/chat` | `/beta/copilot/conversations/{id}/chat` | [Docs](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotconversation-chat) |
| Chat (stream) | `POST` | `/rest/beta/conversations/{id}/chatOverStream` | `/beta/copilot/conversations/{id}/chatOverStream` | [Docs](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/copilotconversation-chatoverstream) |

The sample appends the gateway-specific path to `--endpoint` automatically. You only need to supply the host.

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

### Against the Work IQ Gateway (default host `workiq.svc.cloud.microsoft`)

```bash
dotnet run -- --workiq --token WAM --appid <APP_ID> --tenant <TENANT_ID>
```

Type a message, see a response, type `quit` to exit.

### Against the Work IQ Gateway, a specific ring (e.g. `ppe.`)

```bash
dotnet run -- --workiq --endpoint https://ppe.workiq.svc.cloud.dev.microsoft \
  --token WAM --appid <APP_ID> --tenant <TENANT_ID>
```

`--endpoint` takes **host-only** (scheme + authority). The sample appends `/rest/beta` automatically.

### Against Microsoft Graph

```bash
dotnet run -- --graph --token WAM --appid <APP_ID> --tenant <TENANT_ID>
```

### Streaming mode

Add `--stream` to any of the above to switch from `/chat` (sync) to `/chatOverStream` (SSE).

## Expected output

```
── TOKEN ──
  aud              fdcc1f02-fc51-4226-8753-f668596af7f7
  appid            <APP_ID>
  tid              <TENANT_ID>
  name             <Your Name>
  scp              WorkIQAgent.Ask
  expires          ...

── READY — Synchronous mode (workiq) ──
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

If you see the `── TOKEN ──` block and the `aud`/`scp` match the gateway you're hitting, your auth is working. Anything after that is the server doing its thing.

## Flags

| Flag | Description |
|------|-------------|
| `--graph` / `--workiq` | Gateway selection. Exactly one required. |
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
  |-- POST /rest/beta/conversations --------->|  (creates conversation)
  |<-- 201 { "id": "conv-123" } --------------|
  |                                           |
  |-- POST .../conv-123/chat ---------------->|
  |   { "message": { "text": "..." } }        |
  |<-- 200 { "messages": [...] } -------------|  (complete response)
```

### Streaming mode (`--stream`)

Sends to `/chatOverStream`, which returns an SSE stream. The sample prints each `messages[*].text` delta as it arrives and keeps the final citations.

## Sample-specific troubleshooting

| Symptom | Fix |
|---------|-----|
| `400 Invalid request, no valid route` against Work IQ | Pass `--endpoint` as host-only; the sample appends the correct path |
| `403 Required scopes = [Sites.Read.All, ...]` against Graph | Admin needs to add the 7 Copilot Graph permissions. Run `scripts/admin-setup.sh --graph` |
| `401 Unauthorized` | Token audience doesn't match the gateway. Verify the `aud` claim in the `── TOKEN ──` block matches the gateway you picked. |

See the [root README](../../README.md#troubleshooting) for the full troubleshooting matrix (WAM setup, single-tenant apps, Copilot license, etc).

## Next steps

- Read `Program.cs` — everything is in one file, ~500 lines with comments.
- Try the [A2A sample](../a2a/) for the agent protocol variant.
- Fork and integrate: replace the interactive `Console.ReadLine` loop with your own event source, reuse `CreateConversation` + `ChatSync` / `ChatStream`.
