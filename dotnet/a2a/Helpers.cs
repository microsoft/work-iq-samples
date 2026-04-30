// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json;
using A2A;

namespace WorkIQ.A2A;

public record A2AArgs(
    string? Token, string? AppId, string? Endpoint, string? Account, string? Tenant,
    string? AgentId,
    bool ShowToken, bool Stream, bool ShowWire,
    int Verbosity, List<string> Headers, string? Error);

public static class Helpers
{
    // ── Arg parsing ─────────────────────────────────────────────────────

    public static A2AArgs ParseArgs(string[] args)
    {
        string? token = null, appId = null, endpoint = null, account = null, tenant = null, agentId = null;
        bool showToken = false, stream = false, showWire = false;
        int verbosity = 1;
        var headers = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
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
                case "--tenant" or "-T":
                    if (i + 1 >= args.Length) return Err($"Missing value for {args[i]}");
                    tenant = args[++i]; break;
                case "--agent-id" or "-A":
                    if (i + 1 >= args.Length) return Err($"Missing value for {args[i]}");
                    agentId = args[++i]; break;
                case "--show-token": showToken = true; break;
                case "--stream": stream = true; break;
                case "--show-wire": showWire = true; break;
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

        return new A2AArgs(token, appId, endpoint, account, tenant, agentId, showToken, stream, showWire, verbosity, headers, null);

        static A2AArgs Err(string msg) => new(null, null, null, null, null, null, false, false, false, 1, new List<string>(), msg);
    }

    // ── Sync response extraction (A2A SDK v1.0 SendMessageResponse) ─────

    public static (string text, string? contextId, Dictionary<string, JsonElement>? metadata) Extract(SendMessageResponse response) => response.PayloadCase switch
    {
        SendMessageResponseCase.Message => (
            JoinText(response.Message!.Parts),
            response.Message.ContextId,
            response.Message.Metadata),
        SendMessageResponseCase.Task => (
            ExtractTextFromTask(response.Task!),
            response.Task.ContextId,
            response.Task.Status.Message?.Metadata),    // citations: still in Status.Message.Metadata until DataPart migration ships
        _ => ("(no response)", null, null),
    };

    // Sydney's "answer-as-artifact" change (master commit fedb1c9 / PR 5114178)
    // is mid-rollout. The shape-based discriminator: if Status.Message.Parts has
    // text, this is a pre-fedb1c9 ring still emitting the answer in the legacy
    // location — use it. If Status.Message has no text, this is a post-fedb1c9
    // ring and the answer lives in Artifacts. Some transitional rings populate
    // both (artifact carries a fragment of citation-marker tail; full answer is
    // in Status.Message); the rule still picks the right one because we check
    // Status.Message first.
    // TODO(post-fedb1c9 WW rollout, ~early May 2026): drop the Status.Message
    // arm and use Artifacts[].Parts directly.
    public static string ExtractTextFromTask(AgentTask task)
    {
        if (task.Status.Message?.Parts is { } msgParts)
        {
            var fromStatusMessage = JoinText(msgParts);
            if (!string.IsNullOrEmpty(fromStatusMessage)) return fromStatusMessage;
        }

        var fromArtifacts = JoinText(
            (task.Artifacts ?? new List<Artifact>()).SelectMany(a => a.Parts));
        if (!string.IsNullOrEmpty(fromArtifacts)) return fromArtifacts;

        return $"[Task {task.Id} — {task.Status.State}]";
    }

    public static string JoinText(IEnumerable<Part> parts) =>
        string.Join("\n", parts.Where(p => p.ContentCase == PartContentCase.Text).Select(p => p.Text!));
}
