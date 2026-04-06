// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json;

namespace WorkIQ.Rest;

public record RestArgs(
    string? Token, string? AppId, string? Account,
    bool Graph, bool Workiq, bool Stream, bool ShowToken,
    int Verbosity, List<string> Headers, string? Error);

public static class Helpers
{
    // ── Arg parsing ─────────────────────────────────────────────────────

    public static RestArgs ParseArgs(string[] args)
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
                case "--show-token": showToken = true; break;
                case "--verbosity" or "-v":
                    if (i + 1 >= args.Length) return Err($"Missing value for {args[i]}");
                    if (!int.TryParse(args[++i], out verbosity))
                        return Err($"--verbosity requires an integer, got: {args[i]}");
                    break;
                case "--header" or "-H":
                    if (i + 1 >= args.Length) return Err($"Missing value for {args[i]}");
                    headers.Add(args[++i]); break;
            }
        }

        return new RestArgs(token, appId, account, graph, workiq, stream, showToken, verbosity, headers, null);

        static RestArgs Err(string msg) => new(null, null, null, false, false, false, false, 1, new List<string>(), msg);
    }

    // ── Chat body builder ───────────────────────────────────────────────

    public static string BuildChatBody(string message)
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

    // ── String truncation ───────────────────────────────────────────────

    public static string Trunc(string s, int max) => s.Length <= max ? s : $"{s[..max]}...";
}
