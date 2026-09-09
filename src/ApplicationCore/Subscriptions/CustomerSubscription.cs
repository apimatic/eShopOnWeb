using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A customer's subscription as recorded in Maxio, projected to the fields eShopOnWeb surfaces
/// back to the shopper: which plan, its price, the subscription state, and the next billing date.
/// </summary>
public record CustomerSubscription(
    int Id,
    string? PlanHandle,
    string? PlanName,
    string? State,
    long? PriceInCents,
    string? FormattedPrice,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? ActivatedAt,
    string? Reference);
