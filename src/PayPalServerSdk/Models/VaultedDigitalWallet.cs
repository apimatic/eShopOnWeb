using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// Resource consolidating common request and response attributes for vaulting a Digital Wallet.
/// </summary>
public record VaultedDigitalWallet
{
    /// <summary>
    /// The description displayed to the consumer on the approval flow for a digital wallet, as well as on the merchant view of the payment token management experience. exp: PayPal.com.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    [StringLength(128, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public string? Description { get; init; }

    /// <summary>
    /// Expected business/charge model for the billing agreement.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("usage_pattern")]
    public UsagePattern? UsagePattern { get; init; }

    /// <summary>
    /// The shipping details.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("shipping")]
    public VaultedDigitalWalletShippingDetails? Shipping { get; init; }

    /// <summary>
    /// Create multiple payment tokens for the same payer, merchant/platform combination. Use this when the customer has not logged in at merchant/platform. The payment token thus generated, can then also be used to create the customer account at merchant/platform. Use this also when multiple payment tokens are required for the same payer, different customer at merchant/platform. This helps to identify customers distinctly even though they may share the same PayPal account. This only applies to PayPal payment source.
    /// </summary>
    [JsonPropertyName("permit_multiple_payment_tokens")]
    public bool? PermitMultiplePaymentTokens { get; init; } = false;

    /// <summary>
    /// The usage type associated with a digital wallet payment token.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("usage_type")]
    public PayPalPaymentTokenUsageType? UsageType { get; init; }

    /// <summary>
    /// The customer type associated with a digital wallet payment token. This is to indicate whether the customer acting on the merchant / platform is either a business or a consumer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer_type")]
    public PayPalPaymentTokenCustomerType? CustomerType { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
