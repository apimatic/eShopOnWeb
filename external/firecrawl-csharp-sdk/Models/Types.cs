using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// Which result types are indexed for this repository: <c>issue</c>, <c>pullRequest</c>, and <c>readme</c>.
/// </summary>
public record Types
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("issue")]
    public bool? Issue { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("pullRequest")]
    public bool? PullRequest { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("readme")]
    public bool? Readme { get; init; }
}
