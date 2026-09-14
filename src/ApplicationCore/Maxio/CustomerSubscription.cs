using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A Maxio subscription belonging to the current eShopOnWeb user.
/// </summary>
public class CustomerSubscription
{
    public long SubscriptionId { get; init; }
    public string State { get; init; } = string.Empty;
    public SubscriptionPlan Plan { get; init; } = new();
    public DateTimeOffset? NextBillingAt { get; init; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }
}
