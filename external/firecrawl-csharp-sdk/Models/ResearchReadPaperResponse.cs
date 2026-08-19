using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record ResearchReadPaperResponse
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("paper")]
    public required ResearchPaperMetadata Paper { get; init; }

    [JsonPropertyName("paperId")]
    public required string PaperId { get; init; }

    [JsonPropertyName("query")]
    public required string Query { get; init; }

    [JsonPropertyName("passages")]
    public required IReadOnlyList<ResearchPassage> Passages { get; init; }
}
