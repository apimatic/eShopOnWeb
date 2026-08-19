using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

/// <summary>
/// For 'good', include valuableSources. For 'partial', include valuableSources or missingContent. For 'bad', include missingContent or querySuggestions.
/// </summary>
public record SearchFeedbackRequest
{
    [JsonPropertyName("rating")]
    public required Rating Rating { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("valuableSources")]
    [MaxLength(50)]
    public IReadOnlyList<ValuableSource>? ValuableSources { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("missingContent")]
    [MaxLength(20)]
    public IReadOnlyList<MissingContent>? MissingContent { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("querySuggestions")]
    [MaxLength(2000)]
    public string? QuerySuggestions { get; init; }

    [JsonPropertyName("origin")]
    public string? Origin { get; init; } = "api";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("integration")]
    public string? Integration { get; init; }
}
