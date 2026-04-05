// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using System.Text.Json;
using Xunit;

namespace WorkIQ.A2ARaw.Tests;

/// <summary>
/// Tests for ExtractText / TryGetParts logic duplicated from a2a-raw Program.cs.
/// These are local functions in the top-level program so we replicate them here.
/// </summary>
public class ExtractTextTests
{
    // ── Duplicated logic under test ──────────────────────────────────────

    private static string ExtractText(JsonElement el)
    {
        if (TryGetParts(el, out var text)) return text;
        if (el.TryGetProperty("status", out var status) &&
            status.TryGetProperty("message", out var msg) &&
            TryGetParts(msg, out text)) return text;
        if (el.TryGetProperty("message", out var m) &&
            TryGetParts(m, out text)) return text;
        return "";
    }

    private static bool TryGetParts(JsonElement el, out string text)
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

    // ── Helpers ──────────────────────────────────────────────────────────

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
        Assert.Equal("Hello world", ExtractText(el));
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
        Assert.Equal("Task completed", ExtractText(el));
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
        Assert.Equal("From message", ExtractText(el));
    }

    [Fact]
    public void ExtractText_EmptyObject_ReturnsEmpty()
    {
        var el = Parse("{}");
        Assert.Equal("", ExtractText(el));
    }

    [Fact]
    public void ExtractText_MissingParts_ReturnsEmpty()
    {
        var el = Parse("""{ "status": { "message": {} } }""");
        Assert.Equal("", ExtractText(el));
    }

    [Fact]
    public void ExtractText_MultipleParts_Concatenated()
    {
        var el = Parse("""{ "parts": [{ "text": "Hello " }, { "text": "world" }] }""");
        Assert.Equal("Hello world", ExtractText(el));
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
        Assert.Equal("visible text", ExtractText(el));
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
        Assert.Equal("direct", ExtractText(el));
    }

    // ── TryGetParts tests ───────────────────────────────────────────────

    [Fact]
    public void TryGetParts_MissingPartsProperty_ReturnsFalse()
    {
        var el = Parse("""{ "other": 123 }""");
        var result = TryGetParts(el, out var text);
        Assert.False(result);
        Assert.Equal("", text);
    }

    [Fact]
    public void TryGetParts_NonArrayParts_ReturnsFalse()
    {
        var el = Parse("""{ "parts": "not-an-array" }""");
        var result = TryGetParts(el, out var text);
        Assert.False(result);
        Assert.Equal("", text);
    }

    [Fact]
    public void TryGetParts_EmptyArray_ReturnsFalse()
    {
        var el = Parse("""{ "parts": [] }""");
        var result = TryGetParts(el, out var text);
        Assert.False(result);
        Assert.Equal("", text);
    }

    [Fact]
    public void TryGetParts_PartsWithNoTextProperty_ReturnsFalse()
    {
        var el = Parse("""{ "parts": [{ "kind": "data" }] }""");
        var result = TryGetParts(el, out var text);
        Assert.False(result);
        Assert.Equal("", text);
    }

    [Fact]
    public void TryGetParts_ValidParts_ReturnsTrueWithText()
    {
        var el = Parse("""{ "parts": [{ "text": "ok" }] }""");
        var result = TryGetParts(el, out var text);
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
        var result = ExtractText(el);
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
        Assert.Equal("Here is the answer: 42", ExtractText(el));
    }

    [Fact]
    public void ExtractText_UnicodeText_PreservedCorrectly()
    {
        // Emoji, CJK characters, and RTL text
        var el = Parse("""{ "parts": [{ "text": "🚀 你好世界 مرحبا" }] }""");
        Assert.Equal("🚀 你好世界 مرحبا", ExtractText(el));
    }
}
