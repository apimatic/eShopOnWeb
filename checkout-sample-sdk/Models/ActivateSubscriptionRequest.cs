using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The activate subscription request details.
/// </summary>
public record ActivateSubscriptionRequest
{
    /// <summary>
    /// The reason for activation of a subscription. Required to reactivate the subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reason")]
    [StringLength(128, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public string? Reason { get; init; }
}
