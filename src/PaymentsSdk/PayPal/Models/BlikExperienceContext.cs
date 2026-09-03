using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Core.Validation;
using PayPal.Core.Validation.Attributes;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// Customizes the payer experience during the approval process for the BLIK payment.
/// </summary>
public record BlikExperienceContext
{
    /// <summary>
    /// The label that overrides the business name in the PayPal account on the PayPal site. The pattern is defined by an external party and supports Unicode.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("brand_name")]
    [StringLength(127, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public string? BrandName { get; init; }

    /// <summary>
    /// The <see href="https://tools.ietf.org/html/bcp47#section-2">language tag</see> for the language in which to localize the error-related strings, such as messages, issues, and suggested actions. The tag is made up of the <see href="https://www.loc.gov/standards/iso639-2/php/code_list.php">ISO 639-2 language code</see>, the optional <see href="https://www.unicode.org/iso15924/codelists.html">ISO-15924 script tag</see>, and the <see href="https://developer.paypal.com/api/rest/reference/country-codes/">ISO-3166 alpha-2 country code</see> or <see href="https://unstats.un.org/unsd/methodology/m49/">M49 region code</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("locale")]
    [StringLength(10, MinimumLength = 2)]
    [RegularExpression("^[a-z]{2}(?:-[A-Z][a-z]{3})?(?:-(?:[A-Z]{2}|[0-9]{3}))?$")]
    public string? Locale { get; init; }

    /// <summary>
    /// The location from which the shipping address is derived.
    /// </summary>
    [JsonPropertyName("shipping_preference")]
    public ExperienceContextShippingPreference? ShippingPreference { get; init; } = ExperienceContextShippingPreference.GetFromFile;

    /// <summary>
    /// Describes the URL.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("return_url")]
    [Format(FormatKind.Uri)]
    public string? ReturnUrl { get; init; }

    /// <summary>
    /// Describes the URL.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cancel_url")]
    [Format(FormatKind.Uri)]
    public string? CancelUrl { get; init; }

    /// <summary>
    /// An Internet Protocol address (IP address). This address assigns a numerical label to each device that is connected to a computer network through the Internet Protocol. Supports IPv4 and IPv6 addresses.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("consumer_ip")]
    [StringLength(39, MinimumLength = 7)]
    [RegularExpression("^(([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])\\.){3}([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])$|^(([a-zA-Z]|[a-zA-Z][a-zA-Z0-9\\-]*[a-zA-Z0-9])\\.)*([A-Za-z]|[A-Za-z][A-Za-z0-9\\-]*[A-Za-z0-9])$|^\\s*((([0-9A-Fa-f]{1,4}:){7}([0-9A-Fa-f]{1,4}|:))|(([0-9A-Fa-f]{1,4}:){6}(:[0-9A-Fa-f]{1,4}|((25[0-5]|2[0-4]\\d|1\\d\\d|[1-9]?\\d)(\\.(25[0-5]|2[0-4]\\d|1\\d\\d|[1-9]?\\d)){3})|:))|(([0-9A-Fa-f]{1,4}:){5}(((:[0-9A-Fa-f]{1,4}){1,2})|:((25[0-5]|2[0-4]\\d|1\\d\\d|[1-9]?\\d)(\\.(25[0-5]|2[0-4]\\d|1\\d\\d|[1-9]?\\d)){3})|:))|(([0-9A-Fa-f]{1,4}:){4}(((:[0-9A-Fa-f]{1,4}){1,3})|((:[0-9A-Fa-f]{1,4})?:((25[0-5]|2[0-4]\\d|1\\d\\d|[1-9]?\\d)(\\.(25[0-5]|2[0-4]\\d|1\\d\\d|[1-9]?\\d)){3}))|:))|(([0-9A-Fa-f]{1,4}:){3}(((:[0-9A-Fa-f]{1,4}){1,4})|((:[0-9A-Fa-f]{1,4}){0,2}:((25[0-5]|2[0-4]\\d|1\\d\\d|[1-9]?\\d)(\\.(25[0-5]|2[0-4]\\d|1\\d\\d|[1-9]?\\d)){3}))|:))|(([0-9A-Fa-f]{1,4}:){2}(((:[0-9A-Fa-f]{1,4}){1,5})|((:[0-9A-Fa-f]{1,4}){0,3}:((25[0-5]|2[0-4]\\d|1\\d\\d|[1-9]?\\d)(\\.(25[0-5]|2[0-4]\\d|1\\d\\d|[1-9]?\\d)){3}))|:))|(([0-9A-Fa-f]{1,4}:){1}(((:[0-9A-Fa-f]{1,4}){1,6})|((:[0-9A-Fa-f]{1,4}){0,4}:((25[0-5]|2[0-4]\\d|1\\d\\d|[1-9]?\\d)(\\.(25[0-5]|2[0-4]\\d|1\\d\\d|[1-9]?\\d)){3}))|:))|(:(((:[0-9A-Fa-f]{1,4}){1,7})|((:[0-9A-Fa-f]{1,4}){0,5}:((25[0-5]|2[0-4]\\d|1\\d\\d|[1-9]?\\d)(\\.(25[0-5]|2[0-4]\\d|1\\d\\d|[1-9]?\\d)){3}))|:)))(%.+)?\\s*$")]
    public string? ConsumerIp { get; init; }

    /// <summary>
    /// The payer's User Agent. For example, Mozilla/5.0 (Macintosh; Intel Mac OS X x.y; rv:42.0).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("consumer_user_agent")]
    [StringLength(256, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public string? ConsumerUserAgent { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
