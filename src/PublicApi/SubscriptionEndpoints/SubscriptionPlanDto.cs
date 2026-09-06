namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier of the plan. Post this value to api/subscriptions to subscribe.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in major currency units, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in minor currency units (cents), e.g. 29900.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Length of a billing period, expressed in <see cref="IntervalUnit"/>s.</summary>
    public int IntervalLength { get; set; }

    /// <summary>Unit of the billing period: "month" or "day".</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>True when the shopper must have a stored payment method before subscribing.</summary>
    public bool PaymentMethodRequired { get; set; }

    public bool HasTrial { get; set; }

    public int? TrialIntervalLength { get; set; }

    public string? TrialIntervalUnit { get; set; }

    /// <summary>The price point the plan is offered on, when the billing system reports one.</summary>
    public string? PricePointHandle { get; set; }

    public string? PricePointName { get; set; }
}
