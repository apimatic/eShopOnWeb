using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A plan that can be subscribed to on Maxio.</summary>
public sealed record SubscriptionPlanSummary(
    string Handle,
    string Name,
    int? Interval,
    string? IntervalUnit,
    long? PriceInCents,
    bool RequiresCreditCard,
    bool Archived);

/// <summary>A metered (usage) component available on the product family.</summary>
public sealed record UsageComponentSummary(
    string Handle,
    string? Kind,
    long? PricePerUnitInCents);

/// <summary>Catalog returned by GET /api/subscription-plans.</summary>
public sealed record SubscriptionCatalog(
    IReadOnlyList<SubscriptionPlanSummary> Plans,
    UsageComponentSummary? MeteredComponent);

/// <summary>A subscription of the current user, as known to Maxio.</summary>
public sealed record SubscriptionSummary(
    string? Reference,
    int? MaxioSubscriptionId,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CreatedAt);

/// <summary>Outcome of a subscribe attempt.</summary>
public sealed record SubscribeResult(SubscriptionSummary Subscription, bool Created);

/// <summary>Request to subscribe the identified shopper to a plan.</summary>
public sealed record SubscribeToPlanRequest(
    string UserName,
    string PlanHandle,
    string? FirstName,
    string? LastName);

/// <summary>
/// Billing integration with Maxio Advanced Billing for the recurring-subscription capability.
/// Maxio is the system of record; enrollment rows in the local store are the idempotency gate.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the subscribable plans on the configured product family (plus the metered component, if any).</summary>
    Task<SubscriptionCatalog> GetCatalogAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Subscribes the shopper to a plan. Idempotent for the (shopper, plan) pair: a second call while an
    /// active subscription exists returns the existing subscription instead of creating another.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeToPlanRequest request, CancellationToken cancellationToken);

    /// <summary>Lists the shopper's current Maxio subscriptions.</summary>
    Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken);
}
