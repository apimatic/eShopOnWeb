using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;

namespace PayPalServerSdk.Models;

/// <summary>
/// The portable international postal address. Maps to <see href="https://github.com/googlei18n/libaddressinput/wiki/AddressValidationMetadata">AddressValidationMetadata</see> and HTML 5.1 <see href="https://www.w3.org/TR/html51/sec-forms.html#autofilling-form-controls-the-autocomplete-attribute">Autofilling form controls: the autocomplete attribute</see>.
/// </summary>
public record Address
{
    /// <summary>
    /// The first line of the address, such as number and street, for example, <c>173 Drury Lane</c>. Needed for data entry, and Compliance and Risk checks. This field needs to pass the full address.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("address_line_1")]
    [MaxLength(300)]
    public string? AddressLine1 { get; init; }

    /// <summary>
    /// The second line of the address, for example, a suite or apartment number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("address_line_2")]
    [MaxLength(300)]
    public string? AddressLine2 { get; init; }

    /// <summary>
    /// A city, town, or village. Smaller than <c>admin_area_level_1</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("admin_area_2")]
    [MaxLength(120)]
    public string? AdminArea2 { get; init; }

    /// <summary>
    /// The highest-level sub-division in a country, which is usually a province, state, or ISO-3166-2 subdivision. This data is formatted for postal delivery, for example, <c>CA</c> and not <c>California</c>. Value, by country, is: UK. A county. US. A state. Canada. A province. Japan. A prefecture. Switzerland. A *kanton*.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("admin_area_1")]
    [MaxLength(300)]
    public string? AdminArea1 { get; init; }

    /// <summary>
    /// The postal code, which is the ZIP code or equivalent. Typically required for countries with a postal code or an equivalent. See <see href="https://en.wikipedia.org/wiki/Postal_code">postal code</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("postal_code")]
    [MaxLength(60)]
    public string? PostalCode { get; init; }

    /// <summary>
    /// The <see href="https://developer.paypal.com/api/rest/reference/country-codes/">2-character ISO 3166-1 code</see> that identifies the country or region. Note: The country code for Great Britain is GB and not UK as used in the top-level domain names for that country. Use the <c>C2</c> country code for China worldwide for comparable uncontrolled price (CUP) method, bank card, and cross-border transactions.
    /// </summary>
    [JsonPropertyName("country_code")]
    [StringLength(2, MinimumLength = 2)]
    [RegularExpression("^([A-Z]{2}|C2)$")]
    public required string CountryCode { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
