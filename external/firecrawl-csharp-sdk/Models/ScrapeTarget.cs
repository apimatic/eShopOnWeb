using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record ScrapeTarget
{
    /// <summary>
    /// Optional stable ID for this target. Generated if omitted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    public Guid? Id { get; init; }

    [JsonPropertyName("type")]
    public required TypeEnum Type { get; init; }

    [JsonPropertyName("urls")]
    [MinLength(1)]
    public required IReadOnlyList<string> Urls { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("scrapeOptions")]
    public ScrapeOptions? ScrapeOptions { get; init; }
}
