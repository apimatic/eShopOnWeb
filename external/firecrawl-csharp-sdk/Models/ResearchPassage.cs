using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record ResearchPassage
{
    /// <summary>
    /// In-body passage text. May include markdown tables.
    /// </summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>
    /// Dense similarity score for the passage.
    /// </summary>
    [JsonPropertyName("score")]
    public required double Score { get; init; }
}
