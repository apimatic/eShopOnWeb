using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record SupportDocsSearchRequest
{
    /// <summary>
    /// Documentation question to answer.
    /// </summary>
    [JsonPropertyName("question")]
    public required string Question { get; init; }
}
