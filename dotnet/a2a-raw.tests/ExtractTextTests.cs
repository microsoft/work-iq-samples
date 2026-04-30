// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json;
using WorkIQ.A2ARaw;
using Xunit;

namespace WorkIQ.A2ARaw.Tests;

/// <summary>
/// Tests for <see cref="Helpers.ExtractText"/> and <see cref="Helpers.TryGetParts"/>
/// from the a2a-raw sample app.
/// </summary>
public class ExtractTextTests
{
    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    // ── ExtractText tests ───────────────────────────────────────────────

    [Fact]
    public void ExtractText_DirectParts_ReturnsText()
    {
        var el = Parse("""{ "parts": [{ "text": "Hello world" }] }""");
        Assert.Equal("Hello world", Helpers.ExtractText(el));
    }

    [Fact]
    public void ExtractText_StatusMessageParts_ReturnsText()
    {
        var el = Parse("""
        {
            "status": {
                "message": {
                    "parts": [{ "text": "Task completed" }]
                }
            }
        }
        """);
        Assert.Equal("Task completed", Helpers.ExtractText(el));
    }

    [Fact]
    public void ExtractText_MessageParts_ReturnsText()
    {
        var el = Parse("""
        {
            "message": {
                "parts": [{ "text": "From message" }]
            }
        }
        """);
        Assert.Equal("From message", Helpers.ExtractText(el));
    }

    [Fact]
    public void ExtractText_EmptyObject_ReturnsEmpty()
    {
        var el = Parse("{}");
        Assert.Equal("", Helpers.ExtractText(el));
    }

    [Fact]
    public void ExtractText_MissingParts_ReturnsEmpty()
    {
        var el = Parse("""{ "status": { "message": {} } }""");
        Assert.Equal("", Helpers.ExtractText(el));
    }

    [Fact]
    public void ExtractText_MultipleParts_Concatenated()
    {
        var el = Parse("""{ "parts": [{ "text": "Hello " }, { "text": "world" }] }""");
        Assert.Equal("Hello world", Helpers.ExtractText(el));
    }

    [Fact]
    public void ExtractText_MixedPartTypes_OnlyTextExtracted()
    {
        var el = Parse("""
        {
            "parts": [
                { "text": "visible" },
                { "kind": "data", "data": "abc" },
                { "text": " text" }
            ]
        }
        """);
        Assert.Equal("visible text", Helpers.ExtractText(el));
    }

    [Fact]
    public void ExtractText_PrefersDirectParts_OverStatusMessage()
    {
        var el = Parse("""
        {
            "parts": [{ "text": "direct" }],
            "status": { "message": { "parts": [{ "text": "nested" }] } }
        }
        """);
        Assert.Equal("direct", Helpers.ExtractText(el));
    }

    // ── TryGetParts tests ───────────────────────────────────────────────

    [Fact]
    public void TryGetParts_MissingPartsProperty_ReturnsFalse()
    {
        var el = Parse("""{ "other": 123 }""");
        var result = Helpers.TryGetParts(el, out var text);
        Assert.False(result);
        Assert.Equal("", text);
    }

    [Fact]
    public void TryGetParts_NonArrayParts_ReturnsFalse()
    {
        var el = Parse("""{ "parts": "not-an-array" }""");
        var result = Helpers.TryGetParts(el, out var text);
        Assert.False(result);
        Assert.Equal("", text);
    }

    [Fact]
    public void TryGetParts_EmptyArray_ReturnsFalse()
    {
        var el = Parse("""{ "parts": [] }""");
        var result = Helpers.TryGetParts(el, out var text);
        Assert.False(result);
        Assert.Equal("", text);
    }

    [Fact]
    public void TryGetParts_PartsWithNoTextProperty_ReturnsFalse()
    {
        var el = Parse("""{ "parts": [{ "kind": "data" }] }""");
        var result = Helpers.TryGetParts(el, out var text);
        Assert.False(result);
        Assert.Equal("", text);
    }

    [Fact]
    public void TryGetParts_ValidParts_ReturnsTrueWithText()
    {
        var el = Parse("""{ "parts": [{ "text": "ok" }] }""");
        var result = Helpers.TryGetParts(el, out var text);
        Assert.True(result);
        Assert.Equal("ok", text);
    }

    // ── Edge-case tests ─────────────────────────────────────────────────

    [Fact]
    public void ExtractText_NullTextInParts_HandledGracefully()
    {
        // "text": null — GetString() returns null, StringBuilder.Append(null) is a no-op.
        var el = Parse("""{ "parts": [{ "text": null }] }""");
        // TryGetParts returns false because appended text length is 0.
        var result = Helpers.ExtractText(el);
        Assert.Equal("", result);
    }

    [Fact]
    public void ExtractText_DeeplyNestedStatusMessage_Works()
    {
        // Realistic server response with full status.message.parts path
        var json = """
        {
            "id": "task-123",
            "status": {
                "state": "completed",
                "message": {
                    "role": "agent",
                    "parts": [
                        { "text": "Here is the answer: " },
                        { "text": "42" }
                    ]
                }
            },
            "contextId": "ctx-abc"
        }
        """;
        var el = Parse(json);
        Assert.Equal("Here is the answer: 42", Helpers.ExtractText(el));
    }

    [Fact]
    public void ExtractText_UnicodeText_PreservedCorrectly()
    {
        // Emoji, CJK characters, and RTL text
        var el = Parse("""{ "parts": [{ "text": "🚀 你好世界 مرحبا" }] }""");
        Assert.Equal("🚀 你好世界 مرحبا", Helpers.ExtractText(el));
    }

    // ── A2A v1.0 dual-shape tests ──────────────────────────────────────────
    //
    // Sydney is mid-rollout for the "answer-as-artifact" change (commit fedb1c9 /
    // PR 5114178). During the rollout window, response text may arrive in either
    // shape. ExtractText must prefer the new artifact path while falling back to
    // the legacy status.message path when artifacts are absent.

    [Fact]
    public void ExtractText_TaskWithBothShapes_PrefersStatusMessage()
    {
        // Shape-based rule: when result.task.status.message has text, that's a
        // pre-fedb1c9 ring still emitting in the legacy location — use it
        // regardless of what's in artifacts.
        var el = Parse("""
        {
            "task": {
                "id": "t1",
                "contextId": "ctx-1",
                "status": {
                    "state": "TASK_STATE_COMPLETED",
                    "message": { "parts": [{ "text": "LEGACY answer" }] }
                },
                "artifacts": [
                    {
                        "artifactId": "a1",
                        "name": "Answer",
                        "parts": [{ "text": "artifact text" }]
                    }
                ]
            }
        }
        """);
        Assert.Equal("LEGACY answer", Helpers.ExtractText(el));
    }

    [Fact]
    public void ExtractText_TaskWithArtifactFragment_PicksStatusMessage()
    {
        // Real rollout case: the artifact carries a tail fragment ("\")" from
        // a citation marker) while the full answer lives in status.message.
        // Length-pick correctly returns the status.message text.
        var el = Parse("""
        {
            "task": {
                "id": "t-frag",
                "status": {
                    "state": "TASK_STATE_COMPLETED",
                    "message": {
                        "parts": [{ "text": "The actual long answer body the agent produced for the user." }]
                    }
                },
                "artifacts": [
                    {
                        "artifactId": "a-frag",
                        "parts": [{ "text": "\")" }]
                    }
                ]
            }
        }
        """);
        Assert.Equal("The actual long answer body the agent produced for the user.", Helpers.ExtractText(el));
    }

    [Fact]
    public void ExtractText_TaskOnlyStatusMessage_FallsBackToLegacy()
    {
        // Pre-fedb1c9 server: text in result.task.status.message.parts only.
        var el = Parse("""
        {
            "task": {
                "id": "t-legacy",
                "status": {
                    "state": "TASK_STATE_COMPLETED",
                    "message": { "parts": [{ "text": "Legacy answer" }] }
                }
            }
        }
        """);
        Assert.Equal("Legacy answer", Helpers.ExtractText(el));
    }

    [Fact]
    public void ExtractText_TaskArtifactsAcrossMultipleArtifacts_AllConcatenated()
    {
        var el = Parse("""
        {
            "task": {
                "id": "t-multi",
                "artifacts": [
                    { "artifactId": "a1", "parts": [{ "text": "Part1 " }] },
                    { "artifactId": "a2", "parts": [{ "text": "Part2" }] }
                ]
            }
        }
        """);
        Assert.Equal("Part1 Part2", Helpers.ExtractText(el));
    }

    [Fact]
    public void ExtractText_StreamingArtifactUpdate_ReturnsArtifactText()
    {
        // Streaming event: result.artifactUpdate.artifact.parts (preferred).
        var el = Parse("""
        {
            "artifactUpdate": {
                "taskId": "t1",
                "contextId": "ctx-1",
                "artifact": {
                    "artifactId": "a1",
                    "parts": [{ "text": "streamed" }]
                }
            }
        }
        """);
        Assert.Equal("streamed", Helpers.ExtractText(el));
    }

    [Fact]
    public void ExtractText_StreamingStatusUpdate_LegacyMessageText()
    {
        // Streaming event from a pre-fedb1c9 ring: text in statusUpdate.status.message.
        var el = Parse("""
        {
            "statusUpdate": {
                "taskId": "t1",
                "contextId": "ctx-1",
                "status": {
                    "state": "TASK_STATE_WORKING",
                    "message": { "parts": [{ "text": "thinking..." }] }
                }
            }
        }
        """);
        Assert.Equal("thinking...", Helpers.ExtractText(el));
    }

    [Fact]
    public void ExtractText_StreamingStatusUpdateNoMessage_ReturnsEmpty()
    {
        // Terminal state with no message attached — common shape.
        var el = Parse("""
        {
            "statusUpdate": {
                "taskId": "t1",
                "contextId": "ctx-1",
                "status": { "state": "TASK_STATE_COMPLETED" }
            }
        }
        """);
        Assert.Equal("", Helpers.ExtractText(el));
    }

    [Fact]
    public void ExtractText_TaskWithEmptyArtifacts_FallsBackToStatusMessage()
    {
        var el = Parse("""
        {
            "task": {
                "id": "t-empty-art",
                "status": {
                    "message": { "parts": [{ "text": "from status" }] }
                },
                "artifacts": []
            }
        }
        """);
        Assert.Equal("from status", Helpers.ExtractText(el));
    }

    [Fact]
    public void ExtractText_V1MessagePayload_ReturnsText()
    {
        // result.message — direct Message reply (no task at all).
        var el = Parse("""
        {
            "message": {
                "role": "ROLE_AGENT",
                "messageId": "m1",
                "contextId": "ctx-1",
                "parts": [{ "text": "Direct message reply" }]
            }
        }
        """);
        Assert.Equal("Direct message reply", Helpers.ExtractText(el));
    }
}
