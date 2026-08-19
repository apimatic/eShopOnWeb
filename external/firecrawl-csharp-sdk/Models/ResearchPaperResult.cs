using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record ResearchPaperResult
{
    /// <summary>
    /// Canonical paper id, or web:&lt;url&gt; for SERP-discovered display results.
    /// </summary>
    [JsonPropertyName("paperId")]
    public required string PaperId { get; init; }

    /// <summary>
    /// Preferred cite/fetch id such as arxiv:&lt;id&gt;, pmid:&lt;id&gt;, pmcid:&lt;id&gt;, or doi:&lt;id&gt;.
    /// </summary>
    [JsonPropertyName("primaryId")]
    public required string PrimaryId { get; init; }

    /// <summary>
    /// Source identifiers grouped by namespace.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ids")]
    public IReadOnlyDictionary<string, object>? Ids { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("abstract")]
    public required string Abstract { get; init; }

    [JsonPropertyName("score")]
    public required double Score { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("signals")]
    public ResearchPaperSignals? Signals { get; init; }
}
