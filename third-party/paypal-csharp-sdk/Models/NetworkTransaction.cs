using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// Reference values used by the card network to identify a transaction.
/// </summary>
public record NetworkTransaction
{
    /// <summary>
    /// Transaction reference id returned by the scheme. For Visa and Amex, this is the "Tran id" field in response. For MasterCard, this is the "BankNet reference id" field in response. For Discover, this is the "NRID" field in response. The pattern we expect for this field from Visa/Amex/CB/Discover is numeric, Mastercard/BNPP is alphanumeric and Paysecure is alphanumeric with special character -.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    [StringLength(36, MinimumLength = 9)]
    [RegularExpression("^[a-zA-Z0-9-_@.:&+=*^'~#!$%()]+$")]
    public string? Id { get; init; }

    /// <summary>
    /// The date that the transaction was authorized by the scheme. This field may not be returned for all networks. MasterCard refers to this field as "BankNet reference date". For some specific networks, such as MasterCard and Discover, this date field is mandatory when the <c>previous_network_transaction_reference_id</c> is passed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date")]
    [StringLength(4, MinimumLength = 4)]
    [RegularExpression("^[0-9]+$")]
    public string? Date { get; init; }

    /// <summary>
    /// The card network or brand. Applies to credit, debit, gift, and payment cards.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("network")]
    public CardBrand? Network { get; init; }

    /// <summary>
    /// Reference ID issued for the card transaction. This ID can be used to track the transaction across processors, card brands and issuing banks.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("acquirer_reference_number")]
    [StringLength(36, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9]+$")]
    public string? AcquirerReferenceNumber { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
