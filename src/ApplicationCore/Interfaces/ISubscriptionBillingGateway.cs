using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The recurring-billing system of record. Implementations translate every provider failure into
/// <see cref="Exceptions.BillingRejectedException"/>, <see cref="Exceptions.BillingUnavailableException"/>
/// or, for a write whose outcome could not be settled, <see cref="Exceptions.BillingOutcomeUnknownException"/>.
/// </summary>
public interface ISubscriptionBillingGateway
{
    /// <summary>Identifies the plan catalog (product family) this gateway sells from.</summary>
    string CatalogKey { get; }

    Task<PlanCatalog> ListPlansAsync(CancellationToken cancellationToken);

    Task<int?> FindCustomerIdAsync(string customerReference, CancellationToken cancellationToken);

    /// <summary>Returns the billing id of the customer with <see cref="BillingCustomer.Reference"/>, creating it if absent.</summary>
    Task<int> EnsureCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken);

    Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string subscriptionReference, CancellationToken cancellationToken);

    Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle, string subscriptionReference, CancellationToken cancellationToken);

    Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken);
}
