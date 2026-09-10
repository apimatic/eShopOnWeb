using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as recorded by the billing provider (the system of record).
/// </summary>
public record CustomerSubscription(
    int Id,
    string? PlanHandle,
    string? PlanName,
    string State,
    long? PriceInCents,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextBillingDate);
