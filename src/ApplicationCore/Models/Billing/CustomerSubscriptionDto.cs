using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// A shopper's subscription as reported by the billing system of record.
/// </summary>
public class CustomerSubscriptionDto
{
    public int? SubscriptionId { get; set; }
    public string? PlanName { get; set; }
    public string? PlanHandle { get; set; }
    public long? PriceInCents { get; set; }
    public string? State { get; set; }

    /// <summary>
    /// End of the current billing period, i.e. when the next billing occurs.
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; set; }
}
