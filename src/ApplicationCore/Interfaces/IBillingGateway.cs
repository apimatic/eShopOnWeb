using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port onto the recurring-billing system of record (Maxio Advanced Billing).
/// Implementations live in Infrastructure and own all transport concerns; everything
/// above this interface is expressed in eShopOnWeb terms.
/// </summary>
public interface IBillingGateway
{
    /// <summary>Plans on offer, scoped to the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The plan with this handle within the configured product family, or null.
    /// Family-scoped on purpose: a handle from another family must not be subscribable here.
    /// </summary>
    Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>The billing customer carrying this reference, or null when there is none.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a billing customer. Throws <see cref="Exceptions.BillingGatewayException"/> with
    /// <c>IsDuplicateReference</c> set when the reference is already taken.
    /// </summary>
    Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription. Throws <see cref="Exceptions.BillingGatewayException"/> with
    /// <c>IsDuplicateReference</c> set when the supplied reference is already taken.
    /// </summary>
    Task<CustomerSubscription> CreateSubscriptionAsync(NewSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>The subscription carrying this reference, or null when there is none.</summary>
    Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);
}
