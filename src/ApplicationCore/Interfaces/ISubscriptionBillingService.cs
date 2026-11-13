using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing. The billing provider is the system of record for plans,
/// customers and subscriptions; this application keeps only the claims that make subscribe
/// idempotent.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>The plans offered for subscription (the configured product family).</summary>
    Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for <paramref name="user"/> and enrolls it in the plan.
    /// Idempotent per (user, plan): repeating the call returns the existing subscription.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(BillingUser user, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>The caller's subscriptions as the billing provider holds them.</summary>
    Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsAsync(BillingUser user, CancellationToken cancellationToken = default);
}

/// <summary>The authenticated eShopOnWeb user, as billing needs it.</summary>
public sealed record BillingUser(string UserName, string Email);

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <param name="Plans">The plans read.</param>
/// <param name="Truncated">
/// True when the listing stopped at its page cap with more pages possibly left: the list may be incomplete.
/// </param>
public sealed record SubscriptionPlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool Truncated);

public sealed record SubscriptionSummary(
    int Id,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    string? State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? CreatedAt);

/// <param name="Subscription">The subscription the provider holds.</param>
/// <param name="Created">True when this call created it; false when an existing one was returned.</param>
public sealed record SubscribeResult(SubscriptionSummary Subscription, bool Created);
