using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

/// <summary>
/// Outcome for each result type. Check this when an expected result type is missing: <c>skipped</c> means your <c>types</c> value did not ask for that type, while <c>degraded</c> or <c>unavailable</c> means the gap came from the index or from a filter, not from the query. A repository filter is one such cause — see <see href="/api-reference/endpoint/developer-search#how-the-repository-filters-scope-a-search">how the repository filters scope a search</see>.
/// </summary>
public record Coverage
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("doc")]
    public Doc? Doc { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("issue")]
    public Issue? Issue { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("pull_request")]
    public PullRequest? PullRequest { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("readme")]
    public Readme? Readme { get; init; }
}
