using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record Repo
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("repo")]
    public string? RepoValue { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("indexed")]
    public bool? Indexed { get; init; }

    /// <summary>
    /// Which result types are indexed for this repository: <c>issue</c>, <c>pullRequest</c>, and <c>readme</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("types")]
    public Types? Types { get; init; }
}
