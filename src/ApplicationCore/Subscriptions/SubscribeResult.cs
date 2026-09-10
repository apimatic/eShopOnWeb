using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The outcome of a subscribe operation, confirming plan/price/state/next-billing-date back to the caller.
/// <see cref="AlreadySubscribed"/> is <c>true</c> when an existing live subscription to the plan was
/// returned instead of creating a new one (idempotent subscribe — e.g. a double-click).
/// </summary>
public record SubscribeResult(
    int SubscriptionId,
    int CustomerId,
    string PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string State,
    DateTimeOffset? NextBillingDate,
    bool AlreadySubscribed);
