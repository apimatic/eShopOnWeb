using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation;
using FirecrawlApi.Core.Validation.Attributes;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record EndpointFeedbackRequest
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

    [JsonPropertyName("endpoint")]
    public required Endpoint Endpoint { get; init; }

    [JsonPropertyName("jobId")]
    public required Guid JobId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("issues")]
    [MaxLength(20)]
    public IReadOnlyList<string>? Issues { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tags")]
    [MaxLength(20)]
    public IReadOnlyList<string>? Tags { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("note")]
    [MaxLength(4000)]
    public string? Note { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("pageNumbers")]
    [MaxLength(100)]
    public IReadOnlyList<int>? PageNumbers { get; init; }

    /// <summary>
    /// Small endpoint-specific metadata object. Must be 8KB or smaller; do not include full endpoint results.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("metadata")]
    public object? Metadata { get; init; }
}
