using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Core.Validation;
using PayPal.Core.Validation.Attributes;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// A resource representing an experience context of vault a card.
/// </summary>
public record VaultCardExperienceContext
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
    /// The URL where the customer is redirected after customer approves leaves the flow. It is a required field for contingency flows like PayPal wallet, 3DS.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("return_url")]
    [StringLength(4000, MinimumLength = 1)]
    [Format(FormatKind.Uri)]
    public string? ReturnUrl { get; init; }

    /// <summary>
    /// The URL where the customer is redirected after customer cancels or leaves the flow. It is a required field for contingency flows like PayPal wallet, 3DS.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cancel_url")]
    [StringLength(4000, MinimumLength = 1)]
    [Format(FormatKind.Uri)]
    public string? CancelUrl { get; init; }

    /// <summary>
    /// DEPRECATED. Vault Instruction on action to be performed after a successful payer approval.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("vault_instruction")]
    public VaultInstructionAction? VaultInstruction { get; init; }

    /// <summary>
    /// User Action on action to be performed after a successful payer approval.
    /// </summary>
    [JsonPropertyName("user_action")]
    public VaultUserAction? UserAction { get; init; } = VaultUserAction.Continue;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
