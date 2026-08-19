using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record DeveloperSearchResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("success")]
    public bool? Success { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("results")]
    public IReadOnlyList<DeveloperSearchResult>? Results { get; init; }

    /// <summary>
    /// Outcome for each result type. Check this when an expected result type is missing: <c>skipped</c> means your <c>types</c> value did not ask for that type, while <c>degraded</c> or <c>unavailable</c> means the gap came from the index or from a filter, not from the query. A repository filter is one such cause — see <see href="/api-reference/endpoint/developer-search#how-the-repository-filters-scope-a-search">how the repository filters scope a search</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("coverage")]
    public Coverage? Coverage { get; init; }

    /// <summary>
    /// Whether the ranked list went through the reranking stage.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reranked")]
    public bool? Reranked { get; init; }

    /// <summary>
    /// Present only when <c>repos</c> was sent. Echoes each slug with whether it is indexed, plus a per-type breakdown under <c>types</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("repos")]
    public IReadOnlyList<Repo>? Repos { get; init; }

    /// <summary>
    /// Present only when <c>sources</c> was sent. Reports each id exactly as requested along with whether it is indexed. <c>indexed: true</c> means the source has a published generation, so documentation evidence from it may appear; <c>indexed: false</c> means nothing from that id can match, which distinguishes an id that is not in the index from a query that simply found nothing.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sources")]
    public IReadOnlyList<Source>? Sources { get; init; }
}
