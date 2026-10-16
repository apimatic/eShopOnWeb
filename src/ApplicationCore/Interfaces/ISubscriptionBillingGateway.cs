using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The billing system of record for subscriptions. Implementations translate every provider failure into
/// <see cref="Exceptions.BillingException"/>, and never report a write as failed when it may have landed:
/// such writes come back as <see cref="WriteOutcome.Unknown"/> after the implementation tried to settle them.
/// </summary>
public interface ISubscriptionBillingGateway
{
    /// <summary>Handle of the product family whose plans this store sells.</summary>
    string ProductFamilyHandle { get; }

    Task<PlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<BillingCustomerAccount?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<CustomerCreateResult> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default);

    Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<BillingSubscription?> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<SubscriptionCreateResult> CreateSubscriptionAsync(NewBillingSubscription subscription, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
