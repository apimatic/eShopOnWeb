using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a plan, as reported by the billing system of record.
/// </summary>
public sealed record CustomerSubscription(
    int Id,
    string State,
    string PlanHandle,
    string PlanName,
    int PriceInCents,
    string FormattedPrice,
    int Interval,
    string IntervalUnit,
    string Currency,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextBillingAt,
    int CustomerId,
    DateTimeOffset? CreatedAt);
