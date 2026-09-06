using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port onto the billing system of record. One implementation per provider; the application layer
/// never sees provider payloads, status codes or ids.
/// </summary>
public interface ISubscriptionBillingGateway
{
    /// <summary>Plans offered by the configured product family, cheapest first.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>The plan with this handle, or null when the family does not offer it.</summary>
    Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>The billing customer carrying this reference, or null when there is none.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a billing customer. Implementations must resolve a lost race on the unique reference
    /// by returning the customer that won it rather than failing.
    /// </summary>
    Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>Every subscription held by the customer, newest first.</summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the customer. Throws <see cref="Exceptions.ConcurrentSubscribeException"/> when the
    /// provider recognises the request as a replay of one it is already processing.
    /// </summary>
    Task<CustomerSubscription> CreateSubscriptionAsync(NewSubscription subscription, CancellationToken cancellationToken = default);
}
