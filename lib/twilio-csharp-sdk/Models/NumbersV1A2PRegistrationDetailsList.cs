using System.Collections.Generic;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record NumbersV1A2PRegistrationDetailsList
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<NumbersV1A2PRegistrationDetails> Data { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
