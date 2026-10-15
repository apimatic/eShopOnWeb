using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port to the billing provider (the system of record for subscriptions).
/// Failures surface as <see cref="Exceptions.BillingProviderException"/>; a write whose outcome
/// could not be settled surfaces as <see cref="Exceptions.BillingOutcomeUnknownException"/>.
/// </summary>
public interface ISubscriptionBillingGateway
{
    Task<SubscriptionPlanCatalog> GetPlanCatalogAsync(CancellationToken cancellationToken);

    /// <returns>The provider's customer id, or null when no customer has this reference.</returns>
    Task<int?> FindCustomerIdAsync(string customerReference, CancellationToken cancellationToken);

    /// <summary>Creates the customer; an ambiguous failure is settled by re-reading the reference before this returns.</summary>
    Task<int> CreateCustomerAsync(string customerReference, BillingSubscriber subscriber, CancellationToken cancellationToken);

    Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsAsync(int customerId, CancellationToken cancellationToken);

    /// <summary>Creates the subscription; an ambiguous failure is settled by re-reading the reference before this returns.</summary>
    Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle, string subscriptionReference, CancellationToken cancellationToken);

    /// <returns>The subscription carrying this reference, or null when the provider has none.</returns>
    Task<BillingSubscription?> FindSubscriptionAsync(string subscriptionReference, CancellationToken cancellationToken);
}
