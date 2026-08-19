using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record ResearchPaperMetadataResponse
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("paper")]
    public required ResearchPaperMetadata Paper { get; init; }
}
