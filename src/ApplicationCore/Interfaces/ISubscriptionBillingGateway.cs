using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The billing system of record. Implementations translate every provider failure into
/// <see cref="Exceptions.BillingRequestRejectedException"/>, <see cref="Exceptions.BillingProviderUnavailableException"/>
/// or, for writes whose outcome cannot be known, <see cref="Exceptions.BillingOutcomeUnknownException"/>.
/// </summary>
public interface ISubscriptionBillingGateway
{
    Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <returns>The customer, or null when the billing system has no customer with that reference.</returns>
    Task<BillingCustomerRecord?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<BillingCustomerRecord> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default);

    Task<BillingSubscription> CreateSubscriptionAsync(NewBillingSubscription subscription, CancellationToken cancellationToken = default);

    /// <returns>The subscription, or null when the billing system has no subscription with that reference.</returns>
    Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int billingCustomerId, CancellationToken cancellationToken = default);
}
