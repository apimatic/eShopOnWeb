using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// Details shared by Google for the merchant to be shared with PayPal. This is required to process the transaction using the Google Pay payment method.
/// </summary>
public record GooglePayDecryptedTokenData
{
    /// <summary>
    /// A unique ID that identifies the message in case it needs to be revoked or located at a later time.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("message_id")]
    [StringLength(250, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public string? MessageId { get; init; }

    /// <summary>
    /// Date and time at which the message expires as UTC milliseconds since epoch. Integrators should reject any message that's expired.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("message_expiration")]
    [StringLength(13, MinimumLength = 13)]
    [RegularExpression("\\d{13}")]
    public string? MessageExpiration { get; init; }

    /// <summary>
    /// The type of the payment credential. Currently, only CARD is supported.
    /// </summary>
    [JsonPropertyName("payment_method")]
    public required GooglePayPaymentMethod PaymentMethod { get; init; }

    /// <summary>
    /// The payment card used to fund a Google Pay payment. Can be a credit or debit card.
    /// </summary>
    [JsonPropertyName("card")]
    public required GooglePayCard Card { get; init; }

    /// <summary>
    /// Authentication Method which is used for the card transaction.
    /// </summary>
    [JsonPropertyName("authentication_method")]
    public required GooglePayAuthenticationMethod AuthenticationMethod { get; init; }

    /// <summary>
    /// Base-64 cryptographic identifier used by card schemes to validate the token verification result. This is a conditionally required field if authentication_method is CRYPTOGRAM_3DS.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cryptogram")]
    [StringLength(2000, MinimumLength = 1)]
    public string? Cryptogram { get; init; }

    /// <summary>
    /// Electronic Commerce Indicator may not always be present. It is only returned for tokens on the Visa card network. This value is passed through in the payment authorization request.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("eci_indicator")]
    [StringLength(256, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public string? EciIndicator { get; init; }
}
