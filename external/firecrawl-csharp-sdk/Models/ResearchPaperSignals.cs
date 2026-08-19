using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation.Attributes;

namespace FirecrawlApi.Models;

public record ResearchPaperSignals
{
    /// <summary>
    /// Raw structural graph signal.
    /// </summary>
    [JsonPropertyName("structural")]
    public required double Structural { get; init; }

    /// <summary>
    /// Semantic score from the intent search.
    /// </summary>
    [JsonPropertyName("semantic")]
    public required double Semantic { get; init; }

    /// <summary>
    /// Structural expansion article-rank score.
    /// </summary>
    [JsonPropertyName("articleRank")]
    public required double ArticleRank { get; init; }

    /// <summary>
    /// Number of distinct seeds connected to this candidate.
    /// </summary>
    [JsonPropertyName("seedOverlap")]
    [Minimum(0)]
    public required int SeedOverlap { get; init; }
}
