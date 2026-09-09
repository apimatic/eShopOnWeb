using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription as reflected in the buyer's account.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }

    /// <summary>
    /// Customer id in the billing system (Maxio).
    /// </summary>
    public int BillingCustomerId { get; set; }

    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public decimal Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>
    /// When the next billing/assessment occurs in the billing system.
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}
