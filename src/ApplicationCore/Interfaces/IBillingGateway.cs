using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The billing system of record (Maxio Advanced Billing). Every failure surfaces as
/// <see cref="Exceptions.BillingProviderException"/>; the caller's own cancellation surfaces as
/// <see cref="System.OperationCanceledException"/>.
/// </summary>
public interface IBillingGateway
{
    /// <summary>Lists the plans of the configured product family; the result says whether it was cut short.</summary>
    Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>Returns null only when the billing system says no customer has this reference.</summary>
    Task<BillingCustomerAccount?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<BillingCustomerAccount> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken);

    Task<BillingSubscription> CreateSubscriptionAsync(NewBillingSubscription subscription, CancellationToken cancellationToken);

    /// <summary>Returns null only when the billing system says no subscription has this reference.</summary>
    Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}
