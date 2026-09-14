using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Identity of an eShopOnWeb user as it is represented in Maxio. The reference is the
/// application user id and is unique per Maxio site.
/// </summary>
public sealed record MaxioCustomerProfile(string Reference, string Email, string FirstName, string LastName);

/// <summary>
/// A subscription plan (Maxio product) that a shopper can subscribe to.
/// </summary>
/// <param name="Handle">Maxio product handle.</param>
/// <param name="Name">Display name.</param>
/// <param name="PriceInCents">Recurring amount in cents (default price point).</param>
/// <param name="IntervalCount">Length of one billing period (e.g. 1).</param>
/// <param name="IntervalUnit">Unit of the billing period (wire value, e.g. "month").</param>
public sealed record MaxioPlan(
    string Handle,
    string Name,
    long? PriceInCents,
    int? IntervalCount,
    string? IntervalUnit);

/// <summary>
/// A Maxio subscription as surfaced to the caller.
/// </summary>
public sealed record MaxioSubscription(
    long Id,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    int? IntervalCount,
    string? IntervalUnit,
    string? State,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt);

public sealed record MaxioSubscribeResult(MaxioSubscription Subscription, bool Created);

public interface IMaxioSubscriptionService
{
    /// <summary>Lists the subscription plans available on the configured Maxio product family.</summary>
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="customer"/> (idempotent) and
    /// subscribes them to the plan identified by <paramref name="planHandle"/>. When the
    /// customer already holds the plan (a duplicate submit), the existing subscription is
    /// returned with <see cref="MaxioSubscribeResult.Created"/> set to false.
    /// </summary>
    Task<MaxioSubscribeResult> SubscribeAsync(MaxioCustomerProfile customer, string planHandle, CancellationToken cancellationToken);

    /// <summary>Returns all subscriptions of the Maxio customer identified by <paramref name="customer"/>.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(MaxioCustomerProfile customer, CancellationToken cancellationToken);
}
