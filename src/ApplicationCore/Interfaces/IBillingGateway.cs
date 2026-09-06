using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port onto the external billing system of record. Implemented in Infrastructure against the
/// Maxio Advanced Billing OpenAPI specification (maxio-spec/openapi.yaml); every method maps onto a
/// single operation in that specification.
/// </summary>
/// <remarks>
/// Read operations that address a single entity return <c>null</c> when the provider answers 404;
/// every other non-success response surfaces as a
/// <see cref="Exceptions.BillingProviderException"/>.
/// </remarks>
public interface IBillingGateway
{
    /// <summary>
    /// Settings of the billing site the credentials point at.
    /// Maps to <c>GET /site.json</c> (operationId <c>readSite</c>).
    /// </summary>
    Task<BillingSiteInfo> GetSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the non-archived plans of the configured product family.
    /// Maps to <c>GET /product_families/{product_family_id}/products.json</c>
    /// (operationId <c>listProductsForProductFamily</c>), addressed by <c>handle:</c> prefix.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a customer by the eShopOnWeb-side reference, or <c>null</c> when none exists.
    /// Maps to <c>GET /customers/lookup.json</c> (operationId <c>readCustomerByReference</c>).
    /// </summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a customer. Maps to <c>POST /customers.json</c> (operationId <c>createCustomer</c>).
    /// </summary>
    Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription belonging to a customer.
    /// Maps to <c>GET /customers/{customer_id}/subscriptions.json</c>
    /// (operationId <c>listCustomerSubscriptions</c>).
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a subscription by its eShopOnWeb-side reference, or <c>null</c> when none exists.
    /// Maps to <c>GET /subscriptions/lookup.json</c> (operationId <c>findSubscription</c>).
    /// </summary>
    Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription. Maps to <c>POST /subscriptions.json</c>
    /// (operationId <c>createSubscription</c>).
    /// </summary>
    Task<CustomerSubscription> CreateSubscriptionAsync(NewSubscription subscription, CancellationToken cancellationToken = default);
}
