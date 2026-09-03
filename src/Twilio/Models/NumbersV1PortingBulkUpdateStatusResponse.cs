using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record NumbersV1PortingBulkUpdateStatusResponse
{
    [JsonPropertyName("successful_updates")]
    [MinLength(0)]
    public required IReadOnlyList<NumbersV1PortingBulkPhoneNumberUpdateDetail> SuccessfulUpdates { get; init; }

    [JsonPropertyName("failed_updates")]
    [MinLength(0)]
    public required IReadOnlyList<NumbersV1PortingBulkPhoneNumberUpdateDetail> FailedUpdates { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
