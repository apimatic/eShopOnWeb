using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;

namespace PayPal.Models;

/// <summary>
/// Bank Identification Number (BIN) details used to fund a payment.
/// </summary>
public record BinDetails
{
    /// <summary>
    /// The Bank Identification Number (BIN) signifies the number that is being used to identify the granular level details (except the PII information) of the card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bin")]
    [StringLength(25, MinimumLength = 1)]
    [RegularExpression("^[0-9]+$")]
    public string? Bin { get; init; }

    /// <summary>
    /// The issuer of the card instrument.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("issuing_bank")]
    [StringLength(64, MinimumLength = 1)]
    public string? IssuingBank { get; init; }

    /// <summary>
    /// The <see href="https://developer.paypal.com/api/rest/reference/country-codes/">two-character ISO 3166-1 code</see> that identifies the country or region. Note: The country code for Great Britain is GB and not UK as used in the top-level domain names for that country. Use the <c>C2</c> country code for China worldwide for comparable uncontrolled price (CUP) method, bank card, and cross-border transactions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bin_country_code")]
    [StringLength(2, MinimumLength = 2)]
    [RegularExpression("^([A-Z]{2}|C2)$")]
    public string? BinCountryCode { get; init; }

    /// <summary>
    /// The type of card product assigned to the BIN by the issuer. These values are defined by the issuer and may change over time. Some examples include: PREPAID_GIFT, CONSUMER, CORPORATE.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("products")]
    [MinLength(1)]
    [MaxLength(256)]
    public IReadOnlyList<string>? Products { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
