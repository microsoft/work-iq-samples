// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json;
using A2A;
using WorkIQ.A2A;
using Xunit;

namespace WorkIQ.A2A.Tests;

/// <summary>
/// Tests for <see cref="Helpers.Extract"/>, <see cref="Helpers.ExtractTextFromTask"/>,
/// and <see cref="Helpers.JoinText"/> from the a2a sample app.
///
/// The sample reads answer text from <c>Artifacts[].Parts</c>. Citation
/// metadata is still read from <c>Status.Message.Metadata</c> (will move to a
/// DataPart on the artifact in a later server release).
/// </summary>
public class ExtractTests
{
    // ── Extract: SendMessageResponse with Message payload ───────────────

    [Fact]
    public void Extract_MessagePayload_ReturnsTextAndContext()
    {
        var msg = new Message
        {
            Role = Role.Agent,
            MessageId = "m1",
            ContextId = "ctx-1",
            Parts = [Part.FromText("Hello from agent")],
        };
        var response = new SendMessageResponse { Message = msg };

        var (text, contextId, _) = Helpers.Extract(response);
        Assert.Equal("Hello from agent", text);
        Assert.Equal("ctx-1", contextId);
    }

    [Fact]
    public void Extract_MessagePayload_WithMetadata_ReturnsMetadata()
    {
        var meta = new Dictionary<string, JsonElement>
        {
            ["key"] = JsonSerializer.SerializeToElement("value"),
        };
        var msg = new Message
        {
            Role = Role.Agent,
            MessageId = "m2",
            Parts = [Part.FromText("test")],
            Metadata = meta,
        };
        var response = new SendMessageResponse { Message = msg };

        var (_, _, metadata) = Helpers.Extract(response);
        Assert.NotNull(metadata);
        Assert.Equal("value", metadata["key"].GetString());
    }

    // ── Extract: SendMessageResponse with Task payload ──────────────────

    [Fact]
    public void Extract_TaskWithArtifactText_ReturnsArtifactText()
    {
        var task = new AgentTask
        {
            Id = "t1",
            ContextId = "ctx-2",
            Status = new global::A2A.TaskStatus { State = TaskState.Completed },
            Artifacts =
            [
                new Artifact
                {
                    ArtifactId = "a1",
                    Name = "Answer",
                    Parts = [Part.FromText("Answer from artifact")],
                },
            ],
        };
        var response = new SendMessageResponse { Task = task };

        var (text, contextId, _) = Helpers.Extract(response);
        Assert.Equal("Answer from artifact", text);
        Assert.Equal("ctx-2", contextId);
    }

    [Fact]
    public void Extract_TaskWithMultipleArtifacts_ConcatenatesTextParts()
    {
        var task = new AgentTask
        {
            Id = "t-multi",
            ContextId = "ctx-multi",
            Status = new global::A2A.TaskStatus { State = TaskState.Completed },
            Artifacts =
            [
                new Artifact { ArtifactId = "a1", Parts = [Part.FromText("One")] },
                new Artifact { ArtifactId = "a2", Parts = [Part.FromText("Two")] },
            ],
        };
        var response = new SendMessageResponse { Task = task };

        var (text, _, _) = Helpers.Extract(response);
        Assert.Equal("One\nTwo", text);
    }

    [Fact]
    public void Extract_TaskCompletedNoArtifacts_ReturnsStatusPlaceholder()
    {
        var task = new AgentTask
        {
            Id = "t-empty",
            ContextId = "ctx-empty",
            Status = new global::A2A.TaskStatus { State = TaskState.Completed },
        };
        var response = new SendMessageResponse { Task = task };

        var (text, contextId, metadata) = Helpers.Extract(response);
        Assert.Contains("t-empty", text);
        Assert.Contains("Completed", text);
        Assert.Equal("ctx-empty", contextId);
        Assert.Null(metadata);
    }

    [Fact]
    public void Extract_NonCompletedTask_ReturnsStatusPlaceholder()
    {
        var task = new AgentTask
        {
            Id = "t-working",
            ContextId = "ctx-w",
            Status = new global::A2A.TaskStatus { State = TaskState.Working },
        };
        var response = new SendMessageResponse { Task = task };

        var (text, contextId, _) = Helpers.Extract(response);
        Assert.Contains("t-working", text);
        Assert.Contains("Working", text);
        Assert.Equal("ctx-w", contextId);
    }

    [Fact]
    public void Extract_TaskWithDataOnlyArtifacts_ReturnsStatusPlaceholder()
    {
        // Artifact has only a DataPart (no text) — the sample shows the task
        // status as a placeholder rather than the JSON.
        var task = new AgentTask
        {
            Id = "t-data-only",
            ContextId = "ctx-d",
            Status = new global::A2A.TaskStatus { State = TaskState.Completed },
            Artifacts =
            [
                new Artifact
                {
                    ArtifactId = "a-d",
                    Parts = [Part.FromData(JsonDocument.Parse("{\"k\":\"v\"}").RootElement)],
                },
            ],
        };
        var response = new SendMessageResponse { Task = task };

        var (text, _, _) = Helpers.Extract(response);
        Assert.Contains("t-data-only", text);
        Assert.Contains("Completed", text);
    }

    [Fact]
    public void Extract_TaskCitationsCarriedFromStatusMessageMetadata()
    {
        // Citations remain in Status.Message.Metadata until the DataPart migration ships.
        var attributions = JsonSerializer.SerializeToElement(new[] { new { providerDisplayName = "Org Chart" } });
        var task = new AgentTask
        {
            Id = "t-citations",
            ContextId = "ctx-c",
            Status = new global::A2A.TaskStatus
            {
                State = TaskState.Completed,
                Message = new Message
                {
                    Role = Role.Agent,
                    MessageId = "m-c",
                    Parts = [],
                    Metadata = new Dictionary<string, JsonElement>
                    {
                        ["attributions"] = attributions,
                    },
                },
            },
            Artifacts =
            [
                new Artifact { ArtifactId = "a-c", Parts = [Part.FromText("Your manager is Bob.")] },
            ],
        };
        var response = new SendMessageResponse { Task = task };

        var (_, _, metadata) = Helpers.Extract(response);
        Assert.NotNull(metadata);
        Assert.True(metadata.ContainsKey("attributions"));
    }

    [Fact]
    public void Extract_EmptyResponse_ReturnsNoResponse()
    {
        var response = new SendMessageResponse(); // PayloadCase == None

        var (text, contextId, metadata) = Helpers.Extract(response);
        Assert.Equal("(no response)", text);
        Assert.Null(contextId);
        Assert.Null(metadata);
    }

    // ── JoinText tests ──────────────────────────────────────────────────

    [Fact]
    public void JoinText_MultipleTextParts_JoinedWithNewline()
    {
        var parts = new[] { Part.FromText("Line 1"), Part.FromText("Line 2") };
        Assert.Equal("Line 1\nLine 2", Helpers.JoinText(parts));
    }

    [Fact]
    public void JoinText_EmptyEnumerable_ReturnsEmpty()
    {
        Assert.Equal("", Helpers.JoinText([]));
    }

    [Fact]
    public void JoinText_NonTextParts_Filtered()
    {
        var parts = new[]
        {
            Part.FromText("visible"),
            Part.FromData(JsonDocument.Parse("{}").RootElement),
        };
        Assert.Equal("visible", Helpers.JoinText(parts));
    }

    [Fact]
    public void JoinText_MixedParts_OnlyTextPartsIncluded()
    {
        var parts = new[]
        {
            Part.FromText("A"),
            Part.FromData(JsonDocument.Parse("{}").RootElement),
            Part.FromText("B"),
            Part.FromUrl("https://example.com/file.txt", mediaType: "text/plain", filename: "file.txt"),
            Part.FromText("C"),
        };
        Assert.Equal("A\nB\nC", Helpers.JoinText(parts));
    }
}
