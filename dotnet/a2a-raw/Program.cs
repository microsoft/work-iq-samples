// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
//
// WorkIQ A2A Raw Sample — Minimal A2A client using only HttpClient + JSON
// No A2A SDK. Shows exactly what goes over the wire (JSON-RPC v0.3 format).
//
// Defaults target the Work IQ Gateway (`https://workiq.svc.cloud.microsoft/a2a/`).
// Override --endpoint + --scope to target any other A2A endpoint.
//
// Usage:
//   dotnet run -- --token <JWT|WAM> --appid <client-id> [--account user@tenant.com] [--stream]
//   dotnet run -- --endpoint <agent-url> --token <JWT> [--stream]

using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using WorkIQ.A2ARaw;

// ── Parse args ──────────────────────────────────────────────────────────

var parsed = Helpers.ParseArgs(args);
if (parsed.Error != null)
{
    Console.Error.WriteLine(parsed.Error);
    PrintUsage();
    return;
}

// Defaults target the Work IQ Gateway (A2A endpoint + matching scope).
// Override both --endpoint and --scope to target a different A2A endpoint.
string endpoint = parsed.Endpoint ?? "https://workiq.svc.cloud.microsoft/a2a/";
string scope = parsed.Scope ?? "api://workiq.svc.cloud.microsoft/.default";
string? token = parsed.Token, appId = parsed.AppId, account = parsed.Account, tenant = parsed.Tenant, agentId = parsed.AgentId;
bool stream = parsed.Stream, allHeaders = parsed.AllHeaders, showWire = parsed.ShowWire;

if (string.IsNullOrEmpty(token))
{
    PrintUsage();
    return;
}

// ── Auth ─────────────────────────────────────────────────────────────────

IPublicClientApplication? msalApp = null;
IAccount? msalAccount = null;

if (token.Equals("WAM", StringComparison.OrdinalIgnoreCase))
{
    if (string.IsNullOrEmpty(appId))
    {
        Console.Error.WriteLine("ERROR: --appid is required with --token WAM");
        return;
    }

    var (tok, app, acct) = await AcquireToken(appId, account, scope, tenant);
    token = tok;
    msalApp = app;
    msalAccount = acct;
}

// ── HTTP client ──────────────────────────────────────────────────────────

var http = new HttpClient();
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
// Opt in to A2A v1.0 wire format. Without this header, the server defaults
// to the v0.3 dispatcher and v1.0 method names ("SendMessage" /
// "SendStreamingMessage") return JSON-RPC -32601 "Method not found".
http.DefaultRequestHeaders.TryAddWithoutValidation("A2A-Version", "1.0");

// ── Agent card resolution (when --agent-id is set) ───────────────────────
//
// This sample deliberately does the agent-card fetch in raw HTTP + JSON to
// show what the wire interaction looks like. With --agent-id the sample:
//   1. GET {endpoint}/{agentId}/.well-known/agent-card.json
//   2. Parse the JSON to find:   url, name, capabilities.streaming
//   3. Replace the POST target with agentCard.url
//
// Without --agent-id the sample posts directly to the gateway endpoint
// (i.e., the default agent for that gateway).
if (!string.IsNullOrEmpty(agentId))
{
    var cardUri = $"{endpoint.TrimEnd('/')}/{agentId}/.well-known/agent-card.json";
    try
    {
        var cardRes = await http.GetAsync(cardUri);
        if (!cardRes.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"ERROR: failed to fetch agent card: {(int)cardRes.StatusCode} {cardRes.StatusCode} from {cardUri}");
            return;
        }

        var cardJson = await cardRes.Content.ReadAsStringAsync();
        using var cardDoc = JsonDocument.Parse(cardJson);
        var root = cardDoc.RootElement;

        var resolvedUrl = root.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("agent-card.json has no 'url' field");
        var resolvedName = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var streamingSupported = root.TryGetProperty("capabilities", out var caps)
            && caps.TryGetProperty("streaming", out var s) && s.GetBoolean();

        Console.WriteLine($"Agent card:");
        Console.WriteLine($"  id              {agentId}");
        Console.WriteLine($"  name            {resolvedName}");
        Console.WriteLine($"  url             {resolvedUrl}");
        Console.WriteLine($"  streaming       {streamingSupported}");

        if (stream && !streamingSupported)
        {
            Console.WriteLine("  note: agent does not advertise streaming; falling back to sync");
            stream = false;
        }

        endpoint = resolvedUrl;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ERROR: failed to resolve agent '{agentId}': {ex.Message}");
        return;
    }
}

string? contextId = null;

Console.WriteLine($"Connected to: {endpoint}");
Console.WriteLine($"Mode: {(stream ? "streaming (SendStreamingMessage)" : "sync (SendMessage)")}");
Console.WriteLine("Type a message. 'quit' to exit.\n");

// ── Chat loop ────────────────────────────────────────────────────────────

while (true)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("You > ");
    Console.ResetColor();

    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input) || input.Equals("quit", StringComparison.OrdinalIgnoreCase)) break;

    // Refresh token silently if using WAM
    if (msalApp != null && msalAccount != null)
    {
        try
        {
            var result = await msalApp.AcquireTokenSilent(
                new[] { scope }, msalAccount).ExecuteAsync();
            token = result.AccessToken;
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        catch { /* use existing token */ }
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("Agent > ");
    Console.ResetColor();

    // Show a simple progress indicator while waiting. Skipped when stdout
    // is redirected (e.g., piped to `tee` or a log file): Console.CursorVisible
    // and Console.SetCursorPosition both throw IOException on a non-TTY.
    var spinnerCts = new CancellationTokenSource();
    Task spinnerTask = Task.CompletedTask;
    if (!Console.IsOutputRedirected)
    {
        Console.CursorVisible = false;
        spinnerTask = Task.Run(async () =>
        {
            var frames = new[] { ".  ", ".. ", "..." , " ..", "  .", "   " };
            var i = 0;
            while (!spinnerCts.Token.IsCancellationRequested)
            {
                var f = frames[i++ % frames.Length];
                Console.Write(f);
                Console.SetCursorPosition(Console.CursorLeft - f.Length, Console.CursorTop);
                try { await Task.Delay(150, spinnerCts.Token); } catch { break; }
            }
            Console.Write("   ");
            Console.SetCursorPosition(Console.CursorLeft - 3, Console.CursorTop);
            Console.CursorVisible = true;
        });
    }

    try
    {
        // Build the A2A message (v1.0 format — no "kind" discriminators, ROLE_USER enum,
        // flat parts shape; SCREAMING_SNAKE_CASE on enum values).
        var message = new Dictionary<string, object?>
        {
            ["role"] = "ROLE_USER",
            ["messageId"] = Guid.NewGuid().ToString(),
            ["contextId"] = contextId,
            ["parts"] = new object[] { new { text = input } },
            ["metadata"] = new Dictionary<string, object>
            {
                ["Location"] = new { timeZoneOffset = (int)TimeZoneInfo.Local.BaseUtcOffset.TotalMinutes, timeZone = TimeZoneInfo.Local.Id },
            },
        };

        // Wrap in JSON-RPC envelope — v1.0 method names: SendMessage / SendStreamingMessage.
        var jsonRpcRequest = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString(),
            method = stream ? "SendStreamingMessage" : "SendMessage",
            @params = new { message },
        };

        var json = JsonSerializer.Serialize(jsonRpcRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        if (showWire) PrintWireBody("▶ POST request", json);

        if (stream)
        {
            await StreamResponse(http, endpoint, content, spinnerCts, spinnerTask, showWire);
        }
        else
        {
            await SyncResponse(http, endpoint, content, spinnerCts, spinnerTask, showWire);
        }
    }
    catch (Exception ex)
    {
        spinnerCts.Cancel();
        try { await spinnerTask; } catch { }
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n  ERROR: {ex.Message}");
        Console.ResetColor();
    }

    Console.WriteLine();
}

// ── Sync: POST to base URL with method "message/send" ────────────────────

async Task SyncResponse(HttpClient client, string ep, HttpContent body, CancellationTokenSource spinCts, Task spinTask, bool showWire)
{
    // v0.3: POST to the base URL (not /message:send) — method is inside the JSON-RPC body
    var res = await client.PostAsync(ep, body);

    // Stop spinner now that we have a response
    spinCts.Cancel();
    try { await spinTask; } catch { }

    PrintResponseHeaders(res);

    var responseBody = await res.Content.ReadAsStringAsync();

    if (showWire) PrintWireBody($"◀ {(int)res.StatusCode} {res.StatusCode} response", responseBody);

    if (!res.IsSuccessStatusCode)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  {responseBody}");
        Console.ResetColor();
        return;
    }

    if (string.IsNullOrWhiteSpace(responseBody))
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("  (empty response body)");
        Console.ResetColor();
        return;
    }

    // Parse JSON-RPC response: { "jsonrpc": "2.0", "id": "...", "result": { ... } }
    using var doc = JsonDocument.Parse(responseBody);

    if (doc.RootElement.TryGetProperty("error", out var err))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  JSON-RPC error: {err}");
        Console.ResetColor();
        return;
    }

    if (doc.RootElement.TryGetProperty("result", out var result))
    {
        // v1.0: contextId lives on result.task.contextId or result.message.contextId.
        contextId = ExtractContextId(result) ?? contextId;

        var text = Helpers.ExtractText(result);
        Console.WriteLine(text);
    }
    else
    {
        Console.WriteLine(responseBody);
    }
}

// Walks a v1.0 `result` envelope for the contextId. v1.0 places contextId on
// task / message / statusUpdate / artifactUpdate directly.
static string? ExtractContextId(JsonElement el)
{
    static string? Get(JsonElement e) => e.TryGetProperty("contextId", out var c) ? c.GetString() : null;

    foreach (var key in new[] { "task", "message", "statusUpdate", "artifactUpdate" })
    {
        if (el.TryGetProperty(key, out var inner) && Get(inner) is { } id)
            return id;
    }
    return Get(el);
}

// ── Streaming: POST to base URL with method "SendStreamingMessage" (SSE) ──

async Task StreamResponse(HttpClient client, string ep, HttpContent body, CancellationTokenSource spinCts, Task spinTask, bool showWire)
{
    // v1.0: POST to the base URL — method (`SendStreamingMessage`) is inside the JSON-RPC body.
    var request = new HttpRequestMessage(HttpMethod.Post, ep) { Content = body };
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

    var res = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

    // Stop spinner now that we have a response
    spinCts.Cancel();
    try { await spinTask; } catch { }

    PrintResponseHeaders(res);

    if (!res.IsSuccessStatusCode)
    {
        var errBody = await res.Content.ReadAsStringAsync();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  {errBody}");
        Console.ResetColor();
        return;
    }

    var responseStream = await res.Content.ReadAsStreamAsync();
    using var reader = new StreamReader(responseStream);

    // A2A v1.0 streaming semantics:
    //   result.task            -> initial submitted task (informational)
    //   result.statusUpdate    -> chain-of-thought OR terminal status; the
    //                             final event carries citation metadata
    //   result.artifactUpdate  -> answer chunks. Each event has an `append`
    //                             flag: true => append parts to the artifact
    //                             identified by artifactId; false => replace.
    //                             The full answer is the concatenation of all
    //                             append=true parts (with replaces applied).
    //   result.message         -> direct message reply (rare in this flow)
    var artifactBuffers = new Dictionary<string, StringBuilder>();
    var lastChainOfThought = "";

    while (!reader.EndOfStream)
    {
        var line = await reader.ReadLineAsync();
        if (line == null) break;

        // SSE format: lines starting with "data:" contain JSON-RPC responses
        if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
        var data = line["data:".Length..].Trim();
        if (string.IsNullOrEmpty(data)) continue;

        if (showWire) PrintWireBody("← SSE data", data);

        try
        {
            using var doc = JsonDocument.Parse(data);

            // Unwrap JSON-RPC: get the "result" field
            JsonElement payload;
            if (doc.RootElement.TryGetProperty("result", out var result))
                payload = result;
            else
                payload = doc.RootElement;

            // Extract contextId (v1.0: lives on task/message/statusUpdate/artifactUpdate)
            contextId = ExtractContextId(payload) ?? contextId;

            // Dispatch on event shape.
            if (payload.TryGetProperty("artifactUpdate", out var au))
            {
                if (au.TryGetProperty("artifact", out var artifact))
                {
                    var aId = artifact.TryGetProperty("artifactId", out var aIdProp) ? aIdProp.GetString() ?? "" : "";
                    var append = au.TryGetProperty("append", out var ap) && ap.ValueKind == JsonValueKind.True;

                    if (Helpers.TryGetParts(artifact, out var chunk))
                    {
                        if (!artifactBuffers.TryGetValue(aId, out var sb))
                        {
                            sb = new StringBuilder();
                            artifactBuffers[aId] = sb;
                        }
                        if (!append) sb.Clear();
                        sb.Append(chunk);

                        Console.Write(chunk);
                    }
                }
            }
            else if (payload.TryGetProperty("statusUpdate", out var su))
            {
                // Chain-of-thought / progress message (gray, on its own line),
                // OR terminal state (just signals end of stream).
                if (su.TryGetProperty("status", out var status))
                {
                    if (status.TryGetProperty("message", out var statusMsg) &&
                        Helpers.TryGetParts(statusMsg, out var thought) &&
                        thought != lastChainOfThought)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"\n  [{thought}]");
                        Console.ResetColor();
                        lastChainOfThought = thought;
                    }
                }
            }
            else if (payload.TryGetProperty("message", out var m) &&
                     Helpers.TryGetParts(m, out var msgText))
            {
                Console.Write(msgText);
            }
            // result.task -> initial; nothing to print at default verbosity.
        }
        catch { /* skip unparseable SSE events */ }
    }

    Console.WriteLine();
}

// ── WAM auth ─────────────────────────────────────────────────────────────

async Task<(string token, IPublicClientApplication app, IAccount? account)> AcquireToken(
    string clientId, string? accountHint, string scope, string? tenantId)
{
    var authority = string.IsNullOrEmpty(tenantId)
        ? "https://login.microsoftonline.com/common"
        : $"https://login.microsoftonline.com/{tenantId}";
    var builder = PublicClientApplicationBuilder.Create(clientId)
        .WithDefaultRedirectUri()
        .WithAuthority(authority);

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        builder.WithParentActivityOrWindow(ConsoleWindowHandle).WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows));
    }

    var app = builder.Build();
    var scopes = new[] { scope };

    AuthenticationResult result;
    try
    {
        if (!string.IsNullOrEmpty(accountHint))
        {
            var cached = (await app.GetAccountsAsync()).FirstOrDefault(
                a => a.Username?.Contains(accountHint, StringComparison.OrdinalIgnoreCase) == true);
            result = cached != null
                ? await app.AcquireTokenSilent(scopes, cached).ExecuteAsync()
                : await app.AcquireTokenInteractive(scopes).WithLoginHint(accountHint).ExecuteAsync();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            result = await app.AcquireTokenSilent(scopes, PublicClientApplication.OperatingSystemAccount).ExecuteAsync();
        }
        else
        {
            result = await app.AcquireTokenInteractive(scopes).ExecuteAsync();
        }
    }
    catch (MsalUiRequiredException)
    {
        result = await app.AcquireTokenInteractive(scopes).ExecuteAsync();
    }

    return (result.AccessToken, app, result.Account);
}

// ── Wire body logging (--show-wire) ──────────────────────────────────────

static void PrintWireBody(string label, string raw)
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"\n  {label}:");
    string body;
    try
    {
        using var doc = JsonDocument.Parse(raw);
        body = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }
    catch { body = raw; }

    foreach (var line in body.Split('\n')) Console.WriteLine($"      {line}");
    Console.ResetColor();
}

// ── Response headers ─────────────────────────────────────────────────────

void PrintResponseHeaders(HttpResponseMessage res)
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"  [{(int)res.StatusCode} {res.ReasonPhrase}]");

    if (allHeaders)
    {
        // Print all response headers + content headers
        foreach (var h in res.Headers)
            Console.WriteLine($"  {h.Key}: {string.Join(", ", h.Value)}");
        if (res.Content != null)
            foreach (var h in res.Content.Headers)
                Console.WriteLine($"  {h.Key}: {string.Join(", ", h.Value)}");
    }
    else
    {
        // Print key diagnostic headers only
        string[] diagnosticHeaders = ["request-id", "client-request-id", "x-ms-ags-diagnostic", "Date"];
        foreach (var name in diagnosticHeaders)
        {
            if (res.Headers.TryGetValues(name, out var values))
            {
                Console.WriteLine($"  {name}: {string.Join(", ", values)}");
            }
        }
    }

    Console.ResetColor();
}

// ── Helpers ──────────────────────────────────────────────────────────────

[DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
[DllImport("user32.dll", ExactSpelling = true)] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
static IntPtr ConsoleWindowHandle() { var c = GetConsoleWindow(); var r = GetAncestor(c, 3); return r != IntPtr.Zero ? r : c; }

void PrintUsage()
{
    Console.WriteLine("""
    Work IQ A2A Raw Sample — minimal A2A client, no SDK (JSON-RPC v0.3 wire format)

    Usage:
      dotnet run -- --token <JWT|WAM> [options]

    Required:
      --token, -t      Bearer JWT token, or 'WAM' for Windows broker auth

    Options:
      --endpoint, -e   Agent URL. Defaults to the Work IQ Gateway A2A endpoint:
                       https://workiq.svc.cloud.microsoft/a2a/
                       Override to target a different A2A endpoint.
      --agent-id, -A   Invoke a specific agent. The sample fetches
                       {endpoint}/{agent-id}/.well-known/agent-card.json,
                       reads agentCard.url, and POSTs JSON-RPC there.
                       Without --agent-id, the sample posts directly to
                       --endpoint (default agent for that gateway).
      --scope, -s      Token scope for WAM. Defaults to the Work IQ audience:
                       api://workiq.svc.cloud.microsoft/.default
      --appid, -a      App client ID (required with WAM)
      --account        Account hint (e.g., user@contoso.com)
      --tenant, -T     Tenant ID or domain (required with WAM for single-tenant apps;
                       defaults to 'common' for multi-tenant apps)
      --stream         Use streaming mode (SSE via message/stream)
      --all-headers    Print all response headers (default: key diagnostics only)
      --show-wire      Pretty-print raw JSON-RPC request/response bodies (and each
                       streaming SSE `data:` event as it arrives).

    Examples:
      # Work IQ Gateway (uses defaults)
      dotnet run -- -t WAM -a <appid>

      # List agents (discover IDs to use with --agent-id)

      # Invoke a specific agent
      dotnet run -- -t WAM -a <appid> --agent-id <AGENT_ID>

      # With a pre-obtained JWT
      dotnet run -- -t eyJ0eXAi... --stream
    """);
}
