using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;

namespace PayPalServerSdk.Models;

/// <summary>
/// Representation of card details as received in the request.
/// </summary>
public record CardFromRequest
{
    /// <summary>
    /// The year and month, in ISO-8601 <c>YYYY-MM</c> date format. See <see href="https://tools.ietf.org/html/rfc3339#section-5.6">Internet date and time format</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expiry")]
    [StringLength(7, MinimumLength = 7)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])$")]
    public string? Expiry { get; init; }

    /// <summary>
    /// The last digits of the payment card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("last_digits")]
    [StringLength(4, MinimumLength = 2)]
    [RegularExpression("[0-9]{2,}")]
    public string? LastDigits { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
