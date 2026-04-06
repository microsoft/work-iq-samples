# Work IQ A2A Raw Sample

A bare-minimum A2A client using only `HttpClient` and `System.Text.Json` — **no A2A SDK**. Shows exactly what goes over the wire when talking to a Work IQ agent.

This sample **calls the agent endpoint directly** — no agent card retrieval, no agent discovery, no capability negotiation. It assumes you know the agent's URL and sends messages to it. The default endpoint targets the **Microsoft 365 Copilot** agent via Graph RP.

> **Protocol version**: This sample uses the **A2A v0.3 JSON-RPC wire format**, which is what the Work IQ server currently supports. The A2A spec has since moved to v1.0 with a REST-style API (different URL paths, no JSON-RPC envelope). This sample will be updated to v1.0 when the server is upgraded.

Use this sample when you want to understand the A2A protocol at the HTTP level, or when you don't want to take a dependency on the [A2A .NET SDK](https://github.com/a2aproject/a2a-dotnet).

> **Prerequisites, authentication setup, and common issues** are covered in the [root README](../../README.md). Read that first.

## What's different from the `a2a/` sample?

| | `a2a/` (SDK) | `a2a-raw/` (this sample) |
|--|-------------|--------------------------|
| **Dependencies** | A2A NuGet SDK + MSAL | MSAL only |
| **Protocol handling** | SDK manages JSON-RPC, SSE parsing, types | Raw `HttpClient` + `JsonDocument` |
| **Lines of code** | ~480 | ~280 |
| **Best for** | Production apps, full A2A features | Learning, debugging, minimal integration |

## Quick start

```bash
dotnet build

# With a pre-obtained JWT token (any platform)
dotnet run -- --endpoint https://graph.microsoft.com/rp/workiq/ --token eyJ0eXAi...

# With WAM broker auth (Windows only)
dotnet run -- --endpoint https://graph.microsoft.com/rp/workiq/ --token WAM --appid <your-app-id>

# Streaming mode (SSE)
dotnet run -- --endpoint https://graph.microsoft.com/rp/workiq/ --token WAM --appid <your-app-id> --stream
```

> **macOS / Linux users:** WAM is only available on Windows. Use `--token <JWT>` with a pre-obtained token instead. See the [root README](../../README.md#authentication) for how to acquire a token.

## Parameters

| Flag | Description |
|------|-------------|
| `--endpoint`, `-e` | Agent URL (required) |
| `--token`, `-t` | Bearer JWT token, or `WAM` for Windows broker auth |
| `--appid`, `-a` | App client ID (required with WAM) |
| `--account` | Account hint (e.g., `user@contoso.com`) |
| `--stream` | Use streaming mode (SSE via `message/stream`) |

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

That's it — no A2A SDK, no JWT decoder, no Graph SDK.

## Resources

- [A2A Protocol Specification](https://a2a-protocol.org/latest/specification/)
- [Work IQ Overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/workiq-overview)
