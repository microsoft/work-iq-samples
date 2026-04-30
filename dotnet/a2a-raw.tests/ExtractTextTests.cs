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

    // ── ExtractText: result.task.artifacts (sync) ───────────────────────

    [Fact]
    public void ExtractText_TaskWithArtifactText_ReturnsArtifactText()
    {
        var el = Parse("""
        {
            "task": {
                "id": "t1",
                "contextId": "ctx-1",
                "status": { "state": "TASK_STATE_COMPLETED" },
                "artifacts": [
                    {
                        "artifactId": "a1",
                        "name": "Answer",
                        "parts": [{ "text": "Answer from artifact" }]
                    }
                ]
            }
        }
        """);
        Assert.Equal("Answer from artifact", Helpers.ExtractText(el));
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
    public void ExtractText_TaskWithEmptyArtifacts_ReturnsEmpty()
    {
        var el = Parse("""
        {
            "task": {
                "id": "t-empty-art",
                "status": { "state": "TASK_STATE_COMPLETED" },
                "artifacts": []
            }
        }
        """);
        Assert.Equal("", Helpers.ExtractText(el));
    }

    [Fact]
    public void ExtractText_TaskNoArtifactsField_ReturnsEmpty()
    {
        // No artifacts field at all — sample treats this as "no answer text".
        var el = Parse("""
        {
            "task": {
                "id": "t-no-art",
                "status": {
                    "state": "TASK_STATE_COMPLETED",
                    "message": { "parts": [{ "text": "ignored chain-of-thought" }] }
                }
            }
        }
        """);
        Assert.Equal("", Helpers.ExtractText(el));
    }

    [Fact]
    public void ExtractText_TaskUnicodeText_PreservedCorrectly()
    {
        var el = Parse("""
        {
            "task": {
                "id": "t-unicode",
                "artifacts": [
                    { "artifactId": "a1", "parts": [{ "text": "🚀 你好世界 مرحبا" }] }
                ]
            }
        }
        """);
        Assert.Equal("🚀 你好世界 مرحبا", Helpers.ExtractText(el));
    }

    // ── ExtractText: result.message (sync, direct Message reply) ────────

    [Fact]
    public void ExtractText_V1MessagePayload_ReturnsText()
    {
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

    [Fact]
    public void ExtractText_V1MessagePayload_MultipleTextParts_Concatenated()
    {
        var el = Parse("""
        {
            "message": {
                "parts": [{ "text": "Hello " }, { "text": "world" }]
            }
        }
        """);
        Assert.Equal("Hello world", Helpers.ExtractText(el));
    }

    // ── ExtractText: shapes that should NOT yield answer text ──────────

    [Fact]
    public void ExtractText_EmptyObject_ReturnsEmpty()
    {
        var el = Parse("{}");
        Assert.Equal("", Helpers.ExtractText(el));
    }

    [Fact]
    public void ExtractText_TaskWithDataOnlyArtifacts_ReturnsEmpty()
    {
        var el = Parse("""
        {
            "task": {
                "id": "t-data",
                "artifacts": [
                    { "artifactId": "a1", "parts": [{ "kind": "data", "data": { "k": "v" } }] }
                ]
            }
        }
        """);
        Assert.Equal("", Helpers.ExtractText(el));
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

    [Fact]
    public void TryGetParts_MultipleTextParts_Concatenated()
    {
        var el = Parse("""{ "parts": [{ "text": "Hello " }, { "text": "world" }] }""");
        var result = Helpers.TryGetParts(el, out var text);
        Assert.True(result);
        Assert.Equal("Hello world", text);
    }

    [Fact]
    public void TryGetParts_MixedPartTypes_OnlyTextExtracted()
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
        var result = Helpers.TryGetParts(el, out var text);
        Assert.True(result);
        Assert.Equal("visible text", text);
    }

    [Fact]
    public void TryGetParts_NullTextInParts_ReturnsFalse()
    {
        // "text": null — GetString() returns null, StringBuilder.Append(null) is a no-op.
        // Resulting text length is 0 → TryGetParts returns false.
        var el = Parse("""{ "parts": [{ "text": null }] }""");
        var result = Helpers.TryGetParts(el, out var text);
        Assert.False(result);
        Assert.Equal("", text);
    }
}
