// WorkIQ A2A Raw Sample — Minimal A2A client using only HttpClient + JSON
// No A2A SDK, no NuGet dependencies beyond MSAL for auth.
// Shows exactly what goes over the wire when talking to a Work IQ agent.
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

// ── Parse args ──────────────────────────────────────────────────────────

string? endpoint = null, token = null, appId = null, account = null;
bool stream = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--endpoint" or "-e": endpoint = args[++i]; break;
        case "--token" or "-t": token = args[++i]; break;
        case "--appid" or "-a": appId = args[++i]; break;
        case "--account": account = args[++i]; break;
        case "--stream": stream = true; break;
        default:
            Console.Error.WriteLine($"Unknown flag: {args[i]}");
            PrintUsage();
            return;
    }
}

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
Console.WriteLine($"Mode: {(stream ? "streaming (message:stream)" : "sync (message:send)")}");
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

    try
    {
        // Build the A2A JSON-RPC request — this is ALL you need
        var request = new
        {
            message = new
            {
                messageId = Guid.NewGuid().ToString(),
                role = "user",
                parts = new object[] { new { kind = "text", text = input } },
                contextId,
                metadata = new Dictionary<string, object>
                {
                    ["Location"] = new { timeZoneOffset = (int)TimeZoneInfo.Local.BaseUtcOffset.TotalMinutes, timeZone = TimeZoneInfo.Local.Id },
                },
            },
            configuration = new
            {
                acceptedOutputModes = new[] { "text/plain", "application/json" },
            },
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        if (stream)
        {
            await StreamResponse(http, endpoint, content);
        }
        else
        {
            await SyncResponse(http, endpoint, content);
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n  ERROR: {ex.Message}");
        Console.ResetColor();
    }

    Console.WriteLine();
}

// ── Sync: POST /message:send ─────────────────────────────────────────────

async Task SyncResponse(HttpClient client, string ep, HttpContent body)
{
    var url = ep.TrimEnd('/') + "/message:send";
    var res = await client.PostAsync(url, body);

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"  [{(int)res.StatusCode} {res.ReasonPhrase}]");
    Console.ResetColor();

    var responseBody = await res.Content.ReadAsStringAsync();

    if (!res.IsSuccessStatusCode)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  {responseBody}");
        Console.ResetColor();
        return;
    }

    using var doc = JsonDocument.Parse(responseBody);

    // Extract contextId for multi-turn
    if (doc.RootElement.TryGetProperty("contextId", out var ctx))
        contextId = ctx.GetString();
    else if (doc.RootElement.TryGetProperty("result", out var result) &&
             result.TryGetProperty("contextId", out var rCtx))
        contextId = rCtx.GetString();

    // Extract text from response
    var text = ExtractText(doc.RootElement);
    Console.WriteLine(text);
}

// ── Streaming: POST /message:stream (SSE) ────────────────────────────────

async Task StreamResponse(HttpClient client, string ep, HttpContent body)
{
    var url = ep.TrimEnd('/') + "/message:stream";
    var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = body };
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

    var res = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"  [{(int)res.StatusCode} {res.ReasonPhrase}]");
    Console.ResetColor();

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

        // SSE format: lines starting with "data:" contain JSON
        if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
        var data = line["data:".Length..].Trim();
        if (string.IsNullOrEmpty(data)) continue;

        try
        {
            using var doc = JsonDocument.Parse(data);

            // Extract contextId
            if (doc.RootElement.TryGetProperty("contextId", out var ctx))
                contextId = ctx.GetString();

            // Extract and print text delta
            var fullText = ExtractText(doc.RootElement);
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

// ── Extract text from A2A response ───────────────────────────────────────

string ExtractText(JsonElement root)
{
    // A2A responses can be wrapped in different shapes depending on the server.
    // Try common paths: root.parts, root.result.status.message.parts, root.status.message.parts

    if (TryGetParts(root, out var text)) return text;

    if (root.TryGetProperty("result", out var result))
    {
        if (TryGetParts(result, out text)) return text;
        if (result.TryGetProperty("status", out var status) &&
            status.TryGetProperty("message", out var msg) &&
            TryGetParts(msg, out text)) return text;
    }

    if (root.TryGetProperty("status", out var s) &&
        s.TryGetProperty("message", out var m) &&
        TryGetParts(m, out text)) return text;

    return root.ToString();
}

bool TryGetParts(JsonElement el, out string text)
{
    text = "";
    if (!el.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
        return false;

    var sb = new StringBuilder();
    foreach (var part in parts.EnumerateArray())
    {
        if (part.TryGetProperty("text", out var t))
            sb.Append(t.GetString());
    }

    text = sb.ToString();
    return text.Length > 0;
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

// ── Helpers ──────────────────────────────────────────────────────────────

[DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
[DllImport("user32.dll", ExactSpelling = true)] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
static IntPtr ConsoleWindowHandle() { var c = GetConsoleWindow(); var r = GetAncestor(c, 3); return r != IntPtr.Zero ? r : c; }

void PrintUsage()
{
    Console.WriteLine("""
    Work IQ A2A Raw Sample — minimal A2A client, no SDK

    Usage:
      dotnet run -- --endpoint <url> --token <JWT|WAM> [options]

    Required:
      --endpoint, -e   Agent URL (e.g., https://graph.microsoft.com/rp/workiq/)
      --token, -t      Bearer JWT token, or 'WAM' for Windows broker auth

    Options:
      --appid, -a      App client ID (required with WAM)
      --account        Account hint (e.g., user@contoso.com)
      --stream         Use streaming mode (SSE via message:stream)

    Examples:
      dotnet run -- -e https://graph.microsoft.com/rp/workiq/ -t WAM -a <appid>
      dotnet run -- -e https://graph.microsoft.com/rp/workiq/ -t eyJ0eXAi... --stream
    """);
}
