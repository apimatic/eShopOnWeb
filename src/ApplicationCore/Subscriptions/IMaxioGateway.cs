using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Thin gateway over the Maxio Advanced Billing REST API. Each method maps to a single Maxio
/// endpoint and returns domain models. The implementation lives in the Infrastructure layer.
/// </summary>
public interface IMaxioGateway
{
    /// <summary>Lists the products (plans) belonging to the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds a customer by their unique reference, or <c>null</c> if none exists.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Creates a new customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(CustomerRegistration registration, CancellationToken cancellationToken = default);

    /// <summary>Creates a subscription for an existing customer.</summary>
    Task<CustomerSubscription> CreateSubscriptionAsync(NewSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription belonging to a customer.</summary>
    Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);
}
