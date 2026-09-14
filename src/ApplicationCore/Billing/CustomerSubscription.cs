using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A subscription held by a customer, as confirmed by the billing system of record.
/// </summary>
public record CustomerSubscription(
    int SubscriptionId,
    string State,
    string? PlanHandle,
    string? PlanName,
    int PriceInCents,
    int Interval,
    string? IntervalUnit,
    int CustomerId,
    string? CustomerReference,
    DateTimeOffset? CurrentPeriodStartedAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CreatedAt)
{
    /// <summary>
    /// True when this subscription already existed (the enrollment was a no-op that returned
    /// the pre-existing subscription rather than creating a new one). Only meaningful on the
    /// result of <see cref="Interfaces.IBillingService.SubscribeAsync"/>.
    /// </summary>
    public bool AlreadyExisted { get; init; }

    /// <summary>The recurring price expressed in major currency units (e.g. 299.00).</summary>
    public decimal Price => PriceInCents / 100m;
}
