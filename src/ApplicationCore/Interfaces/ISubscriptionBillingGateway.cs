using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The billing system of record for recurring subscriptions.
/// Implementations translate every provider failure into <see cref="Exceptions.BillingProviderException"/>
/// (or its <see cref="Exceptions.BillingOutcomeUnknownException"/> subtype for writes that may have landed).
/// </summary>
public interface ISubscriptionBillingGateway
{
    /// <summary>Non-archived plans of the configured product family.</summary>
    Task<BillingPlanCatalog> GetPlansAsync(CancellationToken cancellationToken);

    /// <summary>Returns null when the provider has no customer with this reference.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the customer. If the provider reports the reference as taken, or the outcome of the write is
    /// unknown, the implementation settles it by re-reading the customer by reference.
    /// </summary>
    Task<BillingCustomer> CreateCustomerAsync(BillingCustomerProfile profile, CancellationToken cancellationToken);

    /// <summary>Returns null when the provider has no subscription with this reference.</summary>
    Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the subscription. Throws <see cref="Exceptions.BillingOutcomeUnknownException"/> when the
    /// request may have reached the provider but no answer was received; the caller settles it.
    /// </summary>
    Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle, string reference, CancellationToken cancellationToken);

    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}

public sealed record BillingPlan(
    int Id,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <param name="IsTruncated">True when the provider had more plans than the page cap allowed us to read.</param>
public sealed record BillingPlanCatalog(IReadOnlyList<BillingPlan> Plans, bool IsTruncated);

public sealed record BillingCustomerProfile(string Reference, string Email, string FirstName, string LastName);

public sealed record BillingCustomer(int Id, string? Reference, string? Email);

public sealed record BillingSubscription(
    int Id,
    string? Reference,
    int? CustomerId,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    string? State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? CreatedAt);
