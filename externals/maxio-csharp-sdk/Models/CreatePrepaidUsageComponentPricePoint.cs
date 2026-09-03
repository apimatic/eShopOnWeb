using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record CreatePrepaidUsageComponentPricePoint
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("handle")]
    public string? Handle { get; init; }

    /// <summary>
    /// The identifier for the pricing scheme. See <see href="https://help.chargify.com/products/product-components.html">Product Components</see> for an overview of pricing schemes.
    /// </summary>
    [JsonPropertyName("pricing_scheme")]
    public required PricingScheme PricingScheme { get; init; }

    [JsonPropertyName("prices")]
    public required IReadOnlyList<Price> Prices { get; init; }

    [JsonPropertyName("overage_pricing")]
    public required OveragePricing OveragePricing { get; init; }

    /// <summary>
    /// Whether to use the site level exchange rate or define your own prices for each currency if you have multiple currencies defined on the site.
    /// </summary>
    [JsonPropertyName("use_site_exchange_rate")]
    public bool? UseSiteExchangeRate { get; init; } = true;

    /// <summary>
    /// (only for prepaid usage components) Boolean which controls whether or not remaining units should be rolled over to the next period.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("rollover_prepaid_remainder")]
    public bool? RolloverPrepaidRemainder { get; init; }

    /// <summary>
    /// (only for prepaid usage components) Boolean which controls whether or not the allocated quantity should be renewed at the beginning of each period.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("renew_prepaid_allocation")]
    public bool? RenewPrepaidAllocation { get; init; }

    /// <summary>
    /// (only for prepaid usage components where rollover_prepaid_remainder is true) The number of <c>expiration_interval_unit</c>s after which rollover amounts should expire.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expiration_interval")]
    public double? ExpirationInterval { get; init; }

    /// <summary>
    /// (only for prepaid usage components where rollover_prepaid_remainder is true) A string representing the expiration interval unit for this component, either month or day.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expiration_interval_unit")]
    public ExpirationIntervalUnit? ExpirationIntervalUnit { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
