using System;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record Period
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
    /// Total number of credits used in the billing period
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("totalCredits")]
    public int? TotalCredits { get; init; }
}
