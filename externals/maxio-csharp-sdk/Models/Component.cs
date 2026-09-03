using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record Component
{
    /// <summary>
    /// The unique ID assigned to the component by Chargify. This ID can be used to fetch the component from the API.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    public int? Id { get; init; }

    /// <summary>
    /// The name of the Component, suitable for display on statements. e.g., Text Messages.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// The component API handle
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("handle")]
    public string? Handle { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("pricing_scheme")]
    public PricingScheme? PricingScheme { get; init; }

    /// <summary>
    /// The name of the unit that the component’s usage is measured in. e.g., message
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("unit_name")]
    public string? UnitName { get; init; }

    /// <summary>
    /// The amount the customer will be charged per unit. This field is only populated for ‘per_unit’ pricing schemes, otherwise it may be null.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("unit_price")]
    public string? UnitPrice { get; init; }

    /// <summary>
    /// The id of the Product Family to which the Component belongs
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_family_id")]
    public int? ProductFamilyId { get; init; }

    /// <summary>
    /// The name of the Product Family to which the Component belongs
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_family_name")]
    public string? ProductFamilyName { get; init; }

    /// <summary>
    /// The handle of the Product Family to which the Component belongs
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("product_family_handle")]
    public string? ProductFamilyHandle { get; init; }

    /// <summary>
    /// deprecated - use unit_price instead.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price_per_unit_in_cents")]
    public long? PricePerUnitInCents { get; init; }

    /// <summary>
    /// A handle for the component type
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("kind")]
    public ComponentKind? Kind { get; init; }

    /// <summary>
    /// Boolean flag describing whether a component is archived or not.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("archived")]
    public bool? Archived { get; init; }

    /// <summary>
    /// The description of the component.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("default_price_point_id")]
    public int? DefaultPricePointId { get; init; }

    /// <summary>
    /// Applicable only to prepaid usage components. An array of overage price brackets.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("overage_prices")]
    public IReadOnlyList<ComponentPrice?>? OveragePrices { get; init; }

    /// <summary>
    /// An array of price brackets. If the component uses the ‘per_unit’ pricing scheme, this array will be empty.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("prices")]
    public IReadOnlyList<ComponentPrice?>? Prices { get; init; }

    /// <summary>
    /// Count for the number of price points associated with the component
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price_point_count")]
    public int? PricePointCount { get; init; }

    /// <summary>
    /// URL that points to the location to read the existing price points via GET request
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price_points_url")]
    public string? PricePointsUrl { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("default_price_point_name")]
    public string? DefaultPricePointName { get; init; }

    /// <summary>
    /// Boolean flag describing whether a component is taxable or not.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("taxable")]
    public bool? Taxable { get; init; }

    /// <summary>
    /// A string representing the tax code related to the component type. This is especially important when using AvaTax to tax based on locale. This attribute has a max length of 25 characters.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tax_code")]
    public string? TaxCode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("recurring")]
    public bool? Recurring { get; init; }

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

    /// <summary>
    /// Timestamp indicating when this component was created
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// Timestamp indicating when this component was updated
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>
    /// Timestamp indicating when this component was archived
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; init; }

    /// <summary>
    /// (Only available on Relationship Invoicing sites) Boolean flag describing if the service date range should show for the component on generated invoices.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("hide_date_range_on_invoice")]
    public bool? HideDateRangeOnInvoice { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("allow_fractional_quantities")]
    public bool? AllowFractionalQuantities { get; init; }

    /// <summary>
    /// One of the following: Business Software, Consumer Software, Digital Services, Physical Goods, Other
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("item_category")]
    public ItemCategory? ItemCategory { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("use_site_exchange_rate")]
    public bool? UseSiteExchangeRate { get; init; }

    /// <summary>
    /// E.g. Internal ID or SKU Number
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("accounting_code")]
    public string? AccountingCode { get; init; }

    /// <summary>
    /// (Only for Event Based Components) This is an ID of a metric attached to the component. This metric is used to bill upon collected events.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("event_based_billing_metric_id")]
    public int? EventBasedBillingMetricId { get; init; }

    /// <summary>
    /// The numerical interval. e.g., an interval of ‘30’ coupled with an interval_unit of day would mean this component’s default price point would renew every 30 days. This property is only available for sites with Multifrequency enabled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("interval")]
    public int? Interval { get; init; }

    /// <summary>
    /// A string representing the interval unit for this component's default price point, either month or day. This property is only available for sites with Multifrequency enabled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("interval_unit")]
    public IntervalUnit? IntervalUnit { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
