using System;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record Period1
{
    /// <summary>
    /// Start date of the billing period
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("startDate")]
    public DateTimeOffset? StartDate { get; init; }

    /// <summary>
    /// End date of the billing period
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("endDate")]
    public DateTimeOffset? EndDate { get; init; }

    /// <summary>
    /// Name of the API key used for the billing period. null if byApiKey is false (default)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; init; }

    /// <summary>
    /// Total number of tokens used in the billing period
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("totalTokens")]
    public int? TotalTokens { get; init; }
}
