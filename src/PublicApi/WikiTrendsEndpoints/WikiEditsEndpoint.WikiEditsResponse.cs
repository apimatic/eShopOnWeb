using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.WikiTrendsEndpoints;

public record WikiEditsResponse
{
    [JsonPropertyName("received")]
    public int Received { get; init; }

    [JsonPropertyName("matches")]
    public IReadOnlyList<WikiEditDto> Matches { get; init; } = [];

    [JsonPropertyName("latestCommons")]
    public IReadOnlyList<WikiEditDto> LatestCommons { get; init; } = [];

    [JsonPropertyName("stoppedBecause")]
    public string StoppedBecause { get; init; } = "time-limit";
}

public record WikiEditDto
{
    [JsonPropertyName("wiki")]
    public string Wiki { get; init; } = "";

    [JsonPropertyName("pageTitle")]
    public string PageTitle { get; init; } = "";

    [JsonPropertyName("revisionId")]
    public int RevisionId { get; init; }

    [JsonPropertyName("editorName")]
    public string? EditorName { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("slots")]
    public IReadOnlyList<RevisionSlotDto> Slots { get; init; } = [];
}

public record RevisionSlotDto
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("contentModel")]
    public string ContentModel { get; init; } = "";

    [JsonPropertyName("sizeBytes")]
    public int SizeBytes { get; init; }
}
