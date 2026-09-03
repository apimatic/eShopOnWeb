using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Core.Validation;
using PayPal.Core.Validation.Attributes;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// The application context, which customizes the payer experience during the subscription approval process with PayPal.
/// </summary>
public record SubscriptionPatchApplicationContext
{
    /// <summary>
    /// The label that overrides the business name in the PayPal account on the PayPal site.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("brand_name")]
    [StringLength(127, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public string? BrandName { get; init; }

    /// <summary>
    /// The BCP 47-formatted locale of pages that the PayPal payment experience shows. PayPal supports a five-character code. For example, <c>da-DK</c>, <c>he-IL</c>, <c>id-ID</c>, <c>ja-JP</c>, <c>no-NO</c>, <c>pt-BR</c>, <c>ru-RU</c>, <c>sv-SE</c>, <c>th-TH</c>, <c>zh-CN</c>, <c>zh-HK</c>, or <c>zh-TW</c>.
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
    /// The customer and merchant payment preferences.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_method")]
    public PaymentMethod? PaymentMethod { get; init; }

    /// <summary>
    /// The URL where the customer is redirected after the customer approves the payment.
    /// </summary>
    [JsonPropertyName("return_url")]
    [StringLength(4000, MinimumLength = 10)]
    [Format(FormatKind.Uri)]
    public required string ReturnUrl { get; init; }

    /// <summary>
    /// The URL where the customer is redirected after the customer cancels the payment.
    /// </summary>
    [JsonPropertyName("cancel_url")]
    [StringLength(4000, MinimumLength = 10)]
    [Format(FormatKind.Uri)]
    public required string CancelUrl { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
