using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record.
/// Failures surface as <see cref="Exceptions.SubscriptionBillingException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans a shopper can subscribe to.</summary>
    Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for <paramref name="customerReference"/> and enrolls it in
    /// <paramref name="planHandle"/>. Idempotent per (customer, plan): a repeat call returns the existing subscription.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(string customerReference, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions held by <paramref name="customerReference"/>; empty when they have never subscribed.</summary>
    Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <summary>
/// The plans on offer. <see cref="IsTruncated"/> is true when the list was cut short by a safety cap,
/// so callers know the set is incomplete.
/// </summary>
public sealed record SubscriptionPlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool IsTruncated);

public sealed record SubscriptionDetails(
    int SubscriptionId,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    string? State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CreatedAt);

/// <param name="Created">False when the subscription already existed and was returned instead of creating another.</param>
public sealed record SubscribeResult(SubscriptionDetails Subscription, bool Created);
