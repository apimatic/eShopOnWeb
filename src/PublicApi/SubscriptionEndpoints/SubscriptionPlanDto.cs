namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>
    /// Stable identifier of the plan, and the value to post back to subscribe to it.
    /// </summary>
    public string Handle { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    /// <summary>Recurring price in major units, for example 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in minor units, exactly as the billing provider reports it.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int IntervalLength { get; set; }

    /// <summary>Billing-period unit, for example "month".</summary>
    public string IntervalUnit { get; set; }

    /// <summary>
    /// Display hint: whether the billing provider expects a payment method to be entered for this
    /// plan. Null when the provider does not state it.
    /// </summary>
    public bool? RequiresPaymentMethod { get; set; }
}
