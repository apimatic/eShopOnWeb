using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription belonging to the current user, as reflected in Maxio.
/// </summary>
public record CustomerSubscription(
    int Id,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string State,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextBillingDate);
