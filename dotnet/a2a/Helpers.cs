// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json;
using A2A;

namespace WorkIQ.A2A;

public record A2AArgs(
    string? Token, string? AppId, string? Endpoint, string? Account,
    bool Graph, bool Workiq, bool ShowToken, bool Stream,
    int Verbosity, List<string> Headers, string? Error);

public static class Helpers
{
    // ── Arg parsing ─────────────────────────────────────────────────────

    public static A2AArgs ParseArgs(string[] args)
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
                case "--token" or "-t":
                    if (i + 1 >= args.Length) return Err($"Missing value for {args[i]}");
                    token = args[++i]; break;
                case "--appid" or "-a":
                    if (i + 1 >= args.Length) return Err($"Missing value for {args[i]}");
                    appId = args[++i]; break;
                case "--endpoint" or "-e":
                    if (i + 1 >= args.Length) return Err($"Missing value for {args[i]}");
                    endpoint = args[++i]; break;
                case "--account":
                    if (i + 1 >= args.Length) return Err($"Missing value for {args[i]}");
                    account = args[++i]; break;
                case "--show-token": showToken = true; break;
                case "--stream": stream = true; break;
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

        return new A2AArgs(token, appId, endpoint, account, graph, workiq, showToken, stream, verbosity, headers, null);

        static A2AArgs Err(string msg) => new(null, null, null, null, false, false, false, false, 1, new List<string>(), msg);
    }

    // ── Response extraction (uses A2A SDK types) ────────────────────────

    public static (string text, string? contextId, Dictionary<string, JsonElement>? metadata) Extract(object response) => response switch
    {
        AgentMessage am => (Join(am), am.ContextId, am.Metadata),
        AgentTask { Status: { State: TaskState.Completed, Message: AgentMessage cm } } t => (Join(cm), t.ContextId, cm.Metadata),
        AgentTask t => ($"[Task {t.Id} — {t.Status.State}]", t.ContextId, null),
        _ => ("(no response)", null, null),
    };

    public static string Join(AgentMessage m) => string.Join("\n", m.Parts.OfType<TextPart>().Select(p => p.Text));
}
