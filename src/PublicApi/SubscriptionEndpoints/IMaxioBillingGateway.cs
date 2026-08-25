using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal interface IMaxioBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(BillingUser user, string reference, CancellationToken cancellationToken);
    Task<SubscriptionDto?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<SubscriptionDto> CreateSubscriptionAsync(string customerReference, string productHandle, string reference, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}
