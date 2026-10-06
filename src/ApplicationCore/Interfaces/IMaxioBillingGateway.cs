using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin, resource-oriented façade over the Maxio Advanced Billing REST API.
/// Implementations translate between the billing system's wire contract and
/// the application's subscription model.
/// </summary>
public interface IMaxioBillingGateway
{
    /// <summary>
    /// Lists the non-archived products (plans) of the given product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetFamilyPlansAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the customer whose site-unique reference equals <paramref name="reference"/>,
    /// or null when no such customer exists yet.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    Task<MaxioCustomer> CreateCustomerAsync(
        CreateMaxioCustomerRequest request,
        CancellationToken cancellationToken = default);

    Task<MaxioSubscription> CreateSubscriptionAsync(
        CreateMaxioSubscriptionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription that belongs to the given customer id.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default);
}
