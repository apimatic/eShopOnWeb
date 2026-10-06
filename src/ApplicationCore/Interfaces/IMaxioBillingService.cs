using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Identifies the eShopOnWeb user whose Maxio customer is being provisioned.
/// </summary>
/// <param name="CustomerReference">
/// A stable, unique reference for the user in Maxio (deterministic per eShopOnWeb user id).
/// </param>
/// <param name="Email">The user's email address (required by Maxio customer creation).</param>
/// <param name="FirstName">A display first name.</param>
/// <param name="LastName">A display last name.</param>
public record MaxioSubscriber(
    string CustomerReference,
    string Email,
    string FirstName,
    string LastName);

/// <summary>
/// A subscribable plan exposed by the billing system of record.
/// </summary>
public record MaxioPlan(
    string Handle,
    string Name,
    decimal Price,
    int Interval,
    string IntervalUnit);

/// <summary>
/// A subscription state as recorded in Maxio Advanced Billing.
/// </summary>
public record MaxioSubscription(
    int SubscriptionId,
    string PlanHandle,
    string PlanName,
    decimal Price,
    string State,
    DateTimeOffset? NextBillingDate,
    string CustomerReference,
    bool AlreadySubscribed);

/// <summary>
/// The Maxio Advanced Billing integration boundary for recurring subscriptions.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the subscribable plans of the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Idempotently ensures a Maxio customer exists for the subscriber, then idempotently
    /// enrolls them in the plan identified by <paramref name="planHandle"/>.
    /// </summary>
    Task<MaxioSubscription> SubscribeAsync(MaxioSubscriber subscriber, string planHandle, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the subscriptions Maxio holds for the user identified by the given customer reference.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListMySubscriptionsAsync(string customerReference, CancellationToken cancellationToken);
}