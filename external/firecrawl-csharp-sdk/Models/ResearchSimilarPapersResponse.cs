using System.Collections.Generic;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation.Attributes;

namespace FirecrawlApi.Models;

public record ResearchSimilarPapersResponse
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("results")]
    public required IReadOnlyList<ResearchPaperResult> Results { get; init; }

    [JsonPropertyName("poolSize")]
    [Minimum(0)]
    public required int PoolSize { get; init; }

    [JsonPropertyName("truncated")]
    public required bool Truncated { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("note")]
    public string? Note { get; init; }
}
