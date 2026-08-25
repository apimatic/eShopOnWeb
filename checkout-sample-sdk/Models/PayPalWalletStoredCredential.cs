using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// Provides additional details to process a payment using the PayPal wallet billing agreement or a vaulted payment method that has been stored or is intended to be stored.
/// </summary>
public record PayPalWalletStoredCredential
{
    /// <summary>
    /// The person or party who initiated or triggered the payment.
    /// </summary>
    [JsonPropertyName("payment_initiator")]
    public required PaymentInitiator PaymentInitiator { get; init; }

    /// <summary>
    /// DEPRECATED. Expected business/pricing model for the billing agreement, Please use usage_pattern instead.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("charge_pattern")]
    public UsagePattern? ChargePattern { get; init; }

    /// <summary>
    /// Expected business/pricing model for the billing agreement.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("usage_pattern")]
    public UsagePattern? UsagePattern { get; init; }

    /// <summary>
    /// Indicates if this is a <c>first</c> or <c>subsequent</c> payment using a stored payment source (also referred to as stored credential or card on file).
    /// </summary>
    [JsonPropertyName("usage")]
    public StoredPaymentSourceUsageType? Usage { get; init; } = StoredPaymentSourceUsageType.Derived;
}
