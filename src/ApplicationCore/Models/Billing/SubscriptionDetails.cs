using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// A shopper's subscription as recorded in the billing system of record.
/// </summary>
public class SubscriptionDetails
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
}
