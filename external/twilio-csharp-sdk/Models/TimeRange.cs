using System;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

/// <summary>
/// Optional start and end date time for the report window. Defaults to the most recent 7 days when omitted.
/// </summary>
public record TimeRange
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
