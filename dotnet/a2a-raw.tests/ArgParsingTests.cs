// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using WorkIQ.A2ARaw;
using Xunit;

namespace WorkIQ.A2ARaw.Tests;

/// <summary>
/// Tests for <see cref="Helpers.ParseArgs"/> — the arg-parsing logic
/// used by the a2a-raw sample app.
/// </summary>
public class ArgParsingTests
{
    [Fact]
    public void ValidArgs_AllParsedCorrectly()
    {
        var r = Helpers.ParseArgs(["--endpoint", "https://example.com", "--token", "abc", "--appid", "id1", "--stream"]);
        Assert.Null(r.Error);
        Assert.Equal("https://example.com", r.Endpoint);
        Assert.Equal("abc", r.Token);
        Assert.Equal("id1", r.AppId);
        Assert.True(r.Stream);
    }

    [Fact]
    public void ShortFlags_Work()
    {
        var r = Helpers.ParseArgs(["-e", "https://example.com", "-t", "tok", "-a", "app"]);
        Assert.Null(r.Error);
        Assert.Equal("https://example.com", r.Endpoint);
        Assert.Equal("tok", r.Token);
        Assert.Equal("app", r.AppId);
    }

    [Fact]
    public void UnknownFlag_ReturnsError()
    {
        var r = Helpers.ParseArgs(["--unknown"]);
        Assert.NotNull(r.Error);
        Assert.Contains("Unknown flag", r.Error);
    }

    [Fact]
    public void AllHeadersFlag_Parsed()
    {
        var r = Helpers.ParseArgs(["--all-headers", "--endpoint", "url", "--token", "t"]);
        Assert.True(r.AllHeaders);
    }

    [Fact]
    public void AccountFlag_Parsed()
    {
        var r = Helpers.ParseArgs(["--endpoint", "url", "--token", "t", "--account", "user@example.com"]);
        Assert.Equal("user@example.com", r.Account);
    }

    [Fact]
    public void MissingValueAfterToken_ReturnsError()
    {
        var r = Helpers.ParseArgs(["--token"]);
        Assert.NotNull(r.Error);
        Assert.Contains("Missing value", r.Error);
        Assert.Contains("--token", r.Error);
    }

    [Fact]
    public void MissingValueAfterEndpoint_ReturnsError()
    {
        var r = Helpers.ParseArgs(["--endpoint"]);
        Assert.NotNull(r.Error);
        Assert.Contains("Missing value", r.Error);
        Assert.Contains("--endpoint", r.Error);
    }

    [Fact]
    public void MissingValueAfterShortFlag_ReturnsError()
    {
        var r = Helpers.ParseArgs(["-t"]);
        Assert.NotNull(r.Error);
        Assert.Contains("Missing value", r.Error);
    }

    [Fact]
    public void EmptyArgs_NoError()
    {
        var r = Helpers.ParseArgs([]);
        Assert.Null(r.Error);
        Assert.Null(r.Endpoint);
        Assert.Null(r.Token);
    }
}
