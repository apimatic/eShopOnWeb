using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// A subscription plan (Maxio product) offered to shoppers.
/// </summary>
public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    decimal Price,
    string PriceFormatted,
    int Interval,
    string IntervalUnit,
    string ProductFamilyHandle);

/// <summary>
/// A user's subscription as recorded in Maxio Advanced Billing.
/// </summary>
public sealed record SubscriptionDetails(
    int MaxioSubscriptionId,
    string State,
    string PlanHandle,
    string PlanName,
    decimal Price,
    string PriceFormatted,
    string NextBillingDate,
    string CustomerReference,
    string PaymentCollectionMethod,
    DateTime? ActivatedAt,
    DateTime? CreatedAt);

/// <summary>
/// Result of a subscribe attempt; indicates whether the subscription already
/// existed in Maxio (idempotent re-subscribe) or was newly created.
/// </summary>
public sealed record SubscribeResult(
    SubscriptionDetails Subscription,
    bool WasAlreadySubscribed);

/// <summary>
/// Thrown when a shopper tries to subscribe to a plan handle that does not
/// exist in the configured Maxio product family.
/// </summary>
public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

/// <summary>
/// Recurring-subscription orchestration backed by Maxio Advanced Billing as the
/// billing system of record. The eShopOnWeb user is mirrored to a Maxio customer
/// via a deterministic customer reference; subscriptions are idempotent per
/// (user, plan) through deterministic subscription references plus pre-checks.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans (Maxio products) available in the configured
    /// Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given user (idempotent) and subscribes
    /// them to the plan identified by <paramref name="planHandle"/>. Re-subscribing to
    /// a plan the user already has an active subscription for returns the existing
    /// subscription instead of creating a duplicate.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the user's subscriptions as recorded in Maxio.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken = default);
}