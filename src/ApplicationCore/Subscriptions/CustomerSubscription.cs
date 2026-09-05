using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A Maxio subscription belonging to an eShopOnWeb buyer.
/// </summary>
public class CustomerSubscription
{
    public required long SubscriptionId { get; init; }
    public required string PlanHandle { get; init; }
    public required string PlanName { get; init; }
    public int PriceInCents { get; init; }
    public required string State { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
