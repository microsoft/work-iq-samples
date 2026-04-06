// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using WorkIQ.Rest;
using Xunit;

namespace WorkIQ.Rest.Tests;

/// <summary>
/// Tests for <see cref="Helpers.ParseArgs"/> — the arg-parsing logic
/// used by the rest sample app.
/// </summary>
public class ArgParsingTests
{
    [Fact]
    public void ValidArgs_ProduceCorrectConfig()
    {
        var r = Helpers.ParseArgs(["--graph", "--token", "mytoken", "--appid", "app1", "--stream", "--account", "user@test.com"]);
        Assert.Null(r.Error);
        Assert.Equal("mytoken", r.Token);
        Assert.Equal("app1", r.AppId);
        Assert.Equal("user@test.com", r.Account);
        Assert.True(r.Stream);
        Assert.True(r.Graph);
    }

    [Fact]
    public void GraphAndWorkiqTogether_BothFlagsSet()
    {
        // The Helpers.ParseArgs layer just extracts flags — mutual-exclusion is
        // enforced at the Program.cs Config-construction layer.
        var r = Helpers.ParseArgs(["--graph", "--workiq", "--token", "abc"]);
        Assert.Null(r.Error);
        Assert.True(r.Graph);
        Assert.True(r.Workiq);
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
    public void VerbosityWithNonInteger_ReturnsError()
    {
        var r = Helpers.ParseArgs(["--graph", "--token", "t", "--verbosity", "abc"]);
        Assert.NotNull(r.Error);
        Assert.Contains("integer", r.Error);
    }

    [Fact]
    public void VerbosityWithInteger_Parsed()
    {
        var r = Helpers.ParseArgs(["--graph", "--token", "t", "--verbosity", "2"]);
        Assert.Null(r.Error);
        Assert.Equal(2, r.Verbosity);
    }

    [Fact]
    public void HeaderValues_AreCollected()
    {
        var r = Helpers.ParseArgs(["--graph", "--token", "t", "--header", "X-Custom: value1", "-H", "X-Other: value2"]);
        Assert.Null(r.Error);
        Assert.Equal(2, r.Headers.Count);
        Assert.Equal("X-Custom: value1", r.Headers[0]);
        Assert.Equal("X-Other: value2", r.Headers[1]);
    }

    [Fact]
    public void ShowToken_Parsed()
    {
        var r = Helpers.ParseArgs(["--graph", "--token", "t", "--show-token"]);
        Assert.Null(r.Error);
        Assert.True(r.ShowToken);
    }

    [Fact]
    public void DefaultVerbosity_IsOne()
    {
        var r = Helpers.ParseArgs(["--graph", "--token", "t"]);
        Assert.Null(r.Error);
        Assert.Equal(1, r.Verbosity);
    }

    [Fact]
    public void MissingValueAfterToken_ReturnsError()
    {
        var r = Helpers.ParseArgs(["--graph", "--token"]);
        Assert.NotNull(r.Error);
        Assert.Contains("Missing value", r.Error);
    }

    [Fact]
    public void MissingValueAfterHeader_ReturnsError()
    {
        var r = Helpers.ParseArgs(["--graph", "--token", "t", "--header"]);
        Assert.NotNull(r.Error);
        Assert.Contains("Missing value", r.Error);
    }

    [Fact]
    public void MissingValueAfterVerbosity_ReturnsError()
    {
        var r = Helpers.ParseArgs(["--graph", "--token", "t", "--verbosity"]);
        Assert.NotNull(r.Error);
        Assert.Contains("Missing value", r.Error);
    }

    [Fact]
    public void WorkiqGateway_FlagRecognized()
    {
        var r = Helpers.ParseArgs(["--workiq", "--token", "X"]);
        Assert.Null(r.Error);
        Assert.True(r.Workiq);
    }

    [Fact]
    public void StreamFlag_SetsStreamTrue()
    {
        var r = Helpers.ParseArgs(["--graph", "--token", "t", "--stream"]);
        Assert.Null(r.Error);
        Assert.True(r.Stream);
    }
}
