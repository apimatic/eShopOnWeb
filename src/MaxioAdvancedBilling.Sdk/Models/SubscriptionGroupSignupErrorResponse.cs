using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record SubscriptionGroupSignupErrorResponse
{
    [JsonPropertyName("errors")]
    public required SubscriptionGroupSignupError Errors { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
