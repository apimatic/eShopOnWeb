using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// The customer and merchant payment preferences.
/// </summary>
public record PaymentMethod
{
    /// <summary>
    /// The merchant-preferred payment methods.
    /// </summary>
    [JsonPropertyName("payee_preferred")]
    public PayeePaymentMethodPreference? PayeePreferred { get; init; } = PayeePaymentMethodPreference.Unrestricted;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
