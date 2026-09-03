using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// Resource consolidating common request and response attirbutes for vaulting Venmo Wallet.
/// </summary>
public record VenmoWalletVaultAttributes
{
    /// <summary>
    /// Defines how and when the payment source gets vaulted.
    /// </summary>
    [JsonPropertyName("store_in_vault")]
    public required StoreInVaultInstruction StoreInVault { get; init; }

    /// <summary>
    /// The description displayed to Venmo consumer on the approval flow for Venmo, as well as on the Venmo payment token management experience on Venmo.com.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    [StringLength(128, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9_'\\-., :;\\!?\"]*$")]
    public string? Description { get; init; }

    /// <summary>
    /// Expected business/pricing model for the billing agreement.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("usage_pattern")]
    public VenmoPaymentTokenUsagePattern? UsagePattern { get; init; }

    /// <summary>
    /// The usage type associated with the Venmo payment token.
    /// </summary>
    [JsonPropertyName("usage_type")]
    public required VenmoPaymentTokenUsageType UsageType { get; init; }

    /// <summary>
    /// The customer type associated with the Venmo payment token. This is to indicate whether the customer acting on the merchant / platform is either a business or a consumer.
    /// </summary>
    [JsonPropertyName("customer_type")]
    public VenmoPaymentTokenCustomerType? CustomerType { get; init; } = VenmoPaymentTokenCustomerType.Consumer;

    /// <summary>
    /// Create multiple payment tokens for the same payer, merchant/platform combination. Use this when the customer has not logged in at merchant/platform. The payment token thus generated, can then also be used to create the customer account at merchant/platform. Use this also when multiple payment tokens are required for the same payer, different customer at merchant/platform. This helps to identify customers distinctly even though they may share the same Venmo account.
    /// </summary>
    [JsonPropertyName("permit_multiple_payment_tokens")]
    public bool? PermitMultiplePaymentTokens { get; init; } = false;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
