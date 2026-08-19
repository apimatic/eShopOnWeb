using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation.Attributes;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record SearchDeveloperRequest
{
    [JsonPropertyName("query")]
    [MinLength(1)]
    public required string Query { get; init; }

    [JsonPropertyName("k")]
    [Minimum(1)]
    [Maximum(100)]
    public int? K { get; init; } = 10;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("types")]
    public IReadOnlyList<Types1>? Types { get; init; }

    /// <summary>
    /// Repository slugs to scope the repository half of the index to. Applies to the <c>issue</c>, <c>pull_request</c>, and <c>readme</c> types only. Sent together with <c>sources</c>, the two halves are combined rather than intersected. Returns 400 when no repository type is in <c>types</c>, reporting that <c>repos</c> cannot match any requested type and that you should add repository types or drop <c>repos</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("repos")]
    public IReadOnlyList<string>? Repos { get; init; }

    /// <summary>
    /// Documentation source ids to scope the documentation half to, at most 20. Applies to the <c>doc</c> type only. Not a fixed enum: ids reflect the documentation sites in the index and the set grows over time. Returns 400 with <c>sources cannot match any requested type; add doc or drop sources</c> when <c>doc</c> is not in <c>types</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sources")]
    [MaxLength(20)]
    public IReadOnlyList<string>? Sources { get; init; }

    /// <summary>
    /// Set to <c>only</c> to limit the search to indexed agent-skill files.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("skills")]
    public Skills1? Skills { get; init; }

    [JsonPropertyName("passages")]
    [Minimum(1)]
    [Maximum(5)]
    public int? Passages { get; init; } = 1;

    /// <summary>
    /// Repository primary language, such as <c>Rust</c>. Applies to repository results only; sending it with no <c>sources</c> scope returns no <c>doc</c> results. See <see href="/api-reference/endpoint/developer-search#how-the-repository-filters-scope-a-search">how the repository filters scope a search</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("language")]
    public string? Language { get; init; }

    /// <summary>
    /// Repository topic, such as <c>async</c>. Applies to repository results only; sending it with no <c>sources</c> scope returns no <c>doc</c> results.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("topic")]
    public string? Topic { get; init; }

    /// <summary>
    /// Repository license, such as <c>MIT</c>. Applies to repository results only; sending it with no <c>sources</c> scope returns no <c>doc</c> results.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("license")]
    public string? License { get; init; }

    /// <summary>
    /// Lower bound on repository stars. Applies to repository results only; sending it with no <c>sources</c> scope returns no <c>doc</c> results.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("min_stars")]
    [Minimum(0)]
    public int? MinStars { get; init; }

    /// <summary>
    /// Upper bound on repository stars. Applies to repository results only; sending it with no <c>sources</c> scope returns no <c>doc</c> results.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("max_stars")]
    [Minimum(0)]
    public int? MaxStars { get; init; }

    /// <summary>
    /// Include or exclude archived repositories. Applies to repository results only; sending it with no <c>sources</c> scope returns no <c>doc</c> results.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("archived")]
    public bool? Archived { get; init; }

    /// <summary>
    /// Include or exclude forks. Applies to repository results only; sending it with no <c>sources</c> scope returns no <c>doc</c> results.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("fork")]
    public bool? Fork { get; init; }
}
