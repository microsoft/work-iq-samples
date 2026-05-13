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
        var r = Helpers.ParseArgs(["--token", "mytoken", "--appid", "app1", "--stream"]);
        Assert.Null(r.Error);
        Assert.Equal("mytoken", r.Token);
        Assert.Equal("app1", r.AppId);
        Assert.True(r.Stream);
    }

    [Fact]
    public void MissingToken_ReturnsNullToken()
    {
        var r = Helpers.ParseArgs([]);
        Assert.Null(r.Error);
        Assert.Null(r.Token);
    }

    [Fact]
    public void UnknownFlag_ReturnsError()
    {
        var r = Helpers.ParseArgs(["--token", "t", "--stre"]);
        Assert.NotNull(r.Error);
        Assert.Contains("Unknown flag", r.Error);
        Assert.Contains("--stre", r.Error);
    }

    [Fact]
    public void EndpointOverride_Captured()
    {
        var r = Helpers.ParseArgs(["--token", "t", "--endpoint", "https://custom.com/"]);
        Assert.Null(r.Error);
        Assert.Equal("https://custom.com/", r.Endpoint);
    }

    [Fact]
    public void HeaderValues_AreCollected()
    {
        var r = Helpers.ParseArgs(["--token", "t", "--header", "X-Custom: v1", "-H", "X-Other: v2"]);
        Assert.Null(r.Error);
        Assert.Equal(2, r.Headers.Count);
    }

    [Fact]
    public void VerbosityWithNonInteger_ReturnsError()
    {
        var r = Helpers.ParseArgs(["--token", "t", "--verbosity", "abc"]);
        Assert.NotNull(r.Error);
        Assert.Contains("integer", r.Error);
    }

    [Fact]
    public void VerbosityWithInteger_Parsed()
    {
        var r = Helpers.ParseArgs(["--token", "t", "--verbosity", "3"]);
        Assert.Null(r.Error);
        Assert.Equal(3, r.Verbosity);
    }

    [Fact]
    public void MissingValueAfterToken_ReturnsError()
    {
        var r = Helpers.ParseArgs(["--token"]);
        Assert.NotNull(r.Error);
        Assert.Contains("Missing value", r.Error);
    }

    [Fact]
    public void MissingValueAfterEndpoint_ReturnsError()
    {
        var r = Helpers.ParseArgs(["--token", "t", "--endpoint"]);
        Assert.NotNull(r.Error);
        Assert.Contains("Missing value", r.Error);
    }

    [Fact]
    public void DefaultVerbosity_IsOne()
    {
        var r = Helpers.ParseArgs(["--token", "t"]);
        Assert.Null(r.Error);
        Assert.Equal(1, r.Verbosity);
    }

    [Fact]
    public void AgentId_LongFlag_Captured()
    {
        var r = Helpers.ParseArgs(["--token", "t", "--agent-id", "researcher-v2"]);
        Assert.Null(r.Error);
        Assert.Equal("researcher-v2", r.AgentId);
    }

    [Fact]
    public void AgentId_ShortFlag_Captured()
    {
        var r = Helpers.ParseArgs(["--token", "t", "-A", "researcher-v2"]);
        Assert.Null(r.Error);
        Assert.Equal("researcher-v2", r.AgentId);
    }

    [Fact]
    public void AgentId_NotProvided_IsNull()
    {
        var r = Helpers.ParseArgs(["--token", "t"]);
        Assert.Null(r.Error);
        Assert.Null(r.AgentId);
    }

    [Fact]
    public void AgentId_MissingValue_ReturnsError()
    {
        var r = Helpers.ParseArgs(["--token", "t", "--agent-id"]);
        Assert.NotNull(r.Error);
        Assert.Contains("Missing value", r.Error);
    }

    [Fact]
    public void ShowWire_LongFlag_Sets()
    {
        var r = Helpers.ParseArgs(["--token", "t", "--show-wire"]);
        Assert.Null(r.Error);
        Assert.True(r.ShowWire);
    }

    [Fact]
    public void ShowWire_NotProvided_IsFalse()
    {
        var r = Helpers.ParseArgs(["--token", "t"]);
        Assert.Null(r.Error);
        Assert.False(r.ShowWire);
    }

    [Fact]
    public void ShowWire_IndependentOfVerbosity()
    {
        var r = Helpers.ParseArgs(["--token", "t", "--show-wire", "-v", "0"]);
        Assert.Null(r.Error);
        Assert.True(r.ShowWire);
        Assert.Equal(0, r.Verbosity);
    }
}
