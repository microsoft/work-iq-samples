// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using System.Text.Json;

namespace WorkIQ.A2ARaw;

public record RawArgs(
    string? Endpoint, string? Token, string? AppId, string? Account,
    bool Stream, bool AllHeaders, string? Error);

public static class Helpers
{
    // ── Arg parsing ─────────────────────────────────────────────────────

    public static RawArgs ParseArgs(string[] args)
    {
        string? endpoint = null, token = null, appId = null, account = null;
        bool stream = false, allHeaders = false;

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
                case "--stream": stream = true; break;
                case "--all-headers": allHeaders = true; break;
                default:
                    return Err($"Unknown flag: {args[i]}");
            }
        }

        return new RawArgs(endpoint, token, appId, account, stream, allHeaders, null);

        static RawArgs Err(string msg) => new(null, null, null, null, false, false, msg);
    }

    // ── A2A response text extraction ────────────────────────────────────

    public static string ExtractText(JsonElement el)
    {
        // Try direct parts
        if (TryGetParts(el, out var text)) return text;

        // Try result.status.message.parts (task response)
        if (el.TryGetProperty("status", out var status) &&
            status.TryGetProperty("message", out var msg) &&
            TryGetParts(msg, out text)) return text;

        // Try result.message.parts
        if (el.TryGetProperty("message", out var m) &&
            TryGetParts(m, out text)) return text;

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
}
