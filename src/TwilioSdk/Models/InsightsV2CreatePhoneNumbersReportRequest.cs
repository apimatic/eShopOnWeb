using System.Collections.Generic;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record InsightsV2CreatePhoneNumbersReportRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("time_range")]
    public TimeRange1? TimeRange { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("filters")]
    public IReadOnlyList<PhoneNumberReportFilter>? Filters { get; init; }

    /// <summary>
    /// The number of max available top Phone Numbers to generate.
    /// </summary>
    [JsonPropertyName("size")]
    [Minimum(1)]
    [Maximum(6000)]
    public int? Size { get; init; } = 1000;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
