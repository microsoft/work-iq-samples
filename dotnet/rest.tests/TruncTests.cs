// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Xunit;

namespace WorkIQ.Rest.Tests;

/// <summary>
/// Tests for the Trunc utility duplicated from rest Program.cs.
/// </summary>
public class TruncTests
{
    // ── Duplicated logic under test ──────────────────────────────────────

    private static string Trunc(string s, int max) => s.Length <= max ? s : $"{s[..max]}...";

    // ── Tests ────────────────────────────────────────────────────────────

    [Fact]
    public void Trunc_ShorterThanMax_ReturnsSameString()
    {
        Assert.Equal("hi", Trunc("hi", 10));
    }

    [Fact]
    public void Trunc_ExactlyAtMax_ReturnsSameString()
    {
        Assert.Equal("hello", Trunc("hello", 5));
    }

    [Fact]
    public void Trunc_LongerThanMax_TruncatesWithEllipsis()
    {
        Assert.Equal("hel...", Trunc("hello world", 3));
    }

    [Fact]
    public void Trunc_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", Trunc("", 5));
    }

    [Fact]
    public void Trunc_MaxOfOne_TruncatesCorrectly()
    {
        Assert.Equal("h...", Trunc("hello", 1));
    }
}
