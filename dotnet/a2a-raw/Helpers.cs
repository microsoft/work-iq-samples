// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using System.Text.Json;

namespace WorkIQ.A2ARaw;

public record RawArgs(
    string? Endpoint, string? Token, string? AppId, string? Account,
    string? Scope, string? Tenant, string? AgentId,
    bool AllHeaders, bool ShowWire, string? Error);

public static class Helpers
{
    // ── Arg parsing ─────────────────────────────────────────────────────

    public static RawArgs ParseArgs(string[] args)
    {
        string? endpoint = null, token = null, appId = null, account = null, scope = null, tenant = null, agentId = null;
        bool allHeaders = false, showWire = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--endpoint" or "-e":
                    if (i + 1 >= args.Length) return Err($"Missing value for {args[i]}");
                    endpoint = args[++i]; break;
                case "--token" or "-t":
                    if (i + 1 >= args.Length) return Err($"Missing value for {args[i]}");
                    token = args[++i]; break;
                case "--appid" or "-a":
                    if (i + 1 >= args.Length) return Err($"Missing value for {args[i]}");
                    appId = args[++i]; break;
                case "--account":
                    if (i + 1 >= args.Length) return Err($"Missing value for {args[i]}");
                    account = args[++i]; break;
                case "--scope" or "-s":
                    if (i + 1 >= args.Length) return Err($"Missing value for {args[i]}");
                    scope = args[++i]; break;
                case "--tenant" or "-T":
                    if (i + 1 >= args.Length) return Err($"Missing value for {args[i]}");
                    tenant = args[++i]; break;
                case "--agent-id" or "-A":
                    if (i + 1 >= args.Length) return Err($"Missing value for {args[i]}");
                    agentId = args[++i]; break;
                case "--all-headers": allHeaders = true; break;
                case "--show-wire": showWire = true; break;
                default:
                    return Err($"Unknown flag: {args[i]}");
            }
        }

        return new RawArgs(endpoint, token, appId, account, scope, tenant, agentId, allHeaders, showWire, null);

        static RawArgs Err(string msg) => new(null, null, null, null, null, null, null, false, false, msg);
    }

    // ── A2A v1.0 response text extraction ──────────────────────────────────
    //
    // Walks an unwrapped JSON-RPC `result` object and returns the answer text.
    // The agent's answer text lives in artifacts. Status carries metadata
    // (citations) but not the final answer text.
    //
    // Shapes handled:
    //   - result.task.artifacts[].parts[text]            ← sync (typical)
    //   - result.message.parts[text]                     ← sync, direct Message payload

    public static string ExtractText(JsonElement el)
    {
        // Sync `task` payload — answer text lives in artifacts.
        if (el.TryGetProperty("task", out var task) &&
            TryGetArtifactsText(task, out var fromArtifacts))
            return fromArtifacts;

        // Sync `message` payload (direct reply, no task).
        if (el.TryGetProperty("message", out var m) && TryGetParts(m, out var msgText))
            return msgText;

        return "";
    }

    public static bool TryGetParts(JsonElement el, out string text)
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

    // Walks `parent.artifacts[].parts[].text` and concatenates all text content.
    public static bool TryGetArtifactsText(JsonElement parent, out string text)
    {
        text = "";
        if (!parent.TryGetProperty("artifacts", out var arts) || arts.ValueKind != JsonValueKind.Array)
            return false;

        var sb = new StringBuilder();
        foreach (var artifact in arts.EnumerateArray())
        {
            if (TryGetParts(artifact, out var t)) sb.Append(t);
        }

        text = sb.ToString();
        return text.Length > 0;
    }
}
