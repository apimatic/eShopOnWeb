using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A customer's subscription as confirmed back by the billing provider. <see cref="State"/> and
/// <see cref="IntervalUnit"/>-style values are the provider's raw wire values (e.g. "active"), kept
/// as strings so the application layer never depends on the SDK's enum types.
/// </summary>
public record CustomerSubscription(
    int SubscriptionId,
    string State,
    string? PlanHandle,
    string? PlanName,
    long PriceInCents,
    DateTimeOffset? CurrentPeriodEndsAt);
