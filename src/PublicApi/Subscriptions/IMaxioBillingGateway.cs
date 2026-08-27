using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal interface IMaxioBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(BillingCustomerIdentity identity, CancellationToken cancellationToken);
    Task<SubscriptionDto?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<SubscriptionDto> CreateSubscriptionAsync(string productHandle, int customerId,
        string reference, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> GetCustomerSubscriptionsAsync(int customerId,
        CancellationToken cancellationToken);
}
