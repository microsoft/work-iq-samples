// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
//
// WorkIQ A2A Raw Sample — Minimal A2A client using only HttpClient + JSON
// No A2A SDK. Shows exactly what goes over the wire (JSON-RPC v0.3 format).
//
// Usage:
//   dotnet run -- --endpoint <agent-url> --token WAM --appid <client-id> [--account user@tenant.com] [--stream]
//   dotnet run -- --endpoint <agent-url> --token <JWT> [--stream]
//
// Example (M365 Copilot via Graph RP):
//   dotnet run -- --endpoint https://graph.microsoft.com/rp/workiq/ --token WAM --appid <id>

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

string? endpoint = parsed.Endpoint, token = parsed.Token, appId = parsed.AppId, account = parsed.Account;
bool stream = parsed.Stream, allHeaders = parsed.AllHeaders;

if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(token))
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

    var (tok, app, acct) = await AcquireToken(appId, account);
    token = tok;
    msalApp = app;
    msalAccount = acct;
}

// ── HTTP client ──────────────────────────────────────────────────────────

var http = new HttpClient();
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

string? contextId = null;

Console.WriteLine($"Connected to: {endpoint}");
Console.WriteLine($"Mode: {(stream ? "streaming (message/stream)" : "sync (message/send)")}");
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
                new[] { "https://graph.microsoft.com/.default" }, msalAccount).ExecuteAsync();
            token = result.AccessToken;
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        catch { /* use existing token */ }
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("Agent > ");
    Console.ResetColor();

    // Show a simple progress indicator while waiting
    Console.CursorVisible = false;
    var spinnerCts = new CancellationTokenSource();
    var spinnerTask = Task.Run(async () =>
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

    try
    {
        // Build the A2A message (v0.3 format — requires "kind" discriminators)
        var message = new Dictionary<string, object?>
        {
            ["kind"] = "message",
            ["role"] = "user",
            ["messageId"] = Guid.NewGuid().ToString(),
            ["contextId"] = contextId,
            ["parts"] = new object[] { new { kind = "text", text = input } },
            ["metadata"] = new Dictionary<string, object>
            {
                ["Location"] = new { timeZoneOffset = (int)TimeZoneInfo.Local.BaseUtcOffset.TotalMinutes, timeZone = TimeZoneInfo.Local.Id },
            },
        };

        // Wrap in JSON-RPC envelope — v0.3 sends method inside the body, POSTs to base URL
        var jsonRpcRequest = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString(),
            method = stream ? "message/stream" : "message/send",
            @params = new { message },
        };

        var json = JsonSerializer.Serialize(jsonRpcRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        if (stream)
        {
            await StreamResponse(http, endpoint, content, spinnerCts, spinnerTask);
        }
        else
        {
            await SyncResponse(http, endpoint, content, spinnerCts, spinnerTask);
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

async Task SyncResponse(HttpClient client, string ep, HttpContent body, CancellationTokenSource spinCts, Task spinTask)
{
    // v0.3: POST to the base URL (not /message:send) — method is inside the JSON-RPC body
    var res = await client.PostAsync(ep, body);

    // Stop spinner now that we have a response
    spinCts.Cancel();
    try { await spinTask; } catch { }

    PrintResponseHeaders(res);

    var responseBody = await res.Content.ReadAsStringAsync();

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
        // Extract contextId for multi-turn
        if (result.TryGetProperty("contextId", out var ctx))
            contextId = ctx.GetString();
        else if (result.TryGetProperty("status", out var status) &&
                 status.TryGetProperty("message", out var msg) &&
                 msg.TryGetProperty("contextId", out var sCtx))
            contextId = sCtx.GetString();

        var text = Helpers.ExtractText(result);
        Console.WriteLine(text);
    }
    else
    {
        Console.WriteLine(responseBody);
    }
}

// ── Streaming: POST to base URL with method "message/stream" (SSE) ───────

async Task StreamResponse(HttpClient client, string ep, HttpContent body, CancellationTokenSource spinCts, Task spinTask)
{
    // v0.3: POST to the base URL — method is inside the JSON-RPC body
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

    var previousText = "";

    while (!reader.EndOfStream)
    {
        var line = await reader.ReadLineAsync();
        if (line == null) break;

        // SSE format: lines starting with "data:" contain JSON-RPC responses
        if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
        var data = line["data:".Length..].Trim();
        if (string.IsNullOrEmpty(data)) continue;

        try
        {
            using var doc = JsonDocument.Parse(data);

            // Unwrap JSON-RPC: get the "result" field
            JsonElement payload;
            if (doc.RootElement.TryGetProperty("result", out var result))
                payload = result;
            else
                payload = doc.RootElement;

            // Extract contextId
            if (payload.TryGetProperty("contextId", out var ctx))
                contextId = ctx.GetString();
            else if (payload.TryGetProperty("status", out var st) &&
                     st.TryGetProperty("message", out var m) &&
                     m.TryGetProperty("contextId", out var sCtx))
                contextId = sCtx.GetString();

            // Extract and print text delta
            var fullText = Helpers.ExtractText(payload);
            if (fullText.StartsWith(previousText, StringComparison.Ordinal))
            {
                Console.Write(fullText[previousText.Length..]);
                previousText = fullText;
            }
            else if (fullText != previousText)
            {
                Console.Write(fullText);
                previousText = fullText;
            }
        }
        catch { /* skip unparseable SSE events */ }
    }

    Console.WriteLine();
}

// ── WAM auth ─────────────────────────────────────────────────────────────

async Task<(string token, IPublicClientApplication app, IAccount? account)> AcquireToken(
    string clientId, string? accountHint)
{
    var builder = PublicClientApplicationBuilder.Create(clientId)
        .WithDefaultRedirectUri()
        .WithAuthority("https://login.microsoftonline.com/common");

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        builder.WithParentActivityOrWindow(ConsoleWindowHandle).WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows));
    }

    var app = builder.Build();
    var scopes = new[] { "https://graph.microsoft.com/.default" };

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
      dotnet run -- --endpoint <url> --token <JWT|WAM> [options]

    Required:
      --endpoint, -e   Agent URL (e.g., https://graph.microsoft.com/rp/workiq/)
      --token, -t      Bearer JWT token, or 'WAM' for Windows broker auth

    Options:
      --appid, -a      App client ID (required with WAM)
      --account        Account hint (e.g., user@contoso.com)
      --stream         Use streaming mode (SSE via message/stream)
      --all-headers    Print all response headers (default: key diagnostics only)

    Examples:
      dotnet run -- -e https://graph.microsoft.com/rp/workiq/ -t WAM -a <appid>
      dotnet run -- -e https://graph.microsoft.com/rp/workiq/ -t eyJ0eXAi... --stream
    """);
}
