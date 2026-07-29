using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A thin, low-level client over the Maxio Advanced Billing REST API. Each method maps to a
/// single Maxio endpoint and returns domain projections. Implementations translate transport
/// and Maxio-side failures into <see cref="MaxioApiException"/>.
/// </summary>
public interface IMaxioClient
{
    /// <summary>Lists the products (plans) that belong to the given product family handle.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListProductFamilyPlansAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>Looks up a customer by its unique reference. Returns <c>null</c> when none exists.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Creates a new Maxio customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(NewCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>Creates a new subscription for an existing customer identified by reference.</summary>
    Task<SubscriptionSummary> CreateSubscriptionAsync(NewSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>Lists all subscriptions belonging to a Maxio customer.</summary>
    Task<IReadOnlyList<SubscriptionSummary>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
