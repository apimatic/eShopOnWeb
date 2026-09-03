using System;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record TimeRange1
{
    /// <summary>
    /// Start date time of the report
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("start_datetime")]
    public DateTimeOffset? StartDatetime { get; init; }

    /// <summary>
    /// End date time of the report
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("end_datetime")]
    public DateTimeOffset? EndDatetime { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
