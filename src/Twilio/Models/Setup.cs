using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record Setup
{
    [JsonPropertyName("charges_apply")]
    public required bool ChargesApply { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
