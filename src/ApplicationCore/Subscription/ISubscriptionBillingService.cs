using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscription;

/// <summary>
/// Identifies the eShopOnWeb user performing a subscription-billing operation.
/// </summary>
public sealed record SubscriberInfo(string UserId, string Email);

/// <summary>
/// Command to subscribe a user to a plan. Either <see cref="PlanHandle"/> or
/// <see cref="PlanId"/> must identify a plan.
/// </summary>
public sealed record SubscribeCommand(SubscriberInfo Subscriber, string? PlanHandle, int? PlanId);

/// <summary>
/// Contract for recurring-subscription billing against the billing system of record.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans available to shoppers.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a billing customer exists for the user (idempotently) and enrolls them in the
    /// requested plan. Double-subscribe attempts return the existing subscription unchanged.
    /// </summary>
    Task<SubscriptionDetails> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the subscriptions the user holds in the billing system.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetails>> ListForUserAsync(SubscriberInfo subscriber, CancellationToken cancellationToken);
}
