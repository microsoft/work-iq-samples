// WorkIQ A2A Sample — Interactive A2A session via the Work IQ Gateway
// Usage: dotnet run -- --token <JWT|WAM> --appid <clientId> [options]

using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using A2A;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using WorkIQ.A2A;

var config = ParseArgs(args);
if (config == null) return;

string token;
IPublicClientApplication? msalApp = null;
IAccount? msalAccount = null;

if (config.Token.Equals("WAM", StringComparison.OrdinalIgnoreCase))
{
    var r = await AcquireWamToken(config.AppId, config.Gateway.Scopes, config.Account, config.Tenant);
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
WireLog.ShowWire = config.ShowWire;
var httpClient = CreateHttpClient(token, config.Gateway);

// --list-agents: GET {endpoint}/.agents and print, then exit. The endpoint
// is a Work IQ / Sydney extension (not part of the A2A spec) returning an
// array of {agentId, name, provider} entries.
if (config.ListAgents)
{
    await ListAgentsAsync(config.Gateway.Endpoint, httpClient);
    return;
}

// Resolve the A2A target URL.
//   --agent-id not set: post directly to the gateway endpoint (default agent for that gateway).
//   --agent-id set:     fetch the agent card from {gateway}/{agentId}/.well-known/agent-card.json
//                       (handled by A2ACardResolver) and use agentCard.Url as the A2A endpoint.
string a2aEndpoint = config.Gateway.Endpoint;
bool effectiveStream = config.Stream;
if (!string.IsNullOrEmpty(config.AgentId))
{
    var gateway = config.Gateway.Endpoint.TrimEnd('/');
    var agentRoot = new Uri($"{gateway}/{config.AgentId}/");
    var resolver = new A2ACardResolver(agentRoot, httpClient);
    AgentCard agentCard;
    try { agentCard = await resolver.GetAgentCardAsync(); }
    catch (Exception ex)
    {
        Ink($"ERROR: failed to fetch agent card for '{config.AgentId}' at {agentRoot}: {ex.Message}\n", ConsoleColor.Red);
        return;
    }

    // v1.0: AgentCard.Url is gone; the URL lives in SupportedInterfaces[].Url.
    var resolvedUrl = agentCard.SupportedInterfaces.FirstOrDefault()?.Url ?? config.Gateway.Endpoint;
    a2aEndpoint = resolvedUrl;
    var streamingSupported = agentCard.Capabilities.Streaming == true;
    if (config.Stream && !streamingSupported)
    {
        if (config.Verbosity >= 1) Ink($"  note: agent '{agentCard.Name}' does not advertise streaming; falling back to sync\n", ConsoleColor.DarkYellow);
        effectiveStream = false;
    }

    if (config.Verbosity >= 1)
    {
        Log("AGENT");
        Console.WriteLine($"  id              {config.AgentId}");
        Console.WriteLine($"  name            {agentCard.Name}");
        Console.WriteLine($"  url             {resolvedUrl}");
        Console.WriteLine($"  streaming       {streamingSupported}");
    }
}

var a2a = new A2AClient(new Uri(a2aEndpoint), httpClient);
string? contextId = null;

if (config.Verbosity >= 1)
{
    Log($"READY — {config.Gateway.Name} — {(effectiveStream ? "Streaming" : "Sync")} — {a2aEndpoint}");
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
            var r = await AcquireWamToken(config.AppId, config.Gateway.Scopes, config.Account, config.Tenant, msalApp, msalAccount);
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
        var msg = new Message
        {
            Role = Role.User,
            MessageId = Guid.NewGuid().ToString(),
            ContextId = contextId,
            Parts = [Part.FromText(input)],
            Metadata = new Dictionary<string, JsonElement>
            {
                ["Location"] = JsonSerializer.SerializeToElement(new { timeZoneOffset = (int)TimeZoneInfo.Local.BaseUtcOffset.TotalMinutes, timeZone = TimeZoneInfo.Local.Id }),
            },
        };

        var sw = Stopwatch.StartNew();
        Dictionary<string, JsonElement>? responseMetadata = null;

        if (effectiveStream)
        {
            // Track candidate cumulative text from each shape — print whichever is
            // currently longer. During Sydney's "answer-as-artifact" rollout, some
            // rings populate both ArtifactUpdate.Artifact.Parts (new shape) AND
            // StatusUpdate.Status.Message.Parts (legacy shape) with one being a
            // fragment. Length-pick keeps output correct on either path without
            // having to detect which shape is "valid".
            // TODO(post-rollout): drop statusMessageText tracking; keep only artifactText.
            var artifactText = string.Empty;
            var statusMessageText = string.Empty;
            var previousPrinted = string.Empty;

            await foreach (var evt in a2a.SendStreamingMessageAsync(new SendMessageRequest { Message = msg }))
            {
                if (config.ShowWire) PrintStreamEvent(evt);

                bool reachedTerminal = false;

                switch (evt.PayloadCase)
                {
                    case StreamResponseCase.ArtifactUpdate:
                    {
                        var artifact = evt.ArtifactUpdate!.Artifact;
                        artifactText = Helpers.JoinText(artifact.Parts);

                        if (config.Verbosity >= 1)
                            Ink($"  [artifact {artifact.ArtifactId[..Math.Min(8, artifact.ArtifactId.Length)]} {artifact.Parts.Count} part(s)]\n", ConsoleColor.DarkGray);
                        break;
                    }
                    case StreamResponseCase.StatusUpdate:
                    {
                        var statusUpdate = evt.StatusUpdate!;
                        if (statusUpdate.Status.Message is { } statusMsg)
                        {
                            contextId = statusMsg.ContextId;
                            responseMetadata = statusMsg.Metadata;
                            statusMessageText = Helpers.JoinText(statusMsg.Parts);
                        }
                        if (config.Verbosity >= 1)
                            Ink($"  [{statusUpdate.Status.State}]\n", ConsoleColor.DarkGray);

                        if (IsTerminal(statusUpdate.Status.State)) reachedTerminal = true;
                        break;
                    }
                    case StreamResponseCase.Task:
                    {
                        contextId = evt.Task!.ContextId;
                        if (config.Verbosity >= 1)
                            Ink($"  [task {evt.Task.Id[..Math.Min(8, evt.Task.Id.Length)]} {evt.Task.Status.State}]\n", ConsoleColor.DarkGray);
                        break;
                    }
                    case StreamResponseCase.Message:
                    {
                        contextId = evt.Message!.ContextId;
                        responseMetadata = evt.Message.Metadata;
                        // Direct Message reply — treat as artifact-equivalent text.
                        artifactText = Helpers.JoinText(evt.Message.Parts);
                        break;
                    }
                }

                // Shape-based pick: if Status.Message has text, this is a
                // pre-fedb1c9 ring still emitting in the legacy location — use
                // it. Otherwise use the artifact path. Same rule as sync mode.
                var combined = !string.IsNullOrEmpty(statusMessageText)
                    ? statusMessageText
                    : artifactText;

                if (combined.Length > previousPrinted.Length)
                {
                    spinner.Stop();
                    Console.Write(combined.StartsWith(previousPrinted, StringComparison.Ordinal)
                        ? combined[previousPrinted.Length..]
                        : combined);
                    previousPrinted = combined;
                }

                if (reachedTerminal)
                {
                    spinner.Stop();
                    sw.Stop();
                    Console.WriteLine();
                    goto streaming_done;
                }
            }

            sw.Stop();
            Console.WriteLine();
            streaming_done:;
        }
        else
        {
            var response = await a2a.SendMessageAsync(new SendMessageRequest { Message = msg });
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

static (string text, string? contextId, Dictionary<string, JsonElement>? metadata) Extract(SendMessageResponse response)
    => WorkIQ.A2A.Helpers.Extract(response);

static bool IsTerminal(TaskState state) =>
    state == TaskState.Completed || state == TaskState.Failed || state == TaskState.Canceled || state == TaskState.Rejected;

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

// Prints each parsed streaming event as JSON. The A2A SDK has already consumed
// the raw `data:` line by the time we get here, so we re-serialize the parsed
// StreamResponse payload back to JSON. The visible JSON shape matches what the
// server sent inside `result` (the SDK strips the outer `{jsonrpc, id, result}`
// envelope during parsing).
static void PrintStreamEvent(StreamResponse evt)
{
    object? data = evt.PayloadCase switch
    {
        StreamResponseCase.Task => evt.Task,
        StreamResponseCase.Message => evt.Message,
        StreamResponseCase.StatusUpdate => evt.StatusUpdate,
        StreamResponseCase.ArtifactUpdate => evt.ArtifactUpdate,
        _ => null,
    };
    if (data is null) return;

    string json;
    try
    {
        json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
    }
    catch (Exception ex) { json = $"(serialize failed: {ex.Message})"; }

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"\n  ← data ({evt.PayloadCase}):");
    foreach (var line in json.Split('\n')) Console.WriteLine($"      {line}");
    Console.ResetColor();
}

static async Task ListAgentsAsync(string endpoint, HttpClient httpClient)
{
    var url = new Uri($"{endpoint.TrimEnd('/')}/.agents");
    HttpResponseMessage response;
    try { response = await httpClient.GetAsync(url); }
    catch (Exception ex)
    {
        Ink($"ERROR: failed to GET {url}: {ex.Message}\n", ConsoleColor.Red);
        return;
    }

    if (!response.IsSuccessStatusCode)
    {
        var body = await response.Content.ReadAsStringAsync();
        Ink($"ERROR: {(int)response.StatusCode} {response.ReasonPhrase} from {url}\n", ConsoleColor.Red);
        if (!string.IsNullOrWhiteSpace(body)) Console.Error.WriteLine($"  {body.Trim()}");
        return;
    }

    var json = await response.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(json);
    if (doc.RootElement.ValueKind != JsonValueKind.Array)
    {
        Ink($"ERROR: expected JSON array from {url}, got {doc.RootElement.ValueKind}\n", ConsoleColor.Red);
        return;
    }

    var agents = doc.RootElement.EnumerateArray()
        .Select(e => (
            Id: e.TryGetProperty("agentId", out var id) ? id.GetString() ?? "" : "",
            Name: e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
            Provider: e.TryGetProperty("provider", out var p) ? p.GetString() ?? "" : ""))
        .ToList();

    Console.WriteLine();
    Console.WriteLine($"Agents at {endpoint}:");
    Console.WriteLine();
    if (agents.Count == 0)
    {
        Console.WriteLine("  (none)");
        return;
    }

    int wId = Math.Max(8, agents.Max(a => a.Id.Length));
    int wName = Math.Max(4, agents.Max(a => a.Name.Length));
    Ink($"  {"AGENT ID".PadRight(wId)}  {"NAME".PadRight(wName)}  PROVIDER\n", ConsoleColor.DarkGray);
    foreach (var a in agents)
        Console.WriteLine($"  {a.Id.PadRight(wId)}  {a.Name.PadRight(wName)}  {a.Provider}");
    Console.WriteLine();
    Console.WriteLine($"{agents.Count} agent{(agents.Count == 1 ? "" : "s")}.");
}

// ── WAM auth ─────────────────────────────────────────────────────────────

static async Task<(string token, IPublicClientApplication app, IAccount? account)> AcquireWamToken(
    string clientId, string[] scopes, string? accountHint, string? tenantId,
    IPublicClientApplication? existingApp = null, IAccount? existingAccount = null)
{
    var app = existingApp;
    if (app == null)
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
    var a = WorkIQ.A2A.Helpers.ParseArgs(args);

    if (a.Error != null)
    {
        Ink($"ERROR: {a.Error}\n", ConsoleColor.Red);
        return null;
    }

    string? token = a.Token, appId = a.AppId, endpoint = a.Endpoint, account = a.Account, tenant = a.Tenant, agentId = a.AgentId;
    bool showToken = a.ShowToken, stream = a.Stream, listAgents = a.ListAgents, showWire = a.ShowWire;
    int verbosity = a.Verbosity;
    var headers = a.Headers;

    if (string.IsNullOrEmpty(token))
    {
        Console.WriteLine("""
            WorkIQ A2A Sample — Interactive A2A agent session against the Work IQ Gateway

            Usage: dotnet run -- --token <JWT|WAM> --appid <clientId> [options]

            Auth:
              --token, -t      Bearer token or 'WAM' for Windows broker auth
              --appid, -a      App client ID (required with WAM)
              --account        Account hint (e.g. user@contoso.com)
              --tenant, -T     Tenant ID or domain (required with WAM for single-tenant apps;
                               defaults to 'common' for multi-tenant apps)

            Options:
              --endpoint, -e   Override the gateway host (scheme + authority only, no path).
                               The /a2a/ path is preserved automatically.
              --agent-id, -A   Invoke a specific agent. The sample fetches the agent card
                               from {gateway}/{agent-id}/.well-known/agent-card.json and
                               uses agentCard.url as the A2A endpoint. Without --agent-id,
                               the sample posts to the gateway endpoint directly (the
                               default BizChat agent).
              --header, -H     Custom HTTP header in 'Key: Value' format (repeatable)
              --show-token     Print the raw JWT token (for reuse with --token)
              --stream         Use streaming mode (SSE via message/stream)
              --list-agents    GET {endpoint}/.agents and print, then exit (no chat loop)
              --show-wire      Pretty-print raw JSON-RPC request/response bodies (and streaming
                               SSE events). Independent of --verbosity.
              -v, --verbosity  0 = response only, 1 = default, 2 = wire diagnostics

            Examples:
              dotnet run -- --token WAM --appid <your-app-id> --tenant <your-tenant>
              dotnet run -- --token WAM --appid <your-app-id> --account user@contoso.com
              dotnet run -- --token WAM --appid <your-app-id> --agent-id <AGENT_ID>
              dotnet run -- --token WAM --appid <your-app-id> --list-agents
              dotnet run -- --token eyJ0eXAi...
            """);
        return null;
    }

    if (token.Equals("WAM", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(appId))
    {
        Ink("ERROR: --appid is required when using --token WAM\n", ConsoleColor.Red);
        return null;
    }

    var gw = Gateways.WorkIQ;
    if (!string.IsNullOrEmpty(endpoint))
    {
        // --endpoint is host-only (scheme + authority). Preserve the /a2a/ path.
        var hostUri = new Uri(endpoint.TrimEnd('/'));
        var presetPath = new Uri(gw.Endpoint).AbsolutePath;
        gw = gw with { Endpoint = $"{hostUri.Scheme}://{hostUri.Authority}{presetPath}" };
    }

    if (headers.Count > 0)
    {
        gw = gw with { ExtraHeaders = [.. gw.ExtraHeaders, .. headers] };
    }

    return new Config(token, appId ?? "", gw, account, tenant, agentId, showToken, verbosity, stream, listAgents, showWire);
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

record Config(string Token, string AppId, GatewayConfig Gateway, string? Account, string? Tenant, string? AgentId, bool ShowToken, int Verbosity, bool Stream, bool ListAgents, bool ShowWire);

// ── Gateway definitions ──────────────────────────────────────────────────

record GatewayConfig(string Name, string Endpoint, string[] Scopes, string Authority, string[] ExtraHeaders);

class Gateways
{
    public static readonly GatewayConfig WorkIQ = new(
        Name: "Work IQ Gateway",
        Endpoint: "https://workiq.svc.cloud.microsoft/a2a/",
        Scopes: ["api://workiq.svc.cloud.microsoft/.default"],
        Authority: "https://login.microsoftonline.com/common",
        ExtraHeaders: []);
}

// ── Wire logger ──────────────────────────────────────────────────────────

class WireLog : DelegatingHandler
{
    public static int Verbosity { get; set; } = 1;
    public static bool ShowWire { get; set; } = false;

    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        if (Verbosity >= 2 || ShowWire)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"\n  ▶ {req.Method} {req.RequestUri}");
            if (Verbosity >= 2)
            {
                foreach (var h in req.Headers)
                    Console.WriteLine(h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                        ? $"    {h.Key}: Bearer ...({h.Value.First().Length}c)"
                        : $"    {h.Key}: {string.Join(", ", h.Value)}");
            }

            if (req.Content is { } body)
            {
                if (Verbosity >= 2)
                    foreach (var h in body.Headers) Console.WriteLine($"    {h.Key}: {string.Join(", ", h.Value)}");
                var text = await body.ReadAsStringAsync(ct);

                if (ShowWire && IsJson(body.Headers.ContentType?.MediaType))
                {
                    Console.WriteLine($"    Body (raw JSON-RPC):");
                    Console.WriteLine(Indent(PrettyJsonOrRaw(text), "      "));
                }
                else if (Verbosity >= 2)
                {
                    Console.WriteLine($"    Body: {Trunc(text, 500)}");
                }

                // Re-create content so the stream can be read again by SendAsync
                req.Content = new StringContent(text, System.Text.Encoding.UTF8, body.Headers.ContentType?.MediaType ?? "application/json");
            }

            Console.ResetColor();
        }

        HttpResponseMessage res;
        try { res = await base.SendAsync(req, ct); }
        catch (Exception ex) { Ink($"  ◀ ERROR: {ex.Message}\n", ConsoleColor.Red); throw; }

        var contentType = res.Content?.Headers.ContentType?.MediaType;
        var isStreaming = string.Equals(contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase);

        if (Verbosity >= 2 || ShowWire)
        {
            Console.ForegroundColor = (int)res.StatusCode >= 400 ? ConsoleColor.Red : ConsoleColor.DarkGray;
            Console.WriteLine($"  ◀ {(int)res.StatusCode} {res.StatusCode}");
            if (Verbosity >= 2)
            {
                foreach (var h in res.Headers) Console.WriteLine($"    {h.Key}: {string.Join(", ", h.Value)}");
                if (res.Content != null)
                    foreach (var h in res.Content.Headers) Console.WriteLine($"    {h.Key}: {string.Join(", ", h.Value)}");
            }

            // Always print error bodies (even in v2-only mode).
            if ((int)res.StatusCode >= 400 && res.Content != null)
            {
                Console.WriteLine($"    Body: {Trunc(await res.Content.ReadAsStringAsync(ct), 1000)}");
            }
            // For streaming responses, do NOT read the body here (would block until stream closes).
            // The streaming consumer in the main loop logs each SSE event when --show-wire is set.
            else if (ShowWire && isStreaming)
            {
                Console.WriteLine($"    (streaming response — see SSE events below)");
            }
            // For non-streaming JSON success responses, pretty-print the full body when --show-wire.
            else if (ShowWire && IsJson(contentType) && res.Content != null)
            {
                var text = await res.Content.ReadAsStringAsync(ct);
                Console.WriteLine($"    Body (raw JSON-RPC):");
                Console.WriteLine(Indent(PrettyJsonOrRaw(text), "      "));
                // Re-wrap so downstream readers (like the A2A SDK) can still parse it.
                res.Content = new StringContent(text, System.Text.Encoding.UTF8, contentType ?? "application/json");
            }

            Console.ResetColor();
        }
        else if (Verbosity >= 1)
        {
            // Always print key diagnostic headers for troubleshooting
            Console.ForegroundColor = ConsoleColor.DarkGray;
            foreach (var name in new[] { "request-id", "x-ms-ags-diagnostic" })
            {
                if (res.Headers.TryGetValues(name, out var values))
                    Console.WriteLine($"  {name}: {string.Join(", ", values)}");
            }
            Console.ResetColor();
        }

        return res;
    }

    static bool IsJson(string? mediaType) =>
        mediaType != null && (mediaType.Contains("json", StringComparison.OrdinalIgnoreCase));

    static string PrettyJsonOrRaw(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(doc.RootElement, PrettyJson);
        }
        catch { return raw; }
    }

    static string Indent(string text, string prefix) =>
        string.Join("\n", text.Split('\n').Select(line => prefix + line));

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
        // Skip when stdout is redirected (e.g., piped to `tee` or a log file):
        // Console.CursorVisible / SetCursorPosition throw IOException on a non-TTY.
        if (Console.IsOutputRedirected) return;
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
