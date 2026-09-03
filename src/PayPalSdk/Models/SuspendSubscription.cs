using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;

namespace PayPal.Models;

/// <summary>
/// The suspend subscription request details.
/// </summary>
public record SuspendSubscription
{
    /// <summary>
    /// The reason for suspension of the Subscription.
    /// </summary>
    [JsonPropertyName("reason")]
    [StringLength(128, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public required string Reason { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
