using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record MaxioCustomer(int Id, string Reference);

public interface IMaxioGateway
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionPlanDto> GetPlanAsync(string productHandle, CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> EnsureCustomerAsync(BillingUser user, CancellationToken cancellationToken);
    Task<SubscriptionDto?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
    Task<SubscriptionDto> CreateSubscriptionAsync(
        string customerReference,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken);
}
