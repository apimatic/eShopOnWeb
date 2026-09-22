using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>A subscription as reflected in the buyer's account, projected from the Maxio subscription.</summary>
public record SubscriptionSummary(
    int? SubscriptionId,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? State,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextBillingAt);
