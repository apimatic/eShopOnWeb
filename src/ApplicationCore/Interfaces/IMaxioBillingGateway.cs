using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Maxio Advanced Billing gateway. Maxio is the system of record for customers, plans, and subscriptions.
/// </summary>
public interface IMaxioBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlan>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken);

    Task<MaxioProduct?> GetProductByHandleAsync(string productHandle, CancellationToken cancellationToken);

    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomer> CreateCustomerAsync(NewMaxioCustomer customer, string uniquenessToken, CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);

    Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<CustomerSubscription> CreateSubscriptionAsync(NewMaxioSubscription subscription, string uniquenessToken, CancellationToken cancellationToken);
}

public sealed record MaxioProduct(SubscriptionPlan Plan, string? ProductFamilyHandle);

public sealed record MaxioCustomer(int Id, string Reference, string Email, string FirstName, string LastName);

public sealed record NewMaxioCustomer(string Reference, string Email, string FirstName, string LastName);

public sealed record NewMaxioSubscription(int CustomerId, string ProductHandle, string Reference);
