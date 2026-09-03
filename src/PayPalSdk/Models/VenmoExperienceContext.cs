using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// A resource representing an experience context of vault a venmo account.
/// </summary>
public record VenmoExperienceContext
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
    /// The shipping preference. This only applies to PayPal payment source.
    /// </summary>
    [JsonPropertyName("shipping_preference")]
    public ExperienceContextShippingPreference? ShippingPreference { get; init; } = ExperienceContextShippingPreference.GetFromFile;

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
