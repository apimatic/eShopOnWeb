using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public interface IMaxioBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);

    Task<UserSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);

    Task<UserSubscription> CreateSubscriptionAsync(
        BillingCustomer customer,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<UserSubscription>> ListSubscriptionsAsync(
        string customerReference,
        CancellationToken cancellationToken);
}
