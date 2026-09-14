using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription as it exists in Maxio (the billing system of record), reflecting the
/// current plan, price, state and next billing date.
/// </summary>
public class SubscriptionDto
{
    public long SubscriptionId { get; set; }

    public string? State { get; set; }

    public long BalanceInCents { get; set; }

    public string? Currency { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>The next billing date (when the current billing period ends).</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public SubscriptionPlanDto? Plan { get; set; }
}
