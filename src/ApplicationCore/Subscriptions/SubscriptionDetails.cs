using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription belonging to a customer, as reported by the billing system.
/// </summary>
public record SubscriptionDetails(
    long Id,
    string State,
    string PlanHandle,
    string PlanName,
    decimal Price,
    string Currency,
    int Interval,
    string IntervalUnit,
    DateTimeOffset? CurrentPeriodStartsAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CreatedAt,
    long CustomerId,
    string? CustomerReference,
    string PaymentCollectionMethod)
{
    /// <summary>
    /// True when <see cref="Microsoft.eShopWeb.ApplicationCore.Interfaces.ISubscriptionService.SubscribeAsync"/>
    /// returned an existing subscription instead of creating a new one (idempotent replay
    /// of a subscribe request, e.g. a double-click).
    /// </summary>
    public bool AlreadyExisted { get; init; }
}
