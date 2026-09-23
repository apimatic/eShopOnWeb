using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;

namespace PayPalServerSdk.Models;

/// <summary>
/// The cancel subscription request details.
/// </summary>
public record CancelSubscriptionRequest
{
    /// <summary>
    /// The reason for the cancellation of a subscription.
    /// </summary>
    [JsonPropertyName("reason")]
    [StringLength(128, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public required string Reason { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
