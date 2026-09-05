using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A buyer's subscription, as read from Maxio (the system of record for subscription state).
/// </summary>
public class CustomerSubscription
{
    public long SubscriptionId { get; init; }
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public decimal PriceAmount { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
