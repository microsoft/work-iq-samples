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
}
