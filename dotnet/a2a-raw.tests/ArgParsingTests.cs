// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Xunit;

namespace WorkIQ.A2ARaw.Tests;

/// <summary>
/// Tests for arg-parsing logic duplicated from a2a-raw Program.cs.
/// The original uses args[++i] which will throw IndexOutOfRangeException
/// if a flag requiring a value is the last argument.
/// </summary>
public class ArgParsingTests
{
    // ── Duplicated arg parsing logic ─────────────────────────────────────

    private record ParseResult(
        string? Endpoint, string? Token, string? AppId, string? Account,
        bool Stream, bool AllHeaders, string? Error);

    private static ParseResult ParseArgs(string[] args)
    {
        string? endpoint = null, token = null, appId = null, account = null;
        bool stream = false, allHeaders = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--endpoint" or "-e": endpoint = args[++i]; break;
                case "--token" or "-t": token = args[++i]; break;
                case "--appid" or "-a": appId = args[++i]; break;
                case "--account": account = args[++i]; break;
                case "--stream": stream = true; break;
                case "--all-headers": allHeaders = true; break;
                default:
                    return new ParseResult(null, null, null, null, false, false, $"Unknown flag: {args[i]}");
            }
        }

        return new ParseResult(endpoint, token, appId, account, stream, allHeaders, null);
    }

    // ── Tests ────────────────────────────────────────────────────────────

    [Fact]
    public void ValidArgs_AllParsedCorrectly()
    {
        var result = ParseArgs(["--endpoint", "https://example.com", "--token", "abc", "--appid", "id1", "--stream"]);
        Assert.Null(result.Error);
        Assert.Equal("https://example.com", result.Endpoint);
        Assert.Equal("abc", result.Token);
        Assert.Equal("id1", result.AppId);
        Assert.True(result.Stream);
    }

    [Fact]
    public void ShortFlags_Work()
    {
        var result = ParseArgs(["-e", "https://example.com", "-t", "tok", "-a", "app"]);
        Assert.Null(result.Error);
        Assert.Equal("https://example.com", result.Endpoint);
        Assert.Equal("tok", result.Token);
        Assert.Equal("app", result.AppId);
    }

    [Fact]
    public void UnknownFlag_ReturnsError()
    {
        var result = ParseArgs(["--unknown"]);
        Assert.NotNull(result.Error);
        Assert.Contains("Unknown flag", result.Error);
    }

    [Fact]
    public void AllHeadersFlag_Parsed()
    {
        var result = ParseArgs(["--all-headers", "--endpoint", "url", "--token", "t"]);
        Assert.True(result.AllHeaders);
    }

    [Fact]
    public void AccountFlag_Parsed()
    {
        var result = ParseArgs(["--endpoint", "url", "--token", "t", "--account", "user@example.com"]);
        Assert.Equal("user@example.com", result.Account);
    }

    [Fact]
    public void MissingValueAfterToken_ThrowsIndexOutOfRange()
    {
        // Bug: args[++i] throws when --token is the last arg with no value
        Assert.Throws<IndexOutOfRangeException>(() => ParseArgs(["--token"]));
    }

    [Fact]
    public void MissingValueAfterEndpoint_ThrowsIndexOutOfRange()
    {
        Assert.Throws<IndexOutOfRangeException>(() => ParseArgs(["--endpoint"]));
    }

    [Fact]
    public void EmptyArgs_NoError()
    {
        var result = ParseArgs([]);
        Assert.Null(result.Error);
        Assert.Null(result.Endpoint);
        Assert.Null(result.Token);
    }
}
