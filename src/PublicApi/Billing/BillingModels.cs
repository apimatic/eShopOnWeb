using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>The shopper a billing call is made for, taken from the authenticated caller.</summary>
public record BillingBuyer(string UserName, string Email);

public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <summary>The plans on offer. <see cref="IsComplete"/> is false when the provider list was cut short.</summary>
public record SubscriptionPlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool IsComplete);

public record SubscriptionDetails(
    int SubscriptionId,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? ActivatedAt);

/// <summary>The outcome of a subscribe call. <see cref="Created"/> is false when an existing subscription was returned.</summary>
public record SubscribeResult(SubscriptionDetails Subscription, bool Created);

/// <summary>
/// The caller's subscriptions. <see cref="PendingPlanHandles"/> lists plans whose subscribe outcome is not settled yet.
/// </summary>
public record BuyerSubscriptions(IReadOnlyList<SubscriptionDetails> Subscriptions, IReadOnlyList<string> PendingPlanHandles);
