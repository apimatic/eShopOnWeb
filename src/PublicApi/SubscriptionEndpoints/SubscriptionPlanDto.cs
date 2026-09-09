namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan as exposed over the wire.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable plan handle in the billing system of record; use it to subscribe.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price, in integer cents.</summary>
    public long PriceCents { get; set; }

    /// <summary>Recurring price, formatted (e.g. "299.00").</summary>
    public string Price { get; set; } = string.Empty;

    /// <summary>Billing cadence, e.g. "1 month".</summary>
    public string BillingInterval { get; set; } = string.Empty;

    /// <summary>True when a payment method must be captured to enroll.</summary>
    public bool RequiresPaymentMethod { get; set; }
}
