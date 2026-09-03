using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record SubscriptionGroupSignupItem
{
    /// <summary>
    /// The API Handle of the product for which you are creating a subscription. Required, unless a <c>product_id</c> is given instead.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; init; }

    /// <summary>
    /// The Product ID of the product for which you are creating a subscription. You can pass either <c>product_id</c> or <c>product_handle</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_id")]
    public int? ProductId { get; init; }

    /// <summary>
    /// The ID of the particular price point on the product.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_price_point_id")]
    public int? ProductPricePointId { get; init; }

    /// <summary>
    /// The user-friendly API handle of a product's particular price point.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_price_point_handle")]
    public string? ProductPricePointHandle { get; init; }

    /// <summary>
    /// Use in place of passing product and component information to set up the subscription with an existing offer. May be either the Chargify ID of the offer or its handle prefixed with <c>handle:</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("offer_id")]
    public int? OfferId { get; init; }

    /// <summary>
    /// The reference value (provided by your app) for the subscription itself.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reference")]
    public string? Reference { get; init; }

    /// <summary>
    /// One of the subscriptions must be marked as primary in the group.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("primary")]
    public bool? Primary { get; init; }

    /// <summary>
    /// (Optional) If Multi-Currency is enabled and the currency is configured in Chargify, pass it at signup to create a subscription on a non-default currency. Note that you cannot update the currency of an existing subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    /// <summary>
    /// An array for all the coupons attached to the subscription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("coupon_codes")]
    public IReadOnlyList<string>? CouponCodes { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("components")]
    public IReadOnlyList<SubscriptionGroupSignupComponent>? Components { get; init; }

    /// <summary>
    /// (Optional) Used in place of <c>product_price_point_id</c> to define a custom price point unique to the subscription. A subscription can have up to 30 custom price points. Exceeding this limit will result in an API error.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("custom_price")]
    public SubscriptionCustomPrice? CustomPrice { get; init; }

    /// <summary>
    /// (Optional). Cannot be used when also specifying next_billing_at.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("calendar_billing")]
    public CalendarBilling? CalendarBilling { get; init; }

    /// <summary>
    /// (Optional) A set of key/value pairs representing custom fields and their values. Metafields will be created “on-the-fly” in your site for a given key, if they have not been created yet.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("metafields")]
    public IReadOnlyDictionary<string, string>? Metafields { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
