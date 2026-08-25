using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The Third Party Network token used to fund a payment.
/// </summary>
public record NetworkToken
{
    /// <summary>
    /// Third party network token number.
    /// </summary>
    [JsonPropertyName("number")]
    [StringLength(19, MinimumLength = 13)]
    [RegularExpression("^[0-9]{13,19}$")]
    public required string Number { get; init; }

    /// <summary>
    /// The year and month, in ISO-8601 <c>YYYY-MM</c> date format. See <see href="https://tools.ietf.org/html/rfc3339#section-5.6">Internet date and time format</see>.
    /// </summary>
    [JsonPropertyName("expiry")]
    [StringLength(7, MinimumLength = 7)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])$")]
    public required string Expiry { get; init; }

    /// <summary>
    /// An Encrypted one-time use value that's sent along with Network Token. This field is not required to be present for recurring transactions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cryptogram")]
    [StringLength(32, MinimumLength = 28)]
    [RegularExpression("^.*$")]
    public string? Cryptogram { get; init; }

    /// <summary>
    /// Electronic Commerce Indicator (ECI). The ECI value is part of the 2 data elements that indicate the transaction was processed electronically. This should be passed on the authorization transaction to the Gateway/Processor.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("eci_flag")]
    public EciFlag? EciFlag { get; init; }

    /// <summary>
    /// A TRID, or a Token Requestor ID, is an identifier used by merchants to request network tokens from card networks. A TRID is a precursor to obtaining a network token for a credit card primary account number (PAN), and will aid in enabling secure card on file (COF) payments and reducing fraud.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("token_requestor_id")]
    [StringLength(11, MinimumLength = 1)]
    [RegularExpression("^[0-9A-Z_]+$")]
    public string? TokenRequestorId { get; init; }
}
