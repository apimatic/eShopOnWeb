using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The billing system of record, expressed in eShopOnWeb's own vocabulary. Implemented by the Maxio
/// Advanced Billing adapter in the Infrastructure project; every method maps onto an operation
/// declared in the Maxio OpenAPI specification.
/// </summary>
public interface ISubscriptionBillingGateway
{
    /// <summary>Plans on offer, i.e. the products of the configured Maxio product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>The plan with the given handle, or null when the family does not offer it.</summary>
    Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>The customer carrying <paramref name="reference"/>, or null when none exists.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a customer. Throws <see cref="Exceptions.BillingReferenceConflictException"/> when the
    /// reference has already been taken.
    /// </summary>
    Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>Every subscription belonging to a customer, in any state.</summary>
    Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>The subscription carrying <paramref name="reference"/>, or null when none exists.</summary>
    Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls a customer in a plan. Throws
    /// <see cref="Exceptions.BillingReferenceConflictException"/> when the reference has already been
    /// taken.
    /// </summary>
    Task<CustomerSubscription> CreateSubscriptionAsync(NewSubscription subscription, CancellationToken cancellationToken = default);
}
