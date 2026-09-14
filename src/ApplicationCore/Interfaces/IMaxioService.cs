using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A subscribable plan (Maxio Product) belonging to the configured product family.
/// </summary>
public record MaxioPlan(
    int ProductId,
    string ProductHandle,
    string Name,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

/// <summary>
/// A Maxio Customer. <see cref="Reference"/> is the value we set to the eShopOnWeb user's
/// stable identity id, which is what makes customer creation idempotent.
/// </summary>
public record MaxioCustomer(int Id, string Reference, string Email);

/// <summary>
/// A Maxio Subscription, projected down to what eShopOnWeb needs to show a shopper.
/// </summary>
public record MaxioSubscription(
    int Id,
    string State,
    string? ProductHandle,
    string? ProductName,
    long? ProductPriceInCents,
    string? IntervalUnit,
    int? Interval,
    System.DateTimeOffset? CurrentPeriodEndsAt,
    System.DateTimeOffset? NextAssessmentAt);

/// <summary>
/// Talks to Maxio Advanced Billing for the subscription-billing capability. Maxio is the
/// system of record for customers and subscriptions - this service does not cache state
/// locally, it always reflects the live Maxio data.
/// </summary>
public interface IMaxioService
{
    /// <summary>
    /// Lists the active (non-archived) plans in the configured product family.
    /// </summary>
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up an existing Maxio customer by its <c>reference</c>. Returns null if none exists.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given reference, creating one if necessary.
    /// Safe to call repeatedly (e.g. on a double-click) - it will never create a duplicate
    /// customer for the same reference.
    /// </summary>
    Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions belonging to a Maxio customer.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new subscription for the given customer to the given product (plan) handle.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default);
}
