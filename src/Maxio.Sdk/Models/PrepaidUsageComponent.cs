using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.AnyOf;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record PrepaidUsageComponent
{
    /// <summary>
    /// A name for this component that is suitable for showing customers and displaying on billing statements, e.g., "Minutes".
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// The name of the unit of measurement for the component. It should be singular since it will be automatically pluralized when necessary. e.g., “message”, which may then be shown as “5 messages” on a subscription’s component line-item
    /// </summary>
    [JsonPropertyName("unit_name")]
    public required string UnitName { get; init; }

    /// <summary>
    /// A description for the component that will be displayed to the user on the hosted signup page.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// A unique identifier for your use that can be used to retrieve this component in subsequent requests. Must start with a letter or number and may only contain lowercase letters, numbers, or the characters '.', ':', '-', or '_'.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("handle")]
    [RegularExpression("^[a-z0-9][a-z0-9\\-_:.]*$")]
    public string? Handle { get; init; }

    /// <summary>
    /// Boolean flag describing whether a component is taxable or not.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("taxable")]
    public bool? Taxable { get; init; }

    /// <summary>
    /// The identifier for the pricing scheme. See <see href="https://help.chargify.com/products/product-components.html">Product Components</see> for an overview of pricing schemes.
    /// </summary>
    [JsonPropertyName("pricing_scheme")]
    public required PricingScheme PricingScheme { get; init; }

    /// <summary>
    /// (Not required for ‘per_unit’ pricing schemes) One or more price brackets. See <see href="https://maxio.zendesk.com/hc/en-us/articles/24261149166733-Component-Pricing-Schemes#price-bracket-rules">Price Bracket Rules</see> for an overview of how price brackets work for different pricing schemes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("prices")]
    public IReadOnlyList<Price>? Prices { get; init; }

    /// <summary>
    /// The type of credit to be created when upgrading/downgrading. Defaults to the component and then site setting if one is not provided.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("upgrade_charge")]
    public CreditType? UpgradeCharge { get; init; }

    /// <summary>
    /// The type of credit to be created when upgrading/downgrading. Defaults to the component and then site setting if one is not provided.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("downgrade_credit")]
    public CreditType? DowngradeCredit { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price_points")]
    public IReadOnlyList<CreatePrepaidUsageComponentPricePoint>? PricePoints { get; init; }

    /// <summary>
    /// The amount the customer will be charged per unit when the pricing scheme is “per_unit”. For On/Off Components, this is the amount that the customer will be charged when they turn the component on for the subscription. The price can contain up to 8 decimal places. e.g., 1.00 or 0.0012 or 0.00000065
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("unit_price")]
    public UnitPrice1? UnitPrice { get; init; }

    /// <summary>
    /// A string representing the tax code related to the component type. This is especially important when using AvaTax to tax based on locale. This attribute has a max length of 25 characters.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tax_code")]
    public string? TaxCode { get; init; }

    /// <summary>
    /// (Only available on Relationship Invoicing sites) Boolean flag describing if the service date range should show for the component on generated invoices.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("hide_date_range_on_invoice")]
    public bool? HideDateRangeOnInvoice { get; init; }

    [JsonPropertyName("overage_pricing")]
    public required OveragePricing OveragePricing { get; init; }

    /// <summary>
    /// Boolean which controls whether or not remaining units should be rolled over to the next period.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("rollover_prepaid_remainder")]
    public bool? RolloverPrepaidRemainder { get; init; }

    /// <summary>
    /// Boolean which controls whether or not the allocated quantity should be renewed at the beginning of each period.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("renew_prepaid_allocation")]
    public bool? RenewPrepaidAllocation { get; init; }

    /// <summary>
    /// (only for prepaid usage components where rollover_prepaid_remainder is true) The number of <c>expiration_interval_unit</c>s after which rollover amounts should expire
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expiration_interval")]
    public double? ExpirationInterval { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expiration_interval_unit")]
    public ExpirationIntervalUnit? ExpirationIntervalUnit { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("display_on_hosted_page")]
    public bool? DisplayOnHostedPage { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("allow_fractional_quantities")]
    public bool? AllowFractionalQuantities { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("public_signup_page_ids")]
    public IReadOnlyList<int>? PublicSignupPageIds { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
