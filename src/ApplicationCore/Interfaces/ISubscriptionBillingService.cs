using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Identity details of an eShopOnWeb user, used to provision and correlate their
/// Maxio Advanced Billing customer. The user id is used as the Maxio customer
/// reference, which Maxio enforces as unique — guaranteeing one billing profile per user.
/// </summary>
public record SubscriptionUserInfo(string UserId, string Email, string FirstName, string LastName);

/// <summary>
/// A subscribable plan, read from the Maxio product family configured via Maxio:ProductFamilyHandle.
/// </summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

/// <summary>
/// A subscription as seen in Maxio, confirmed back to the application.
/// </summary>
public record SubscriptionSummary(
    int SubscriptionId,
    string PlanHandle,
    string? PlanName,
    decimal? Price,
    string State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? StartedAt,
    string? Reference);

/// <summary>
/// The result of subscribing a user to a plan.
/// </summary>
public record SubscriptionEnrollmentResult(SubscriptionSummary Subscription, bool AlreadySubscribed);

/// <summary>
/// Application-facing boundary for Maxio Advanced Billing operations. Implementations
/// translate provider failures into <see cref="ApplicationCore.Exceptions.MaxioBillingException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans available for subscription (the products of the configured product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the user to the plan with the given product handle. Idempotent:
    /// if the user already holds a live subscription to the same plan, the existing
    /// subscription is returned with <see cref="SubscriptionEnrollmentResult.AlreadySubscribed"/>
    /// set to true and no second subscription is created.
    /// </summary>
    Task<SubscriptionEnrollmentResult> SubscribeAsync(SubscriptionUserInfo user, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the user's subscriptions in Maxio. Returns an empty list when the user
    /// has no billing profile yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsForUserAsync(SubscriptionUserInfo user, CancellationToken cancellationToken = default);
}
