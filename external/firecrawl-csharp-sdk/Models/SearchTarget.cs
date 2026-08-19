using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation.Attributes;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

/// <summary>
/// Runs web search queries on each check and alerts on new results that match the monitor's goal. Requires a non-empty top-level <c>goal</c> on the monitor unless <c>judgeEnabled</c> is <c>false</c>.
/// </summary>
public record SearchTarget
{
    /// <summary>
    /// Optional stable ID for this target. Generated if omitted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    public Guid? Id { get; init; }

    [JsonPropertyName("type")]
    public required Type29 Type { get; init; }

    /// <summary>
    /// Search queries to run on each check (1-12).
    /// </summary>
    [JsonPropertyName("queries")]
    [MinLength(1)]
    [MaxLength(12)]
    public required IReadOnlyList<string> Queries { get; init; }

    /// <summary>
    /// Recency filter — only consider results published within this window.
    /// </summary>
    [JsonPropertyName("searchWindow")]
    public SearchWindow? SearchWindow { get; init; } = SearchWindow._24H;

    /// <summary>
    /// Total results to evaluate per check, merged and deduped across all queries (a combined cap, not per-query).
    /// </summary>
    [JsonPropertyName("maxResults")]
    [Minimum(1)]
    [Maximum(50)]
    public int? MaxResults { get; init; } = 10;

    /// <summary>
    /// Optional. Restrict results to these domains.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("includeDomains")]
    [MaxLength(50)]
    public IReadOnlyList<string>? IncludeDomains { get; init; }

    /// <summary>
    /// Optional. Drop results from these domains.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("excludeDomains")]
    [MaxLength(50)]
    public IReadOnlyList<string>? ExcludeDomains { get; init; }
}
