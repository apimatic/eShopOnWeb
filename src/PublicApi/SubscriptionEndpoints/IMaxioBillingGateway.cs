using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal interface IMaxioBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<MaxioProduct?> FindProductAsync(string productHandle, CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerAsync(string customerReference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(
        BillingUser user,
        string customerReference,
        CancellationToken cancellationToken);
    Task<NoCardPaymentCollectionMethod> ResolveNoCardPaymentCollectionMethodAsync(
        CancellationToken cancellationToken);
    Task<SubscriptionDto?> FindSubscriptionAsync(string subscriptionReference, CancellationToken cancellationToken);
    Task<SubscriptionDto> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        NoCardPaymentCollectionMethod paymentCollectionMethod,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken);
}
