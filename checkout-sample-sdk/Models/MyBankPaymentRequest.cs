using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// Information needed to pay using MyBank.
/// </summary>
public record MyBankPaymentRequest
{
    /// <summary>
    /// The full name representation like Mr J Smith.
    /// </summary>
    [JsonPropertyName("name")]
    [StringLength(300, MinimumLength = 3)]
    public required string Name { get; init; }

    /// <summary>
    /// The <see href="/api/rest/reference/country-codes/">two-character ISO 3166-1 code</see> that identifies the country or region. Note: The country code for Great Britain is GB and not UK as used in the top-level domain names for that country. Use the <c>C2</c> country code for China worldwide for comparable uncontrolled price (CUP) method, bank card, and cross-border transactions.
    /// </summary>
    [JsonPropertyName("country_code")]
    [StringLength(2, MinimumLength = 2)]
    [RegularExpression("^([A-Z]{2}|C2)$")]
    public required string CountryCode { get; init; }

    /// <summary>
    /// Customizes the payer experience during the approval process for the payment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("experience_context")]
    public ExperienceContext? ExperienceContext { get; init; }
}
