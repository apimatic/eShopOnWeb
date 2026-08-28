using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// Information about the Payment data obtained by decrypting Apple Pay token.
/// </summary>
public record ApplePayDecryptedTokenData
{
    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("transaction_amount")]
    public Money? TransactionAmount { get; init; }

    /// <summary>
    /// The payment card to use to fund a payment. Can be a credit or debit card.
    /// </summary>
    [JsonPropertyName("tokenized_card")]
    public required ApplePayTokenizedCard TokenizedCard { get; init; }

    /// <summary>
    /// Apple Pay Hex-encoded device manufacturer identifier. The pattern is defined by an external party and supports Unicode.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("device_manufacturer_id")]
    [StringLength(2000, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public string? DeviceManufacturerId { get; init; }

    /// <summary>
    /// Indicates the type of payment data passed, in case of Non China the payment data is 3DSECURE and for China it is EMV.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_data_type")]
    public ApplePayPaymentDataType? PaymentDataType { get; init; }

    /// <summary>
    /// Information about the decrypted apple pay payment data for the token like cryptogram, eci indicator.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_data")]
    public ApplePayPaymentData? PaymentData { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
