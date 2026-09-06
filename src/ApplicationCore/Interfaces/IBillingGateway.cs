using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The port onto the external subscription-billing system. Implementations translate these calls
/// into billing-provider API requests and normalise provider failures onto
/// <see cref="Exceptions.BillingException"/>.
/// </summary>
public interface IBillingGateway
{
    /// <summary>Reads the billing site's own settings, e.g. its currency and invoicing model.</summary>
    Task<BillingSite> GetSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists the plans that eShopOnWeb offers, i.e. the products in the configured family.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the customer carrying <paramref name="reference"/>, or null when none exists.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a customer. Throws <see cref="Exceptions.BillingConflictException"/> when the
    /// reference is already taken, which is how a lost race against a concurrent create surfaces.
    /// </summary>
    Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription belonging to a customer, live or ended.</summary>
    Task<IReadOnlyCollection<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Enrolls a customer on a plan.</summary>
    Task<BillingSubscription> CreateSubscriptionAsync(NewSubscription subscription, CancellationToken cancellationToken = default);
}
