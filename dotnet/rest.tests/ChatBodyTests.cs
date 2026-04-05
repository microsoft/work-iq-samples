// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json;
using Xunit;

namespace WorkIQ.Rest.Tests;

/// <summary>
/// Tests for BuildChatBody logic duplicated from rest Program.cs.
/// </summary>
public class ChatBodyTests
{
    // ── Duplicated logic under test ──────────────────────────────────────

    private static string BuildChatBody(string message)
    {
        string tz;
        try { tz = TimeZoneInfo.Local.HasIanaId ? TimeZoneInfo.Local.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? iana : "UTC"; }
        catch { tz = "UTC"; }

        return JsonSerializer.Serialize(new
        {
            message = new { text = message },
            locationHint = new { timeZone = tz },
        });
    }

    // ── Tests ────────────────────────────────────────────────────────────

    [Fact]
    public void BuildChatBody_ContainsMessageText()
    {
        var body = BuildChatBody("Hello");
        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("message").GetProperty("text").GetString();
        Assert.Equal("Hello", text);
    }

    [Fact]
    public void BuildChatBody_OutputIsValidJson()
    {
        var body = BuildChatBody("test");
        var ex = Record.Exception(() => JsonDocument.Parse(body));
        Assert.Null(ex);
    }

    [Fact]
    public void BuildChatBody_SpecialCharacters_ArePreserved()
    {
        var msg = "He said \"hello\"\nNew line\tTab \u00e9";
        var body = BuildChatBody(msg);
        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("message").GetProperty("text").GetString();
        Assert.Equal(msg, text);
    }

    [Fact]
    public void BuildChatBody_ContainsLocationHintTimeZone()
    {
        var body = BuildChatBody("test");
        using var doc = JsonDocument.Parse(body);
        var tz = doc.RootElement.GetProperty("locationHint").GetProperty("timeZone").GetString();
        Assert.NotNull(tz);
        Assert.NotEmpty(tz);
    }

    [Fact]
    public void BuildChatBody_EmptyMessage_StillValid()
    {
        var body = BuildChatBody("");
        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("message").GetProperty("text").GetString();
        Assert.Equal("", text);
    }

    // ── Edge-case tests ─────────────────────────────────────────────────

    [Fact]
    public void BuildChatBody_VeryLongMessage_Handled()
    {
        var longMessage = new string('A', 100_000);
        var body = BuildChatBody(longMessage);
        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("message").GetProperty("text").GetString();
        Assert.Equal(longMessage, text);
    }

    [Fact]
    public void BuildChatBody_JsonInjectionAttempt_Escaped()
    {
        // Attempt to break out of the JSON string value
        var malicious = "\"}, \"evil\": true, {\"";
        var body = BuildChatBody(malicious);
        using var doc = JsonDocument.Parse(body); // must still be valid JSON
        var text = doc.RootElement.GetProperty("message").GetProperty("text").GetString();
        Assert.Equal(malicious, text);
        // The raw JSON must NOT contain an unescaped "evil" key at the top level
        Assert.False(doc.RootElement.TryGetProperty("evil", out _));
    }

    [Fact]
    public void BuildChatBody_NullCharactersInMessage_Handled()
    {
        var msg = "before\0after";
        var body = BuildChatBody(msg);
        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("message").GetProperty("text").GetString();
        Assert.Equal(msg, text);
    }
}
