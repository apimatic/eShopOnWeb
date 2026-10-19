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
    Task<SubscriptionPlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes <paramref name="user"/> to <paramref name="planHandle"/> (or the configured default plan).
    /// Idempotent per (user, plan): a repeat returns the existing subscription instead of creating another.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(BillingUser user, string? planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(BillingUser user, CancellationToken cancellationToken = default);
}

/// <summary>The eShop identity the billing customer is created for.</summary>
/// <param name="UserId">The identity user id; keys this application's local billing records.</param>
/// <param name="CustomerReference">
/// The billing provider's customer reference: a stable identifier for the user that lets an existing
/// provider customer be found again even when local records are lost.
/// </param>
/// <param name="Email">The user's email address.</param>
public record BillingUser(string UserId, string CustomerReference, string Email);

public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    int? Interval,
    string? IntervalUnit);

/// <param name="IsTruncated">True when the provider had more plans than were read; the list is partial.</param>
public record SubscriptionPlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool IsTruncated);

public record SubscriptionDetails(
    int Id,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    decimal? Price,
    string? Currency,
    string? State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CreatedAt);

public enum SubscribeOutcome
{
    /// <summary>A new subscription was created.</summary>
    Created,

    /// <summary>The user already had a live subscription to this plan; it is returned unchanged.</summary>
    AlreadySubscribed,

    /// <summary>The create was sent but its outcome could not be confirmed yet; it is settled on a later request.</summary>
    PendingConfirmation
}

public record SubscribeResult(SubscribeOutcome Outcome, string PlanHandle, string Reference, SubscriptionDetails? Subscription);
