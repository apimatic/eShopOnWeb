using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;

namespace Twilio.Models;

public record VerifyV2VerificationAttemptsSummary
{
    /// <summary>
    /// Total of attempts made according to the provided filters
    /// </summary>
    [JsonPropertyName("total_attempts")]
    public int? TotalAttempts { get; init; } = 0;

    /// <summary>
    /// Total of  attempts made that were confirmed by the end user, according to the provided filters.
    /// </summary>
    [JsonPropertyName("total_converted")]
    public int? TotalConverted { get; init; } = 0;

    /// <summary>
    /// Total of attempts made that were not confirmed by the end user, according to the provided filters.
    /// </summary>
    [JsonPropertyName("total_unconverted")]
    public int? TotalUnconverted { get; init; } = 0;

    /// <summary>
    /// Percentage of the confirmed messages over the total, defined by (total_converted/total_attempts)*100.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("conversion_rate_percentage")]
    public string? ConversionRatePercentage { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
