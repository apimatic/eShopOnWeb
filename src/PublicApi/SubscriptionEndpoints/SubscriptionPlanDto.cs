using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan from the Maxio catalog.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>
    /// Stable plan identity; send as <see cref="CreateSubscriptionRequest.ProductHandle"/> to subscribe.
    /// </summary>
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Recurring price in cents.</summary>
    public long PriceInCents { get; set; }
    /// <summary>Billing interval count.</summary>
    public int Interval { get; set; }
    /// <summary>Billing interval unit ("day" or "month").</summary>
    public string IntervalUnit { get; set; } = string.Empty;
}
