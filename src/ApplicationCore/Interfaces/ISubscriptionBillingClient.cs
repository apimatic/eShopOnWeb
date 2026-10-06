using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port over the billing system of record (Maxio Advanced Billing). Implemented in
/// Infrastructure; consumed by <see cref="ISubscriptionService"/>. The implementation
/// is configured with exactly one product family (the sales catalog).
/// </summary>
public interface ISubscriptionBillingClient
{
    /// <summary>Plans currently offered for subscription in the configured product family.</summary>
    Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds the billing customer registered under an application reference, or null.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Registers a new billing customer. The reference must be unique in the billing system.</summary>
    Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>Enrolls an existing billing customer in a plan.</summary>
    Task<BillingSubscription> CreateSubscriptionAsync(NewBillingSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>All subscriptions owned by the given billing customer.</summary>
    Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default);
}
