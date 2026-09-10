using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

public record PayPalWalletVaultInstruction
{
    /// <summary>
    /// Defines how and when the payment source gets vaulted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("store_in_vault")]
    public StoreInVaultInstruction? StoreInVault { get; init; }

    /// <summary>
    /// The description displayed to PayPal consumer on the approval flow for PayPal, as well as on the PayPal payment token management experience on PayPal.com.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    [StringLength(128, MinimumLength = 1)]
    public string? Description { get; init; }

    /// <summary>
    /// Expected business/pricing model for the billing agreement.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("usage_pattern")]
    public UsagePattern? UsagePattern { get; init; }

    /// <summary>
    /// The usage type associated with the PayPal payment token.
    /// </summary>
    [JsonPropertyName("usage_type")]
    public required PayPalPaymentTokenUsageType UsageType { get; init; }

    /// <summary>
    /// The customer type associated with the PayPal payment token. This is to indicate whether the customer acting on the merchant / platform is either a business or a consumer.
    /// </summary>
    [JsonPropertyName("customer_type")]
    public PayPalPaymentTokenCustomerType? CustomerType { get; init; } = PayPalPaymentTokenCustomerType.Consumer;

    /// <summary>
    /// Create multiple payment tokens for the same payer, merchant/platform combination. Use this when the customer has not logged in at merchant/platform. The payment token thus generated, can then also be used to create the customer account at merchant/platform. Use this also when multiple payment tokens are required for the same payer, different customer at merchant/platform. This helps to identify customers distinctly even though they may share the same PayPal account. This only applies to PayPal payment source.
    /// </summary>
    [JsonPropertyName("permit_multiple_payment_tokens")]
    public bool? PermitMultiplePaymentTokens { get; init; } = false;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
