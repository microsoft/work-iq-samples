// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json;
using A2A;
using WorkIQ.A2A;
using Xunit;

namespace WorkIQ.A2A.Tests;

/// <summary>
/// Tests for <see cref="Helpers.Extract"/> and <see cref="Helpers.Join"/> from the a2a sample app.
/// </summary>
public class ExtractTests
{
    // ── Extract tests ───────────────────────────────────────────────────

    [Fact]
    public void Extract_AgentMessage_ReturnsTextAndContext()
    {
        var msg = new AgentMessage
        {
            Role = MessageRole.Agent,
            MessageId = "m1",
            ContextId = "ctx-1",
            Parts = [new TextPart { Text = "Hello from agent" }],
        };

        var (text, contextId, _) = Helpers.Extract(msg);
        Assert.Equal("Hello from agent", text);
        Assert.Equal("ctx-1", contextId);
    }

    [Fact]
    public void Extract_AgentMessage_WithMetadata_ReturnsMetadata()
    {
        var meta = new Dictionary<string, JsonElement>
        {
            ["key"] = JsonSerializer.SerializeToElement("value"),
        };
        var msg = new AgentMessage
        {
            Role = MessageRole.Agent,
            MessageId = "m2",
            Parts = [new TextPart { Text = "test" }],
            Metadata = meta,
        };

        var (_, _, metadata) = Helpers.Extract(msg);
        Assert.NotNull(metadata);
        Assert.Equal("value", metadata["key"].GetString());
    }

    [Fact]
    public void Extract_CompletedAgentTask_ReturnsMessageText()
    {
        var agentMsg = new AgentMessage
        {
            Role = MessageRole.Agent,
            MessageId = "m3",
            Parts = [new TextPart { Text = "Task done" }],
        };
        var task = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-2",
            Status = new AgentTaskStatus { State = TaskState.Completed, Message = agentMsg },
        };

        var (text, contextId, _) = Helpers.Extract(task);
        Assert.Equal("Task done", text);
        Assert.Equal("ctx-2", contextId);
    }

    [Fact]
    public void Extract_NonCompletedAgentTask_ReturnsStatusString()
    {
        var task = new AgentTask
        {
            Id = "t2",
            ContextId = "ctx-3",
            Status = new AgentTaskStatus { State = TaskState.Working },
        };

        var (text, contextId, metadata) = Helpers.Extract(task);
        Assert.Contains("t2", text);
        Assert.Contains("Working", text);
        Assert.Equal("ctx-3", contextId);
        Assert.Null(metadata);
    }

    [Fact]
    public void Extract_UnknownType_ReturnsNoResponse()
    {
        var (text, contextId, metadata) = Helpers.Extract("some random object");
        Assert.Equal("(no response)", text);
        Assert.Null(contextId);
        Assert.Null(metadata);
    }

    // ── Join tests ──────────────────────────────────────────────────────

    [Fact]
    public void Join_MultipleTextParts_JoinedWithNewline()
    {
        var msg = new AgentMessage
        {
            Role = MessageRole.Agent,
            MessageId = "m4",
            Parts = [new TextPart { Text = "Line 1" }, new TextPart { Text = "Line 2" }],
        };

        Assert.Equal("Line 1\nLine 2", Helpers.Join(msg));
    }

    [Fact]
    public void Join_EmptyParts_ReturnsEmptyString()
    {
        var msg = new AgentMessage
        {
            Role = MessageRole.Agent,
            MessageId = "m5",
            Parts = [],
        };

        Assert.Equal("", Helpers.Join(msg));
    }

    [Fact]
    public void Join_NonTextParts_Filtered()
    {
        var msg = new AgentMessage
        {
            Role = MessageRole.Agent,
            MessageId = "m6",
            Parts = [new TextPart { Text = "visible" }, new DataPart { Data = new Dictionary<string, JsonElement>() }],
        };

        Assert.Equal("visible", Helpers.Join(msg));
    }

    [Fact]
    public void Join_MixedParts_OnlyTextPartsIncluded()
    {
        var msg = new AgentMessage
        {
            Role = MessageRole.Agent,
            MessageId = "m7",
            Parts =
            [
                new TextPart { Text = "A" },
                new DataPart { Data = new Dictionary<string, JsonElement>() },
                new TextPart { Text = "B" },
                new FilePart { File = new FileContent(bytes: new byte[] { 0 }) { Name = "file.txt" } },
                new TextPart { Text = "C" },
            ],
        };

        Assert.Equal("A\nB\nC", Helpers.Join(msg));
    }

    // ── Edge-case tests ─────────────────────────────────────────────────

    [Fact]
    public void Extract_AgentMessage_EmptyParts_ReturnsEmptyString()
    {
        var msg = new AgentMessage
        {
            Role = MessageRole.Agent,
            MessageId = "m-empty",
            Parts = [],
        };

        var (text, _, _) = Helpers.Extract(msg);
        Assert.Equal("", text);
    }

    [Fact]
    public void Extract_AgentMessage_NullContextId_ReturnsNull()
    {
        var msg = new AgentMessage
        {
            Role = MessageRole.Agent,
            MessageId = "m-no-ctx",
            Parts = [new TextPart { Text = "test" }],
            // ContextId not set — defaults to null
        };

        var (text, contextId, _) = Helpers.Extract(msg);
        Assert.Equal("test", text);
        Assert.Null(contextId);
    }
}
