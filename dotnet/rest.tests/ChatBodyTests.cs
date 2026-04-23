// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json;
using WorkIQ.Rest;
using Xunit;

namespace WorkIQ.Rest.Tests;

/// <summary>
/// Tests for <see cref="Helpers.BuildChatBody"/> from the rest sample app.
/// </summary>
public class ChatBodyTests
{
    [Fact]
    public void BuildChatBody_ContainsMessageText()
    {
        var body = Helpers.BuildChatBody("Hello");
        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("message").GetProperty("text").GetString();
        Assert.Equal("Hello", text);
    }

    [Fact]
    public void BuildChatBody_OutputIsValidJson()
    {
        var body = Helpers.BuildChatBody("test");
        var ex = Record.Exception(() => JsonDocument.Parse(body));
        Assert.Null(ex);
    }

    [Fact]
    public void BuildChatBody_SpecialCharacters_ArePreserved()
    {
        var msg = "He said \"hello\"\nNew line\tTab \u00e9";
        var body = Helpers.BuildChatBody(msg);
        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("message").GetProperty("text").GetString();
        Assert.Equal(msg, text);
    }

    [Fact]
    public void BuildChatBody_ContainsLocationHintTimeZone()
    {
        var body = Helpers.BuildChatBody("test");
        using var doc = JsonDocument.Parse(body);
        var tz = doc.RootElement.GetProperty("locationHint").GetProperty("timeZone").GetString();
        Assert.NotNull(tz);
        Assert.NotEmpty(tz);
    }

    [Fact]
    public void BuildChatBody_EmptyMessage_StillValid()
    {
        var body = Helpers.BuildChatBody("");
        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("message").GetProperty("text").GetString();
        Assert.Equal("", text);
    }

    // ── Edge-case tests ─────────────────────────────────────────────────

    [Fact]
    public void BuildChatBody_VeryLongMessage_Handled()
    {
        var longMessage = new string('A', 100_000);
        var body = Helpers.BuildChatBody(longMessage);
        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("message").GetProperty("text").GetString();
        Assert.Equal(longMessage, text);
    }

    [Fact]
    public void BuildChatBody_JsonInjectionAttempt_Escaped()
    {
        // Attempt to break out of the JSON string value
        var malicious = "\"}, \"evil\": true, {\"";
        var body = Helpers.BuildChatBody(malicious);
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
        var body = Helpers.BuildChatBody(msg);
        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("message").GetProperty("text").GetString();
        Assert.Equal(msg, text);
    }
}
