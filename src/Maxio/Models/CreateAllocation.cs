using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.AnyOf;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record CreateAllocation
{
    /// <summary>
    /// The allocated quantity to which to set the line-items allocated quantity. By default, this is an integer. If decimal allocations are enabled for the component, it will be a decimal number. For On/Off components, use 1 for on and 0 for off.
    /// </summary>
    [JsonPropertyName("quantity")]
    public required double Quantity { get; init; }

    /// <summary>
    /// Decimal representation of the allocated quantity. Only valid when decimal
    /// allocations are enabled for the component.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("decimal_quantity")]
    public string? DecimalQuantity { get; init; }

    /// <summary>
    /// The quantity that was in effect before this allocation. Responses always
    /// include this value; it may be supplied on preview requests to ensure the
    /// expected change is evaluated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("previous_quantity")]
    public double? PreviousQuantity { get; init; }

    /// <summary>
    /// Decimal representation of <c>previous_quantity</c>. Only valid when decimal
    /// allocations are enabled for the component.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("decimal_previous_quantity")]
    public string? DecimalPreviousQuantity { get; init; }

    /// <summary>
    /// (required for the multiple allocations endpoint) The id associated with the component for which the allocation is being made.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("component_id")]
    public int? ComponentId { get; init; }

    /// <summary>
    /// A memo to record along with the allocation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("memo")]
    public string? Memo { get; init; }

    /// <summary>
    /// The scheme used if the proration is a downgrade. Defaults to the site setting if one is not provided.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("proration_downgrade_scheme")]
    public string? ProrationDowngradeScheme { get; init; }

    /// <summary>
    /// The scheme used if the proration is an upgrade. Defaults to the site setting if one is not provided.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("proration_upgrade_scheme")]
    public string? ProrationUpgradeScheme { get; init; }

    /// <summary>
    /// The type of credit to be created when upgrading/downgrading. Defaults to the component and then site setting if one is not provided. Values are:
    /// <para>
    /// <c>full</c> -  A full price credit is added for the amount owed.
    /// </para>
    /// <para>
    /// <c>prorated</c> - A prorated credit is added for the amount owed.
    /// </para>
    /// <para>
    /// <c>none</c> - No charge is added.
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("downgrade_credit")]
    public DowngradeCreditCreditType? DowngradeCredit { get; init; }

    /// <summary>
    /// The type of credit to be created when upgrading/downgrading. Defaults to the component and then site setting if one is not provided. Values are:
    /// <para>
    /// <c>full</c> - A charge is added for the full price of the component.
    /// </para>
    /// <para>
    /// <c>prorated</c> - A charge is added for the prorated price of the component change.
    /// </para>
    /// <para>
    /// <c>none</c> - No charge is added.
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("upgrade_charge")]
    public UpgradeChargeCreditType? UpgradeCharge { get; init; }

    /// <summary>
    /// "If the change in cost is an upgrade, this determines if the charge should accrue to the next renewal or if capture should be attempted immediately.
    /// <para>
    /// <c>true</c> - Attempt to charge the customer at the next renewal.
    /// </para>
    /// <para>
    /// <c>false</c> - Attempt to charge the customer right away. If it fails, the charge will be accrued until the next renewal.
    /// </para>
    /// <para>
    /// Defaults to the site setting if unspecified in the request.
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("accrue_charge")]
    public bool? AccrueCharge { get; init; }

    /// <summary>
    /// If set to true, if the immediate component payment fails, initiate dunning for the subscription.
    /// Otherwise, leave the charges on the subscription to pay for at renewal. Defaults to false.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("initiate_dunning")]
    public bool? InitiateDunning { get; init; }

    /// <summary>
    /// Price point that the allocation should be charged at. Accepts either the price point's id (integer) or handle (string). When not specified, the default price point will be used.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price_point_id")]
    public PricePointId1? PricePointId { get; init; }

    /// <summary>
    /// Billing schedule settings for component allocations or usages on multi-frequency subscriptions. Use this to start a component's billing period on a custom date instead of aligning with the product charge schedule.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing_schedule")]
    public BillingSchedule? BillingSchedule { get; init; }

    /// <summary>
    /// Create or update custom pricing unique to the subscription. Used in place of <c>price_point_id</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("custom_price")]
    public ComponentCustomPrice? CustomPrice { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
