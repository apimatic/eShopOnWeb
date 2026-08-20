using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionPlan> GetPlanAsync(string productHandle, CancellationToken cancellationToken);
    Task EnsureCustomerAsync(BillingCustomerProfile profile, CancellationToken cancellationToken);
    Task<SubscriptionDetails?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<SubscriptionDetails> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken);
}

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionEnrollmentResult> SubscribeAsync(string userId, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(string userId, CancellationToken cancellationToken);
}
