// WorkIQ A2A Sample — Interactive A2A session via Graph RP or WorkIQ Gateway
// Usage: dotnet run -- --graph --token <JWT|WAM> --appid <clientId> [options]
//        dotnet run -- --workiq --token <JWT|WAM> --appid <clientId> [options]

using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using A2A;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;

var config = ParseArgs(args);
if (config == null) return;

string token;
IPublicClientApplication? msalApp = null;
IAccount? msalAccount = null;

if (config.Token.Equals("WAM", StringComparison.OrdinalIgnoreCase))
{
    var r = await AcquireWamToken(config.AppId, config.Gateway.Scopes, config.Account);
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

WireLog.Verbosity = config.Verbosity;
var httpClient = CreateHttpClient(token, config.Gateway);
var a2a = new A2AClient(new Uri(config.Gateway.Endpoint), httpClient);
string? contextId = null;

if (config.Verbosity >= 1)
{
    Log($"READY — {config.Gateway.Name} — {(config.Stream ? "Streaming" : "Sync")} — {config.Gateway.Endpoint}");
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
            var r = await AcquireWamToken(config.AppId, config.Gateway.Scopes, config.Account, msalApp, msalAccount);
            token = r.token; msalAccount = r.account;
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        catch { /* use cached */ }
    }

    if (config.Verbosity >= 1) Ink("Agent > ", ConsoleColor.Green);
    var spinner = new Spinner();
    spinner.Start();
    try
    {
        var msg = new AgentMessage
        {
            Role = MessageRole.User,
            MessageId = Guid.NewGuid().ToString(),
            ContextId = contextId,
            Parts = [new TextPart { Text = input }],
            Metadata = new Dictionary<string, JsonElement>
            {
                ["Location"] = JsonSerializer.SerializeToElement(new { timeZoneOffset = (int)TimeZoneInfo.Local.BaseUtcOffset.TotalMinutes, timeZone = TimeZoneInfo.Local.Id }),
            },
        };

        var sw = Stopwatch.StartNew();
        Dictionary<string, JsonElement>? responseMetadata = null;

        if (config.Stream)
        {
            var previousText = string.Empty;
            await foreach (var sseItem in a2a.SendMessageStreamingAsync(new MessageSendParams { Message = msg }))
            {
                if (sseItem.Data is TaskStatusUpdateEvent statusUpdate)
                {
                    if (statusUpdate.Status.Message is AgentMessage agentMsg)
                    {
                        contextId = agentMsg.ContextId;
                        responseMetadata = agentMsg.Metadata;

                        // -v 1+: show A2A event details (state, part types, sizes)
                        if (config.Verbosity >= 1)
                        {
                            var parts = agentMsg.Parts.Select(p => p switch
                            {
                                TextPart tp => $"TextPart({tp.Text?.Length ?? 0}c)",
                                DataPart dp => $"DataPart",
                                FilePart fp => $"FilePart({fp.File?.Name ?? "?"})",
                                _ => p.GetType().Name
                            });
                            Ink($"  [{statusUpdate.Status.State}] {string.Join(" + ", parts)}\n", ConsoleColor.DarkGray);
                        }

                        var combined = string.Join("", agentMsg.Parts.OfType<TextPart>().Select(p => p.Text));
                        spinner.Stop();

                        // Print text delta
                        if (combined.StartsWith(previousText, StringComparison.Ordinal))
                        {
                            Console.Write(combined[previousText.Length..]);
                            previousText = combined;
                        }
                        else
                        {
                            Console.Write(combined);
                            previousText = combined;
                        }
                    }

                    if (statusUpdate.Final || statusUpdate.Status.State == TaskState.Completed)
                    {
                        break;
                    }
                }
            }

            sw.Stop();
            Console.WriteLine();
        }
        else
        {
            var response = await a2a.SendMessageAsync(new MessageSendParams { Message = msg });
            sw.Stop();
            spinner.Stop();
            var (text, ctx, meta) = Extract(response);
            contextId = ctx;
            responseMetadata = meta;
            Console.WriteLine(text);
        }

        if (config.Verbosity >= 1) Ink($"  ({sw.ElapsedMilliseconds} ms)\n", ConsoleColor.DarkGray);

        // Parse citations from metadata["attributions"]
        if (responseMetadata != null) PrintCitations(responseMetadata, config.Verbosity);
    }
    catch (Exception ex)
    {
        spinner.Stop();
        Ink($"\n  ERROR: {ex.GetType().Name}: {ex.Message}\n", ConsoleColor.Red);
        if (ex.InnerException != null) Ink($"  INNER: {ex.InnerException.Message}\n", ConsoleColor.Red);
    }

    Console.WriteLine();
}

// ── Core helpers ─────────────────────────────────────────────────────────

static (string text, string? contextId, Dictionary<string, JsonElement>? metadata) Extract(object response) => response switch
{
    AgentMessage am => (Join(am), am.ContextId, am.Metadata),
    AgentTask { Status: { State: TaskState.Completed, Message: AgentMessage cm } } t => (Join(cm), t.ContextId, cm.Metadata),
    AgentTask t => ($"[Task {t.Id} — {t.Status.State}]", t.ContextId, null),
    _ => ("(no response)", null, null),
};

static string Join(AgentMessage m) => string.Join("\n", m.Parts.OfType<TextPart>().Select(p => p.Text));

static HttpClient CreateHttpClient(string bearerToken, GatewayConfig gw)
{
    var client = new HttpClient(new WireLog { InnerHandler = new HttpClientHandler() }) { Timeout = TimeSpan.FromMinutes(5) };
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
    foreach (var h in gw.ExtraHeaders)
    {
        var parts = h.Split(':', 2);
        client.DefaultRequestHeaders.Add(parts[0].Trim(), parts[1].Trim());
    }

    return client;
}

// ── WAM auth ─────────────────────────────────────────────────────────────

static async Task<(string token, IPublicClientApplication app, IAccount? account)> AcquireWamToken(
    string clientId, string[] scopes, string? accountHint,
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
    string? token = null, appId = null, endpoint = null, account = null;
    bool graph = false, workiq = false, showToken = false, stream = false;
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
            case "--endpoint" or "-e": endpoint = args[++i]; break;
            case "--account": account = args[++i]; break;
            case "--show-token": showToken = true; break;
            case "--stream": stream = true; break;
            case "--verbosity" or "-v": verbosity = int.Parse(args[++i]); break;
            case "--header" or "-H": headers.Add(args[++i]); break;
        }
    }

    if (string.IsNullOrEmpty(token) || (!graph && !workiq))
    {
        Console.WriteLine("""
            WorkIQ A2A Sample — Interactive A2A agent session

            Usage: dotnet run -- <gateway> --token <JWT|WAM> --appid <clientId> [options]

            Gateway (exactly one required):
              --graph          Use Microsoft Graph RP gateway
              --workiq         Use WorkIQ Gateway (coming soon)

            Auth:
              --token, -t      Bearer token or 'WAM' for Windows broker auth
              --appid, -a      App client ID (required with WAM)
              --account        Account hint (e.g. user@contoso.com)

            Options:
              --endpoint, -e   Override default gateway endpoint
              --header, -H     Custom HTTP header in 'Key: Value' format (repeatable)
              --show-token     Print the raw JWT token (for reuse with --token)
              --stream         Use streaming mode (SSE via message/stream)
              -v, --verbosity  0 = response only, 1 = default, 2 = full wire

            Examples:
              dotnet run -- --graph --token WAM --appid <your-app-id>
              dotnet run -- --graph --token WAM --appid <your-app-id> --account user@contoso.com
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

    var gw = graph ? Gateways.Graph : Gateways.WorkIQ;
    if (!string.IsNullOrEmpty(endpoint))
    {
        gw = gw with { Endpoint = endpoint };
    }

    if (headers.Count > 0)
    {
        gw = gw with { ExtraHeaders = [.. gw.ExtraHeaders, .. headers] };
    }

    return new Config(token, appId ?? "", gw, account, showToken, verbosity, stream);
}

// ── Citations ─────────────────────────────────────────────────────────────

static void PrintCitations(Dictionary<string, JsonElement>? metadata, int verbosity)
{
    if (metadata == null || !metadata.TryGetValue("attributions", out var attrs) || attrs.ValueKind != JsonValueKind.Array)
        return;

    var citations = new List<(string type, string source, string provider, string url)>();
    foreach (var attr in attrs.EnumerateArray())
    {
        var type = attr.TryGetProperty("attributionType", out var t) ? t.ToString() : "";
        var source = attr.TryGetProperty("attributionSource", out var s) ? s.ToString() : "";
        var provider = attr.TryGetProperty("providerDisplayName", out var p) ? p.GetString() ?? "" : "";
        var url = attr.TryGetProperty("seeMoreWebUrl", out var u) ? u.GetString() ?? "" : "";
        citations.Add((type, source, provider, url));
    }

    if (citations.Count == 0) return;

    var citationCount = citations.Count(c => c.type.Contains("citation", StringComparison.OrdinalIgnoreCase));
    var annotationCount = citations.Count(c => c.type.Contains("annotation", StringComparison.OrdinalIgnoreCase));

    if (verbosity >= 1)
    {
        Ink($"  Citations: {citationCount}  Annotations: {annotationCount}\n", ConsoleColor.DarkYellow);
    }

    if (verbosity >= 2)
    {
        foreach (var (type, source, provider, url) in citations)
        {
            var label = type.Contains("citation", StringComparison.OrdinalIgnoreCase) ? "📄" : "🔗";
            var name = !string.IsNullOrEmpty(provider) ? provider : "(unnamed)";
            Console.ForegroundColor = type.Contains("citation", StringComparison.OrdinalIgnoreCase) ? ConsoleColor.Yellow : ConsoleColor.DarkGray;
            Console.WriteLine($"    {label} [{type}/{source}] {name}");
            if (!string.IsNullOrEmpty(url))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                var truncUrl = url.Length <= 120 ? url : $"{url[..120]}...";
                Console.WriteLine($"       {truncUrl}");
            }
        }

        Console.ResetColor();
    }
}

// ── Utilities ────────────────────────────────────────────────────────────

static void Log(string s) { Ink($"\n── {s} ──\n", ConsoleColor.DarkGray); }
static void Ink(string s, ConsoleColor c) { Console.ForegroundColor = c; Console.Write(s); Console.ResetColor(); }

[DllImport("kernel32.dll", EntryPoint = "GetConsoleWindow")] static extern IntPtr Win32GetConsoleWindow();
[DllImport("user32.dll", ExactSpelling = true)] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
static IntPtr ConsoleWindowHandle() { var c = Win32GetConsoleWindow(); var r = GetAncestor(c, 3); return r != IntPtr.Zero ? r : c; }

record Config(string Token, string AppId, GatewayConfig Gateway, string? Account, bool ShowToken, int Verbosity, bool Stream);

// ── Gateway definitions ──────────────────────────────────────────────────

record GatewayConfig(string Name, string Endpoint, string[] Scopes, string Authority, string[] ExtraHeaders);

class Gateways
{
    public static readonly GatewayConfig Graph = new(
        Name: "Graph RP",
        Endpoint: "https://graph.microsoft.com/rp/workiq/",
        Scopes: ["https://graph.microsoft.com/.default"],
        Authority: "https://login.microsoftonline.com/common",
        ExtraHeaders: []);

    public static readonly GatewayConfig WorkIQ = new(
        Name: "WorkIQ Gateway",
        Endpoint: "", // TODO: set when available
        Scopes: [], // TODO: set when available
        Authority: "https://login.microsoftonline.com/common",
        ExtraHeaders: []);
}

// ── Wire logger ──────────────────────────────────────────────────────────

class WireLog : DelegatingHandler
{
    public static int Verbosity { get; set; } = 1;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        if (Verbosity >= 2)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"\n  ▶ {req.Method} {req.RequestUri}");
            foreach (var h in req.Headers)
                Console.WriteLine(h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                    ? $"    {h.Key}: Bearer ...({h.Value.First().Length}c)"
                    : $"    {h.Key}: {string.Join(", ", h.Value)}");

            if (req.Content is { } body)
            {
                foreach (var h in body.Headers) Console.WriteLine($"    {h.Key}: {string.Join(", ", h.Value)}");
                var text = await body.ReadAsStringAsync(ct);
                Console.WriteLine($"    Body: {Trunc(text, 500)}");
            }

            Console.ResetColor();
        }

        HttpResponseMessage res;
        try { res = await base.SendAsync(req, ct); }
        catch (Exception ex) { Ink($"  ◀ ERROR: {ex.Message}\n", ConsoleColor.Red); throw; }

        if (Verbosity >= 2)
        {
            Console.ForegroundColor = (int)res.StatusCode >= 400 ? ConsoleColor.Red : ConsoleColor.DarkGray;
            Console.WriteLine($"  ◀ {(int)res.StatusCode} {res.StatusCode}");
            foreach (var h in res.Headers) Console.WriteLine($"    {h.Key}: {string.Join(", ", h.Value)}");
            if (res.Content != null)
                foreach (var h in res.Content.Headers) Console.WriteLine($"    {h.Key}: {string.Join(", ", h.Value)}");

            if ((int)res.StatusCode >= 400 && res.Content != null)
                Console.WriteLine($"    Body: {Trunc(await res.Content.ReadAsStringAsync(ct), 1000)}");

            Console.ResetColor();
        }

        return res;
    }

    static string Trunc(string s, int max) => s.Length <= max ? s : $"{s[..max]}...";
    static void Ink(string s, ConsoleColor c) { Console.ForegroundColor = c; Console.Write(s); Console.ResetColor(); }
}

// ── Spinner ─────────────────────────────────────────────────────────────

class Spinner
{
    private static readonly string[] Frames = ["·  ", "·· ", "···", " ··", "  ·", "   "];
    private CancellationTokenSource? cts;
    private Task? task;

    public void Start()
    {
        cts = new CancellationTokenSource();
        var token = cts.Token;
        task = Task.Run(async () =>
        {
            var i = 0;
            Console.CursorVisible = false;
            while (!token.IsCancellationRequested)
            {
                var frame = Frames[i % Frames.Length];
                Console.Write(frame);
                Console.SetCursorPosition(Console.CursorLeft - frame.Length, Console.CursorTop);
                try { await Task.Delay(150, token); } catch { break; }
                i++;
            }
            Console.Write("   ");
            Console.SetCursorPosition(Console.CursorLeft - 3, Console.CursorTop);
            Console.CursorVisible = true;
        });
    }

    public void Stop()
    {
        if (cts == null) return;
        cts.Cancel();
        try { task?.Wait(); } catch { }
        cts = null;
    }
}
