using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the subset of the Maxio Advanced Billing API used by eShopOnWeb.
/// Implemented in Infrastructure against the Maxio OpenAPI specification
/// (see maxio-spec/openapi.yaml - the authoritative contract).
/// </summary>
public interface IMaxioBillingGateway
{
    /// <summary>Plans (products) belonging to the configured product family. GET product_families/handle:{handle}/products.json</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Customer exact match by app reference. GET customers/lookup.json?reference= ; null when absent (404).</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST customers.json. Throws MaxioReferenceConflictException when the reference is taken.</summary>
    Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>GET subscriptions/lookup.json?reference= ; null when absent (404).</summary>
    Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST subscriptions.json. Throws MaxioReferenceConflictException when the reference is taken.</summary>
    Task<BillingSubscription> CreateSubscriptionAsync(NewBillingSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>All subscriptions that belong to a customer. GET customers/{id}/subscriptions.json</summary>
    Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsForCustomerAsync(long customerId, CancellationToken cancellationToken = default);
}
