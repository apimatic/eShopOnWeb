using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The billing provider (system of record for recurring subscriptions).
/// Failures surface as <see cref="Exceptions.BillingProviderException"/>; a write whose outcome could
/// not be settled surfaces as <see cref="Exceptions.BillingOutcomeUnknownException"/>.
/// </summary>
public interface ISubscriptionBillingGateway
{
    Task<BillingPlanCatalog> GetPlansAsync(CancellationToken cancellationToken);

    /// <returns>The customer, or null when the provider has no customer with that reference.</returns>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken);

    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);

    Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle, string reference, CancellationToken cancellationToken);

    /// <returns>The subscription, or null when the provider has no subscription with that reference.</returns>
    Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);
}
