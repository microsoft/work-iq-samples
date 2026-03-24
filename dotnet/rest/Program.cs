// WorkIQ REST Sample — Interactive Copilot Chat via Microsoft Graph REST API
// Supports both synchronous (/chat) and streaming (/chatOverStream) modes.
// Usage: dotnet run -- --token <JWT|WAM> --appid <clientId> [--stream] [--account <email>]
// Docs:  https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/chat/overview

using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;

const string GraphBase = "https://graph.microsoft.com/beta/copilot";

var config = ParseArgs(args);
if (config == null) return;

string token;
IPublicClientApplication? msalApp = null;
IAccount? msalAccount = null;

if (config.Token.Equals("WAM", StringComparison.OrdinalIgnoreCase))
{
    var r = await AcquireWamToken(config.AppId, config.Account);
    token = r.token; msalApp = r.app; msalAccount = r.account;
}
else
{
    token = config.Token;
}

if (config.Verbosity >= 1)
{
    Log("TOKEN");
    DecodeToken(token);
    if (config.ShowToken) Console.WriteLine($"\n  {token}\n");
}

var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
foreach (var h in config.Headers)
{
    var parts = h.Split(':', 2);
    if (parts.Length == 2) httpClient.DefaultRequestHeaders.TryAddWithoutValidation(parts[0].Trim(), parts[1].Trim());
}

string? conversationId = null;

if (config.Verbosity >= 1)
{
    Log($"READY — {(config.Stream ? "Streaming" : "Synchronous")} mode");
    Console.WriteLine("Type a message. 'quit' to exit.\n");
}

while (true)
{
    if (config.Verbosity >= 1) Ink("You > ", ConsoleColor.Cyan);
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input) || input.Equals("quit", StringComparison.OrdinalIgnoreCase)) break;

    // Silent token refresh
    if (msalApp != null)
    {
        try
        {
            var r = await AcquireWamToken(config.AppId, config.Account, msalApp, msalAccount);
            token = r.token; msalAccount = r.account;
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        catch { /* use cached */ }
    }

    try
    {
        // Step 1: Create conversation (first turn only)
        if (conversationId == null)
        {
            conversationId = await CreateConversation(httpClient);
            if (config.Verbosity >= 1) Ink($"  Conversation: {conversationId}\n", ConsoleColor.DarkGray);
        }

        // Step 2: Send message
        var sw = Stopwatch.StartNew();
        if (config.Stream)
        {
            await ChatStream(httpClient, conversationId, input);
        }
        else
        {
            await ChatSync(httpClient, conversationId, input);
        }

        sw.Stop();
        if (config.Verbosity >= 1) Ink($"  ({sw.ElapsedMilliseconds} ms)\n", ConsoleColor.DarkGray);
    }
    catch (Exception ex)
    {
        Ink($"\n  ERROR: {ex.GetType().Name}: {ex.Message}\n", ConsoleColor.Red);
        if (ex.InnerException != null) Ink($"  INNER: {ex.InnerException.Message}\n", ConsoleColor.Red);
        conversationId = null; // Reset conversation on error
    }

    Console.WriteLine();
}

// ── API calls ────────────────────────────────────────────────────────────

async Task<string> CreateConversation(HttpClient client)
{
    if (config.Verbosity >= 1) Ink("  Creating conversation...\n", ConsoleColor.DarkGray);
    var response = await client.PostAsync($"{GraphBase}/conversations",
        new StringContent("{}", Encoding.UTF8, "application/json"));

    var json = await response.Content.ReadAsStringAsync();
    if (config.Verbosity >= 2) LogWire("POST", $"{GraphBase}/conversations", "{}", response, json);

    response.EnsureSuccessStatusCode();

    using var doc = JsonDocument.Parse(json);
    return doc.RootElement.GetProperty("id").GetString()
        ?? throw new InvalidOperationException("No conversation ID in response");
}

async Task ChatSync(HttpClient client, string convId, string message)
{
    var url = $"{GraphBase}/conversations/{convId}/chat";
    var body = BuildChatBody(message);

    var response = await client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
    var json = await response.Content.ReadAsStringAsync();
    if (config.Verbosity >= 2) LogWire("POST", url, body, response, json);

    response.EnsureSuccessStatusCode();

    using var doc = JsonDocument.Parse(json);
    var messages = doc.RootElement.GetProperty("messages");

    // Last message is the assistant response
    JsonElement lastMsg = default;
    foreach (var msg in messages.EnumerateArray()) lastMsg = msg;

    var text = lastMsg.GetProperty("text").GetString() ?? "";
    if (config.Verbosity >= 1) Ink("Agent > ", ConsoleColor.Green);
    Console.WriteLine(text);

    // Parse and display citations
    if (lastMsg.TryGetProperty("attributions", out var attrs) && attrs.GetArrayLength() > 0)
    {
        PrintCitations(attrs);
    }
}

void PrintCitations(JsonElement attrs)
{
    var citations = new List<(string type, string source, string provider, string url)>();
    foreach (var attr in attrs.EnumerateArray())
    {
        var type = attr.TryGetProperty("attributionType", out var t) ? t.GetString() ?? "" : "";
        var source = attr.TryGetProperty("attributionSource", out var s) ? s.GetString() ?? "" : "";
        var provider = attr.TryGetProperty("providerDisplayName", out var p) ? p.GetString() ?? "" : "";
        var url = attr.TryGetProperty("seeMoreWebUrl", out var u) ? u.GetString() ?? "" : "";
        citations.Add((type, source, provider, url));
    }

    var citationCount = citations.Count(c => c.type == "citation");
    var annotationCount = citations.Count(c => c.type == "annotation");

    if (config.Verbosity >= 1)
    {
        Ink($"\n  Citations: {citationCount}  Annotations: {annotationCount}\n", ConsoleColor.DarkYellow);
    }

    if (config.Verbosity >= 2)
    {
        foreach (var (type, source, provider, url) in citations)
        {
            var label = type == "citation" ? "📄" : "🔗";
            var name = !string.IsNullOrEmpty(provider) ? provider : "(unnamed)";
            Console.ForegroundColor = type == "citation" ? ConsoleColor.Yellow : ConsoleColor.DarkGray;
            Console.WriteLine($"    {label} [{type}/{source}] {name}");
            if (!string.IsNullOrEmpty(url))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"       {Trunc(url, 120)}");
            }
        }

        Console.ResetColor();
    }
}

async Task ChatStream(HttpClient client, string convId, string message)
{
    var url = $"{GraphBase}/conversations/{convId}/chatOverStream";
    var body = BuildChatBody(message);

    var request = new HttpRequestMessage(HttpMethod.Post, url)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    if (config.Verbosity >= 2) LogWire("POST", url, body, response);

    response.EnsureSuccessStatusCode();

    using var stream = await response.Content.ReadAsStreamAsync();
    using var reader = new StreamReader(stream);

    string? previousText = null;
    bool headerPrinted = false;
    JsonElement lastAttrs = default;
    bool hasAttrs = false;

    while (!reader.EndOfStream)
    {
        var line = await reader.ReadLineAsync();
        if (line == null) break;

        // SSE format: "data: {...}"
        if (!line.StartsWith("data: ")) continue;
        var eventJson = line["data: ".Length..];
        if (string.IsNullOrWhiteSpace(eventJson)) continue;

        try
        {
            using var doc = JsonDocument.Parse(eventJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("messages", out var msgs)) continue;

            // Find the latest assistant message
            JsonElement lastMsg = default;
            bool found = false;
            foreach (var msg in msgs.EnumerateArray())
            {
                if (msg.TryGetProperty("text", out _))
                {
                    lastMsg = msg;
                    found = true;
                }
            }

            if (!found) continue;

            var text = lastMsg.GetProperty("text").GetString() ?? "";

            // Track citations from latest event (final event has complete attributions)
            if (lastMsg.TryGetProperty("attributions", out var a) && a.GetArrayLength() > 0)
            {
                lastAttrs = a.Clone();
                hasAttrs = true;
            }

            // Print only the delta (new text since last event)
            if (!headerPrinted) { if (config.Verbosity >= 1) Ink("Agent > ", ConsoleColor.Green); headerPrinted = true; }

            if (previousText != null && text.StartsWith(previousText))
            {
                Console.Write(text[previousText.Length..]);
            }
            else if (previousText == null || text != previousText)
            {
                Console.Write(text);
            }

            previousText = text;
        }
        catch (JsonException)
        {
            // Skip malformed events
        }
    }

    Console.WriteLine();

    // Print citations from the final SSE event
    if (hasAttrs)
    {
        PrintCitations(lastAttrs);
    }
}

static string BuildChatBody(string message)
{
    // Graph API requires IANA timezone (e.g. "America/Los_Angeles"), not Windows (e.g. "Pacific Standard Time")
    string tz;
    try { tz = TimeZoneInfo.Local.HasIanaId ? TimeZoneInfo.Local.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? iana : "UTC"; }
    catch { tz = "UTC"; }

    return JsonSerializer.Serialize(new
    {
        message = new { text = message },
        locationHint = new { timeZone = tz },
    });
}

// ── WAM auth ─────────────────────────────────────────────────────────────

static async Task<(string token, IPublicClientApplication app, IAccount? account)> AcquireWamToken(
    string clientId, string? accountHint,
    IPublicClientApplication? existingApp = null, IAccount? existingAccount = null)
{
    var app = existingApp;
    if (app == null)
    {
        var builder = PublicClientApplicationBuilder.Create(clientId)
            .WithDefaultRedirectUri()
            .WithAuthority("https://login.microsoftonline.com/common");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            builder.WithParentActivityOrWindow(ConsoleWindowHandle).WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows));
        }

        app = builder.Build();
    }

    string[] scopes = ["https://graph.microsoft.com/.default"];
    AuthenticationResult result;

    try
    {
        if (existingAccount != null)
        {
            result = await app.AcquireTokenSilent(scopes, existingAccount).ExecuteAsync();
        }
        else if (!string.IsNullOrEmpty(accountHint))
        {
            var cached = (await app.GetAccountsAsync()).FirstOrDefault(a => a.Username?.Contains(accountHint, StringComparison.OrdinalIgnoreCase) == true);
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

// ── Token decode ─────────────────────────────────────────────────────────

static void DecodeToken(string token)
{
    try
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        foreach (var c in new[] { "aud", "appid", "app_displayname", "tid", "upn", "name", "scp" })
        {
            var v = jwt.Claims.FirstOrDefault(x => x.Type == c)?.Value;
            if (!string.IsNullOrEmpty(v)) Console.WriteLine($"  {c,-16} {v}");
        }

        var left = jwt.ValidTo - DateTime.UtcNow;
        Ink($"  {"expires",-16} {jwt.ValidTo:HH:mm:ss} UTC ({(left.TotalMinutes > 0 ? $"{left.TotalMinutes:F0}m" : "EXPIRED")})\n",
            left.TotalMinutes < 5 ? ConsoleColor.Red : ConsoleColor.Gray);
    }
    catch (Exception ex) { Ink($"  decode failed: {ex.Message}\n", ConsoleColor.Red); }
}

// ── Args ─────────────────────────────────────────────────────────────────

static Config? ParseArgs(string[] args)
{
    string? token = null, appId = null, account = null;
    bool graph = false, workiq = false, stream = false, showToken = false;
    int verbosity = 1;
    var headers = new List<string>();

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--graph": graph = true; break;
            case "--workiq": workiq = true; break;
            case "--token" or "-t": token = args[++i]; break;
            case "--appid" or "-a": appId = args[++i]; break;
            case "--account": account = args[++i]; break;
            case "--stream": stream = true; break;
            case "--show-token": showToken = true; break;
            case "--verbosity" or "-v": verbosity = int.Parse(args[++i]); break;
            case "--header" or "-H": headers.Add(args[++i]); break;
        }
    }

    if (string.IsNullOrEmpty(token) || (!graph && !workiq))
    {
        Console.WriteLine("""
            WorkIQ REST Sample — Interactive Copilot Chat via Microsoft Graph

            Usage: dotnet run -- <gateway> --token <JWT|WAM> --appid <clientId> [options]

            Gateway (exactly one required):
              --graph          Use Microsoft Graph API
              --workiq         Use WorkIQ Gateway (coming soon)

            Auth:
              --token, -t      Bearer token or 'WAM' for Windows broker auth
              --appid, -a      App client ID (required with WAM)
              --account        Account hint (e.g. user@contoso.com)

            Options:
              --header, -H     Custom HTTP header in 'Key: Value' format (repeatable)
              --stream         Use streaming mode (SSE via /chatOverStream)
              --show-token     Print the raw JWT token (for reuse)
              -v, --verbosity  0 = response only, 1 = default, 2 = full wire

            Examples:
              dotnet run -- --graph --token WAM --appid <your-app-id>
              dotnet run -- --graph --token WAM --appid <your-app-id> --stream
              dotnet run -- --graph --token eyJ0eXAi...
            """);
        return null;
    }

    if (graph && workiq)
    {
        Ink("ERROR: specify --graph or --workiq, not both\n", ConsoleColor.Red);
        return null;
    }

    if (workiq)
    {
        Ink("ERROR: --workiq gateway is not yet implemented\n", ConsoleColor.Yellow);
        return null;
    }

    if (token.Equals("WAM", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(appId))
    {
        Ink("ERROR: --appid is required when using --token WAM\n", ConsoleColor.Red);
        return null;
    }

    return new Config(token, appId ?? "", account, stream, showToken, verbosity, headers);
}

// ── Utilities ────────────────────────────────────────────────────────────

static void Log(string s) { Ink($"\n── {s} ──\n", ConsoleColor.DarkGray); }
static void Ink(string s, ConsoleColor c) { Console.ForegroundColor = c; Console.Write(s); Console.ResetColor(); }

static void LogWire(string method, string url, string? body, HttpResponseMessage res, string? responseBody = null)
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"  ▶ {method} {url}");
    foreach (var h in res.RequestMessage?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
        Console.WriteLine($"    {h.Key}: {string.Join(", ", h.Value)}");
    if (body != null && body.Length > 2) Console.WriteLine($"    Body: {Trunc(body, 300)}");

    Console.ForegroundColor = (int)res.StatusCode >= 400 ? ConsoleColor.Red : ConsoleColor.DarkGray;
    Console.WriteLine($"  ◀ {(int)res.StatusCode} {res.StatusCode}");
    foreach (var h in res.Headers) Console.WriteLine($"    {h.Key}: {string.Join(", ", h.Value)}");
    if (res.Content != null)
        foreach (var h in res.Content.Headers) Console.WriteLine($"    {h.Key}: {string.Join(", ", h.Value)}");

    if ((int)res.StatusCode >= 400 && responseBody != null)
        Console.WriteLine($"    {Trunc(responseBody, 500)}");

    Console.ResetColor();
}

static string Trunc(string s, int max) => s.Length <= max ? s : $"{s[..max]}...";

[DllImport("kernel32.dll", EntryPoint = "GetConsoleWindow")] static extern IntPtr Win32GetConsoleWindow();
[DllImport("user32.dll", ExactSpelling = true)] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
static IntPtr ConsoleWindowHandle() { var c = Win32GetConsoleWindow(); var r = GetAncestor(c, 3); return r != IntPtr.Zero ? r : c; }

record Config(string Token, string AppId, string? Account, bool Stream, bool ShowToken, int Verbosity, List<string> Headers);
