using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The payment card to use to fund a payment. Card can be a credit or debit card.
/// </summary>
public record CardResponse
{
    /// <summary>
    /// The card holder's name as it appears on the card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("name")]
    [StringLength(300, MinimumLength = 2)]
    public string? Name { get; init; }

    /// <summary>
    /// The last digits of the payment card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("last_digits")]
    [RegularExpression("[0-9]{2,}")]
    public string? LastDigits { get; init; }

    /// <summary>
    /// The card network or brand. Applies to credit, debit, gift, and payment cards.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("brand")]
    public CardBrand? Brand { get; init; }

    /// <summary>
    /// Array of brands or networks associated with the card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("available_networks")]
    [MinLength(1)]
    [MaxLength(256)]
    public IReadOnlyList<CardBrand>? AvailableNetworks { get; init; }

    /// <summary>
    /// Type of card. i.e Credit, Debit and so on.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("type")]
    public CardType? Type { get; init; }

    /// <summary>
    /// Results of Authentication such as 3D Secure.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("authentication_result")]
    public AuthenticationResponse? AuthenticationResult { get; init; }

    /// <summary>
    /// Additional attributes associated with the use of this card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("attributes")]
    public CardAttributesResponse? Attributes { get; init; }

    /// <summary>
    /// Representation of card details as received in the request.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("from_request")]
    public CardFromRequest? FromRequest { get; init; }

    /// <summary>
    /// The year and month, in ISO-8601 <c>YYYY-MM</c> date format. See <see href="https://tools.ietf.org/html/rfc3339#section-5.6">Internet date and time format</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expiry")]
    [StringLength(7, MinimumLength = 7)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])$")]
    public string? Expiry { get; init; }

    /// <summary>
    /// Bank Identification Number (BIN) details used to fund a payment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bin_details")]
    public BinDetails? BinDetails { get; init; }

    /// <summary>
    /// Provides additional details to process a payment using a <c>card</c> that has been stored or is intended to be stored (also referred to as stored_credential or card-on-file). Parameter compatibility: <c>payment_type=ONE_TIME</c> is compatible only with <c>payment_initiator=CUSTOMER</c>. <c>usage=FIRST</c> is compatible only with <c>payment_initiator=CUSTOMER</c>. <c>previous_transaction_reference</c> or <c>previous_network_transaction_reference</c> is compatible only with <c>payment_initiator=MERCHANT</c>. Only one of the parameters - <c>previous_transaction_reference</c> and <c>previous_network_transaction_reference</c> - can be present in the request.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("stored_credential")]
    public CardStoredCredential? StoredCredential { get; init; }
}
