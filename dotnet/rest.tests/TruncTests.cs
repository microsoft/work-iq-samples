// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using WorkIQ.Rest;
using Xunit;

namespace WorkIQ.Rest.Tests;

/// <summary>
/// Tests for <see cref="Helpers.Trunc"/> from the rest sample app.
/// </summary>
public class TruncTests
{
    [Fact]
    public void Trunc_ShorterThanMax_ReturnsSameString()
    {
        Assert.Equal("hi", Helpers.Trunc("hi", 10));
    }

    [Fact]
    public void Trunc_ExactlyAtMax_ReturnsSameString()
    {
        Assert.Equal("hello", Helpers.Trunc("hello", 5));
    }

    [Fact]
    public void Trunc_LongerThanMax_TruncatesWithEllipsis()
    {
        Assert.Equal("hel...", Helpers.Trunc("hello world", 3));
    }

    [Fact]
    public void Trunc_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", Helpers.Trunc("", 5));
    }

    [Fact]
    public void Trunc_MaxOfOne_TruncatesCorrectly()
    {
        Assert.Equal("h...", Helpers.Trunc("hello", 1));
    }
}
