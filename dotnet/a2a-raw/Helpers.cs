// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using System.Text.Json;

namespace WorkIQ.A2ARaw;

public record RawArgs(
    string? Endpoint, string? Token, string? AppId, string? Account,
    string? Scope, string? Tenant, string? AgentId,
    bool Stream, bool AllHeaders, bool ShowWire, string? Error);

public static class Helpers
{
    // ── Arg parsing ─────────────────────────────────────────────────────

    public static RawArgs ParseArgs(string[] args)
    {
        string? endpoint = null, token = null, appId = null, account = null, scope = null, tenant = null, agentId = null;
        bool stream = false, allHeaders = false, showWire = false;

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
                case "--stream": stream = true; break;
                case "--all-headers": allHeaders = true; break;
                case "--show-wire": showWire = true; break;
                default:
                    return Err($"Unknown flag: {args[i]}");
            }
        }

        return new RawArgs(endpoint, token, appId, account, scope, tenant, agentId, stream, allHeaders, showWire, null);

        static RawArgs Err(string msg) => new(null, null, null, null, null, null, null, false, false, false, msg);
    }

    // ── A2A v1.0 response text extraction ──────────────────────────────────
    //
    // Walks an unwrapped JSON-RPC `result` object (or a single streaming event)
    // and returns the response text. Tries the v1.0 shapes in preference order:
    //   1. result.task.artifacts[].parts[text]                 ← sync, post-fedb1c9 (preferred)
    //   2. result.task.status.message.parts[text]              ← sync, pre-fedb1c9 (legacy fallback)
    //   3. result.message.parts[text]                          ← sync, direct Message payload
    //   4. result.artifactUpdate.artifact.parts[text]          ← streaming, post-fedb1c9 (preferred)
    //   5. result.statusUpdate.status.message.parts[text]      ← streaming, pre-fedb1c9 (legacy fallback)
    //   6. <root>.parts[text]                                  ← already-unwrapped (caller convenience)
    //   7. <root>.status.message.parts[text]                   ← already-unwrapped (legacy)
    //
    // TODO(post-fedb1c9 WW rollout, ~early May 2026): drop the legacy fallback
    // branches (#2, #5, #7) once Sydney's "answer-as-artifact" change has fully
    // rolled out. Tracking: Sydney master commit fedb1c9 / PR 5114178.

    public static string ExtractText(JsonElement el)
    {
        // 1, 2: sync `task` payload. Use the shape-based discriminator: if
        // status.message.parts has text, the server is still on the legacy
        // emission path (pre-fedb1c9) — use it. If status.message is empty,
        // the answer lives in artifacts (post-fedb1c9). Some transitional
        // rings populate both (artifact carries a fragment while status.
        // message has the full answer); the legacy-first check correctly
        // picks the full answer.
        if (el.TryGetProperty("task", out var task))
        {
            if (task.TryGetProperty("status", out var status) &&
                status.TryGetProperty("message", out var msg) &&
                TryGetParts(msg, out var fromStatusMessage))
                return fromStatusMessage;

            if (TryGetArtifactsText(task, out var fromArtifacts)) return fromArtifacts;
        }

        // 3: sync `message` payload (direct reply, no task).
        if (el.TryGetProperty("message", out var m) && TryGetParts(m, out var msgText))
            return msgText;

        // 4: streaming `artifactUpdate` — extract text from the artifact's parts.
        if (el.TryGetProperty("artifactUpdate", out var au) &&
            au.TryGetProperty("artifact", out var artifact) &&
            TryGetParts(artifact, out var auText)) return auText;

        // 5: streaming `statusUpdate` legacy — text in status.message.parts.
        if (el.TryGetProperty("statusUpdate", out var su) &&
            su.TryGetProperty("status", out var sStatus) &&
            sStatus.TryGetProperty("message", out var sMsg) &&
            TryGetParts(sMsg, out var suText)) return suText;

        // 6: caller passed an already-unwrapped object with parts at the root.
        if (TryGetParts(el, out var directText)) return directText;

        // 7: caller passed a task/agent-message-shaped object with status.message.parts.
        if (el.TryGetProperty("status", out var status2) &&
            status2.TryGetProperty("message", out var msg2) &&
            TryGetParts(msg2, out var legacyText2)) return legacyText2;

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
