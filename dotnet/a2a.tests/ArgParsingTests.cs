// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Xunit;

namespace WorkIQ.A2A.Tests;

/// <summary>
/// Tests for arg-parsing logic duplicated from a2a Program.cs.
/// </summary>
public class ArgParsingTests
{
    // ── Duplicated types and logic ───────────────────────────────────────

    private record GatewayConfig(string Name, string Endpoint, string[] Scopes, string Authority, string[] ExtraHeaders);
    private record Config(string Token, string AppId, GatewayConfig Gateway, string? Account, bool ShowToken, int Verbosity, bool Stream);

    private static readonly GatewayConfig Graph = new(
        Name: "Graph RP",
        Endpoint: "https://graph.microsoft.com/rp/workiq/",
        Scopes: ["https://graph.microsoft.com/.default"],
        Authority: "https://login.microsoftonline.com/common",
        ExtraHeaders: []);

    private static readonly GatewayConfig WorkIQ = new(
        Name: "WorkIQ Gateway",
        Endpoint: "",
        Scopes: [],
        Authority: "https://login.microsoftonline.com/common",
        ExtraHeaders: []);

    private static Config? ParseArgs(string[] args)
    {
        string? token = null, appId = null, endpoint = null, account = null;
        bool graph = false, workiq = false, showToken = false, stream = false;
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
                case "--endpoint" or "-e": endpoint = args[++i]; break;
                case "--account": account = args[++i]; break;
                case "--show-token": showToken = true; break;
                case "--stream": stream = true; break;
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

        var gw = graph ? Graph : WorkIQ;
        if (!string.IsNullOrEmpty(endpoint))
            gw = gw with { Endpoint = endpoint };
        if (headers.Count > 0)
            gw = gw with { ExtraHeaders = [.. gw.ExtraHeaders, .. headers] };

        return new Config(token, appId ?? "", gw, account, showToken, verbosity, stream);
    }

    // ── Tests ────────────────────────────────────────────────────────────

    [Fact]
    public void ValidArgs_ProduceCorrectConfig()
    {
        var result = ParseArgs(["--graph", "--token", "mytoken", "--appid", "app1", "--stream"]);
        Assert.NotNull(result);
        Assert.Equal("mytoken", result.Token);
        Assert.Equal("app1", result.AppId);
        Assert.True(result.Stream);
        Assert.Equal("Graph RP", result.Gateway.Name);
    }

    [Fact]
    public void MissingToken_ReturnsNull()
    {
        Assert.Null(ParseArgs(["--graph"]));
    }

    [Fact]
    public void MissingGateway_ReturnsNull()
    {
        Assert.Null(ParseArgs(["--token", "abc"]));
    }

    [Fact]
    public void GraphAndWorkiqTogether_ReturnsNull()
    {
        Assert.Null(ParseArgs(["--graph", "--workiq", "--token", "abc"]));
    }

    [Fact]
    public void WamWithoutAppid_ReturnsNull()
    {
        Assert.Null(ParseArgs(["--graph", "--token", "WAM"]));
    }

    [Fact]
    public void EndpointOverride_AppliedToGateway()
    {
        var result = ParseArgs(["--graph", "--token", "t", "--endpoint", "https://custom.com/"]);
        Assert.NotNull(result);
        Assert.Equal("https://custom.com/", result.Gateway.Endpoint);
    }

    [Fact]
    public void HeaderValues_AreCollected()
    {
        var result = ParseArgs(["--graph", "--token", "t", "--header", "X-Custom: v1", "-H", "X-Other: v2"]);
        Assert.NotNull(result);
        Assert.Equal(2, result.Gateway.ExtraHeaders.Length);
    }

    [Fact]
    public void VerbosityWithNonInteger_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => ParseArgs(["--graph", "--token", "t", "--verbosity", "abc"]));
    }

    [Fact]
    public void MissingValueAfterToken_ThrowsIndexOutOfRange()
    {
        Assert.Throws<IndexOutOfRangeException>(() => ParseArgs(["--graph", "--token"]));
    }

    [Fact]
    public void DefaultVerbosity_IsOne()
    {
        var result = ParseArgs(["--graph", "--token", "t"]);
        Assert.NotNull(result);
        Assert.Equal(1, result.Verbosity);
    }
}
