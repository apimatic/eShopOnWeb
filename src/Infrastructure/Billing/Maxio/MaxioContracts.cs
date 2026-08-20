using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public sealed record MaxioCustomer(int Id, string Reference);

public sealed record MaxioSubscription(
    SubscriptionDetails Details,
    int CustomerId,
    string CustomerReference,
    string ProductFamilyHandle,
    string Reference);

public interface IMaxioClient
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);
    Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken = default);
    Task<MaxioCustomer> CreateCustomerAsync(SubscriptionUser user, string reference, CancellationToken cancellationToken = default);
    Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
    Task<MaxioSubscription> CreateSubscriptionAsync(string customerReference, string productHandle, string reference, CancellationToken cancellationToken = default);
}
