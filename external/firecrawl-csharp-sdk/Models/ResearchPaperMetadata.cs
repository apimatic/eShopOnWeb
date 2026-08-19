using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record ResearchPaperMetadata
{
    /// <summary>
    /// Canonical paper id.
    /// </summary>
    [JsonPropertyName("paperId")]
    public required string PaperId { get; init; }

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

    /// <summary>
    /// Comma-joined author names.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("authors")]
    public string? Authors { get; init; }

    /// <summary>
    /// Paper categories.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("categories")]
    public IReadOnlyList<string>? Categories { get; init; }

    /// <summary>
    /// Original creation date string.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("createdDate")]
    public string? CreatedDate { get; init; }

    /// <summary>
    /// Last-updated date string.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("updateDate")]
    public string? UpdateDate { get; init; }
}
