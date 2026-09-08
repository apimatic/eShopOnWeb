using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A plan (Maxio Advanced Billing product) that shoppers can subscribe to.
/// </summary>
public record SubscriptionPlanSummary(
    string Handle,
    string Name,
    string? Description,
    int PriceInCents,
    int BillingInterval,
    string BillingIntervalUnit,
    bool RequiresPaymentMethod);

/// <summary>
/// A subscription as recorded by the billing system of record (Maxio Advanced Billing).
/// </summary>
public record SubscriptionDetails(
    int Id,
    string State,
    string PlanHandle,
    string PlanName,
    int PriceInCents,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CanceledAt);

/// <summary>
/// The result of subscribing a user to a plan.
/// </summary>
public record SubscriptionResult(SubscriptionDetails Subscription, bool AlreadySubscribed);

/// <summary>
/// Identifies the eShopOnWeb user enrolling in a plan. The user reference is used as the
/// unique customer reference in the billing system, guaranteeing one billing customer per user.
/// </summary>
public record SubscribeCommand(
    string UserReference,
    string Email,
    string FirstName,
    string LastName,
    string PlanHandle);

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanSummary>> GetPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Enrolls the user in the given plan. Idempotent: if the user already has a live
    /// subscription to the same plan, the existing subscription is returned unchanged.
    /// </summary>
    Task<SubscriptionResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken);

    /// <summary>
    /// Returns all subscriptions the user holds in the billing system.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetails>> GetMySubscriptionsAsync(string userReference, CancellationToken cancellationToken);
}
