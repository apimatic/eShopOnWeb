using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record ResearchSearchPapersResponse
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("results")]
    public required IReadOnlyList<ResearchPaperResult> Results { get; init; }
}
