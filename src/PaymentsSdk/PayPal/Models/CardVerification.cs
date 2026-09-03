using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// The API caller can opt in to verify the card through PayPal offered verification services (e.g. Smart Dollar Auth, 3DS).
/// </summary>
public record CardVerification
{
    /// <summary>
    /// The method used for card verification.
    /// </summary>
    [JsonPropertyName("method")]
    public OrdersCardVerificationMethod? Method { get; init; } = OrdersCardVerificationMethod.ScaWhenRequired;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
