using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionPlan?> GetPlanAsync(string productHandle, CancellationToken cancellationToken);
    Task EnsureCustomerAsync(SubscriptionCustomer customer, CancellationToken cancellationToken);
    Task<SubscriptionDetails?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<SubscriptionDetails> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken);
}

