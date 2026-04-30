// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
//
// WorkIQ A2A Raw Sample — Minimal A2A client using only HttpClient + JSON
// No A2A SDK. Shows exactly what goes over the wire (JSON-RPC v0.3 format).
//
// Targets the Work IQ Gateway (`https://workiq.svc.cloud.microsoft/a2a/`).
//
// Usage:
//   dotnet run -- --token <JWT|WAM> --appid <client-id> [--account user@tenant.com]

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
bool allHeaders = parsed.AllHeaders, showWire = parsed.ShowWire;

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
// to the v0.3 dispatcher and the v1.0 method name "SendMessage" returns
// JSON-RPC -32601 "Method not found".
http.DefaultRequestHeaders.TryAddWithoutValidation("A2A-Version", "1.0");

// ── Agent card resolution (when --agent-id is set) ───────────────────────
//
// This sample deliberately does the agent-card fetch in raw HTTP + JSON to
// show what the wire interaction looks like. With --agent-id the sample:
//   1. GET {endpoint}/{agentId}/.well-known/agent-card.json
//   2. Parse the JSON to find:   url, name
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

        Console.WriteLine($"Agent card:");
        Console.WriteLine($"  id              {agentId}");
        Console.WriteLine($"  name            {resolvedName}");
        Console.WriteLine($"  url             {resolvedUrl}");

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

        // Wrap in JSON-RPC envelope — v1.0 method name: SendMessage.
        var jsonRpcRequest = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString(),
            method = "SendMessage",
            @params = new { message },
        };

        var json = JsonSerializer.Serialize(jsonRpcRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        if (showWire) PrintWireBody("▶ POST request", json);

        await SyncResponse(http, endpoint, content, spinnerCts, spinnerTask, showWire);
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

// Walks a v1.0 sync `result` envelope for the contextId. v1.0 places contextId
// on result.task or result.message.
static string? ExtractContextId(JsonElement el)
{
    static string? Get(JsonElement e) => e.TryGetProperty("contextId", out var c) ? c.GetString() : null;

    foreach (var key in new[] { "task", "message" })
    {
        if (el.TryGetProperty(key, out var inner) && Get(inner) is { } id)
            return id;
    }
    return Get(el);
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
      --agent-id, -A   Invoke a specific agent. The sample fetches
                       <gateway>/<agent-id>/.well-known/agent-card.json,
                       reads agentCard.url, and POSTs JSON-RPC there.
                       Without --agent-id, the sample posts to the
                       Work IQ Gateway A2A endpoint (default agent).
      --scope, -s      Token scope for WAM. Defaults to the Work IQ audience:
                       api://workiq.svc.cloud.microsoft/.default
      --appid, -a      App client ID (required with WAM)
      --account        Account hint (e.g., user@contoso.com)
      --tenant, -T     Tenant ID or domain (required with WAM for single-tenant apps;
                       defaults to 'common' for multi-tenant apps)
      --all-headers    Print all response headers (default: key diagnostics only)
      --show-wire      Pretty-print raw JSON-RPC request/response bodies.

    Examples:
      # Work IQ Gateway (uses defaults)
      dotnet run -- -t WAM -a <appid>

      # Invoke a specific agent
      dotnet run -- -t WAM -a <appid> --agent-id <AGENT_ID>

      # With a pre-obtained JWT
      dotnet run -- -t eyJ0eXAi...

    Note: streaming responses are coming soon and not yet supported by this sample.
    """);
}
