using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// A simple postal address with coarse-grained fields. Do not use for an international address. Use for backward compatibility only. Does not contain phone.
/// </summary>
public record SimplePostalAddressCoarseGrained
{
    /// <summary>
    /// The first line of the address. For example, number or street.
    /// </summary>
    [JsonPropertyName("line1")]
    public required string Line1 { get; init; }

    /// <summary>
    /// The second line of the address. For example, suite or apartment number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("line2")]
    public string? Line2 { get; init; }

    /// <summary>
    /// The city name.
    /// </summary>
    [JsonPropertyName("city")]
    public required string City { get; init; }

    /// <summary>
    /// The <see href="/docs/api/reference/state-codes/">code</see> for a US state or the equivalent for other countries. Required for transactions if the address is in one of these countries: <see href="/docs/api/reference/state-codes/#argentina">Argentina</see>, <see href="/docs/api/reference/state-codes/#brazil">Brazil</see>, <see href="/docs/api/reference/state-codes/#canada">Canada</see>, <see href="/docs/api/reference/state-codes/#china">China</see>, <see href="/docs/api/reference/state-codes/#india">India</see>, <see href="/docs/api/reference/state-codes/#italy">Italy</see>, <see href="/docs/api/reference/state-codes/#japan">Japan</see>, <see href="/docs/api/reference/state-codes/#mexico">Mexico</see>, <see href="/docs/api/reference/state-codes/#thailand">Thailand</see>, or <see href="/docs/api/reference/state-codes/#usa">United States</see>. Maximum length is 40 single-byte characters.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("state")]
    public string? State { get; init; }

    /// <summary>
    /// The <see href="/docs/integration/direct/rest/country-codes/">two-character ISO 3166-1 code</see> that identifies the country or region. Note: The country code for Great Britain is GB and not UK as used in the top-level domain names for that country. Use the <c>C2</c> country code for China worldwide for comparable uncontrolled price (CUP) method, bank card, and cross-border transactions.
    /// </summary>
    [JsonPropertyName("country_code")]
    [StringLength(2, MinimumLength = 2)]
    [RegularExpression("^([A-Z]{2}|C2)$")]
    public required string CountryCode { get; init; }

    /// <summary>
    /// The postal code, which is the zip code or equivalent. Typically required for countries with a postal code or an equivalent. See <see href="https://en.wikipedia.org/wiki/Postal_code">postal code</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; init; }
}
