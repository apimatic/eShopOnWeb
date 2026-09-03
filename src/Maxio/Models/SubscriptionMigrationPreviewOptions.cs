using System;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record SubscriptionMigrationPreviewOptions
{
    /// <summary>
    /// The ID of the target Product. Either a product_id or product_handle must be present. A Subscription can be migrated to another product for both the current Product Family and another Product Family. Note: Going to another Product Family, components will not be migrated as well.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_id")]
    public int? ProductId { get; init; }

    /// <summary>
    /// The ID of the specified product's price point. This can be passed to migrate to a non-default price point.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_price_point_id")]
    public int? ProductPricePointId { get; init; }

    /// <summary>
    /// Whether to include the trial period configured for the product price point when starting a new billing period. Note that if preserve_period is set, then include_trial will be ignored.
    /// </summary>
    [JsonPropertyName("include_trial")]
    public bool? IncludeTrial { get; init; } = false;

    /// <summary>
    /// If <c>true</c> is sent initial charges will be assessed.
    /// </summary>
    [JsonPropertyName("include_initial_charge")]
    public bool? IncludeInitialCharge { get; init; } = false;

    /// <summary>
    /// If <c>true</c> is sent, any coupons associated with the subscription will be applied to the migration. If <c>false</c> is sent, coupons will not be applied. Note: When migrating to a new product family, the coupon cannot migrate.
    /// </summary>
    [JsonPropertyName("include_coupons")]
    public bool? IncludeCoupons { get; init; } = true;

    /// <summary>
    /// If <c>false</c> is sent, the subscription's billing period will be reset to today and the full price of the new product will be charged. If <c>true</c> is sent, the billing period will not change and a prorated charge will be issued for the new product.
    /// </summary>
    [JsonPropertyName("preserve_period")]
    public bool? PreservePeriod { get; init; } = false;

    /// <summary>
    /// The handle of the target Product. Either a product_id or product_handle must be present. A Subscription can be migrated to another product for both the current Product Family and another Product Family. Note: Going to another Product Family, components will not be migrated as well.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; init; }

    /// <summary>
    /// The ID or handle of the specified product's price point. This can be passed to migrate to a non-default price point.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_price_point_handle")]
    public string? ProductPricePointHandle { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("proration")]
    public Proration? Proration { get; init; }

    /// <summary>
    /// The date that the proration is calculated from for the preview
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("proration_date")]
    public DateTimeOffset? ProrationDate { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
