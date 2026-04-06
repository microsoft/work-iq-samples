// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using WorkIQ.A2A;
using Xunit;

namespace WorkIQ.A2A.Tests;

/// <summary>
/// Tests for <see cref="Helpers.ParseArgs"/> — the arg-parsing logic
/// used by the a2a sample app.
/// </summary>
public class ArgParsingTests
{
    [Fact]
    public void ValidArgs_ProduceCorrectConfig()
    {
        var r = Helpers.ParseArgs(["--graph", "--token", "mytoken", "--appid", "app1", "--stream"]);
        Assert.Null(r.Error);
        Assert.Equal("mytoken", r.Token);
        Assert.Equal("app1", r.AppId);
        Assert.True(r.Stream);
        Assert.True(r.Graph);
    }

    [Fact]
    public void MissingToken_ReturnsNullToken()
    {
        var r = Helpers.ParseArgs(["--graph"]);
        Assert.Null(r.Error);
        Assert.Null(r.Token);
    }

    [Fact]
    public void MissingGateway_NoGatewayFlagSet()
    {
        var r = Helpers.ParseArgs(["--token", "abc"]);
        Assert.Null(r.Error);
        Assert.False(r.Graph);
        Assert.False(r.Workiq);
    }

    [Fact]
    public void GraphAndWorkiqTogether_BothFlagsSet()
    {
        // Mutual-exclusion is enforced at the Program.cs layer, not here.
        var r = Helpers.ParseArgs(["--graph", "--workiq", "--token", "abc"]);
        Assert.Null(r.Error);
        Assert.True(r.Graph);
        Assert.True(r.Workiq);
    }

    [Fact]
    public void EndpointOverride_Captured()
    {
        var r = Helpers.ParseArgs(["--graph", "--token", "t", "--endpoint", "https://custom.com/"]);
        Assert.Null(r.Error);
        Assert.Equal("https://custom.com/", r.Endpoint);
    }

    [Fact]
    public void HeaderValues_AreCollected()
    {
        var r = Helpers.ParseArgs(["--graph", "--token", "t", "--header", "X-Custom: v1", "-H", "X-Other: v2"]);
        Assert.Null(r.Error);
        Assert.Equal(2, r.Headers.Count);
    }

    [Fact]
    public void VerbosityWithNonInteger_ReturnsError()
    {
        var r = Helpers.ParseArgs(["--graph", "--token", "t", "--verbosity", "abc"]);
        Assert.NotNull(r.Error);
        Assert.Contains("integer", r.Error);
    }

    [Fact]
    public void VerbosityWithInteger_Parsed()
    {
        var r = Helpers.ParseArgs(["--graph", "--token", "t", "--verbosity", "3"]);
        Assert.Null(r.Error);
        Assert.Equal(3, r.Verbosity);
    }

    [Fact]
    public void MissingValueAfterToken_ReturnsError()
    {
        var r = Helpers.ParseArgs(["--graph", "--token"]);
        Assert.NotNull(r.Error);
        Assert.Contains("Missing value", r.Error);
    }

    [Fact]
    public void MissingValueAfterEndpoint_ReturnsError()
    {
        var r = Helpers.ParseArgs(["--graph", "--token", "t", "--endpoint"]);
        Assert.NotNull(r.Error);
        Assert.Contains("Missing value", r.Error);
    }

    [Fact]
    public void DefaultVerbosity_IsOne()
    {
        var r = Helpers.ParseArgs(["--graph", "--token", "t"]);
        Assert.Null(r.Error);
        Assert.Equal(1, r.Verbosity);
    }
}
