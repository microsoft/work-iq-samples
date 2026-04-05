// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Xunit;

namespace WorkIQ.Rest.Tests;

/// <summary>
/// Tests for arg-parsing logic duplicated from rest Program.cs.
/// </summary>
public class ArgParsingTests
{
    // ── Duplicated types and logic ───────────────────────────────────────

    private record Config(string Token, string AppId, string? Account, bool Stream, bool ShowToken, int Verbosity, List<string> Headers);

    private static Config? ParseArgs(string[] args)
    {
        string? token = null, appId = null, account = null;
        bool graph = false, workiq = false, stream = false, showToken = false;
        int verbosity = 1;
        var headers = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--graph": graph = true; break;
                case "--workiq": workiq = true; break;
                case "--token" or "-t": token = args[++i]; break;
                case "--appid" or "-a": appId = args[++i]; break;
                case "--account": account = args[++i]; break;
                case "--stream": stream = true; break;
                case "--show-token": showToken = true; break;
                case "--verbosity" or "-v": verbosity = int.Parse(args[++i]); break;
                case "--header" or "-H": headers.Add(args[++i]); break;
            }
        }

        if (string.IsNullOrEmpty(token) || (!graph && !workiq))
            return null;

        if (graph && workiq)
            return null;

        if (workiq)
            return null;

        if (token.Equals("WAM", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(appId))
            return null;

        return new Config(token, appId ?? "", account, stream, showToken, verbosity, headers);
    }

    // ── Tests ────────────────────────────────────────────────────────────

    [Fact]
    public void GraphAndWorkiqTogether_ReturnsNull()
    {
        var result = ParseArgs(["--graph", "--workiq", "--token", "abc"]);
        Assert.Null(result);
    }

    [Fact]
    public void WamWithoutAppid_ReturnsNull()
    {
        var result = ParseArgs(["--graph", "--token", "WAM"]);
        Assert.Null(result);
    }

    [Fact]
    public void MissingToken_ReturnsNull()
    {
        var result = ParseArgs(["--graph"]);
        Assert.Null(result);
    }

    [Fact]
    public void MissingGateway_ReturnsNull()
    {
        var result = ParseArgs(["--token", "abc"]);
        Assert.Null(result);
    }

    [Fact]
    public void ValidArgs_ProduceCorrectConfig()
    {
        var result = ParseArgs(["--graph", "--token", "mytoken", "--appid", "app1", "--stream", "--account", "user@test.com"]);
        Assert.NotNull(result);
        Assert.Equal("mytoken", result.Token);
        Assert.Equal("app1", result.AppId);
        Assert.Equal("user@test.com", result.Account);
        Assert.True(result.Stream);
    }

    [Fact]
    public void VerbosityWithNonInteger_ThrowsFormatException()
    {
        // Bug: int.Parse will throw if the value isn't an integer
        Assert.Throws<FormatException>(() => ParseArgs(["--graph", "--token", "t", "--verbosity", "abc"]));
    }

    [Fact]
    public void HeaderValues_AreCollected()
    {
        var result = ParseArgs(["--graph", "--token", "t", "--header", "X-Custom: value1", "-H", "X-Other: value2"]);
        Assert.NotNull(result);
        Assert.Equal(2, result.Headers.Count);
        Assert.Equal("X-Custom: value1", result.Headers[0]);
        Assert.Equal("X-Other: value2", result.Headers[1]);
    }

    [Fact]
    public void ShowToken_Parsed()
    {
        var result = ParseArgs(["--graph", "--token", "t", "--show-token"]);
        Assert.NotNull(result);
        Assert.True(result.ShowToken);
    }

    [Fact]
    public void DefaultVerbosity_IsOne()
    {
        var result = ParseArgs(["--graph", "--token", "t"]);
        Assert.NotNull(result);
        Assert.Equal(1, result.Verbosity);
    }

    [Fact]
    public void MissingValueAfterToken_ThrowsIndexOutOfRange()
    {
        Assert.Throws<IndexOutOfRangeException>(() => ParseArgs(["--graph", "--token"]));
    }

    // ── Edge-case tests ─────────────────────────────────────────────────

    [Fact]
    public void WorkiqGateway_ReturnsNull_WithMessage()
    {
        // --workiq is recognized but not yet implemented — always returns null
        var result = ParseArgs(["--workiq", "--token", "X"]);
        Assert.Null(result);
    }

    [Fact]
    public void StreamFlag_SetsStreamTrue()
    {
        var result = ParseArgs(["--graph", "--token", "t", "--stream"]);
        Assert.NotNull(result);
        Assert.True(result.Stream);
    }
}
