using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<List<BillingProduct>> ListAvailablePlansAsync(CancellationToken cancellationToken = default);

    Task<BillingSubscription> SubscribeAsync(string userId, string email, string firstName, string lastName, int productId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> GetUserSubscriptionAsync(string userId, int subscriptionId, CancellationToken cancellationToken = default);
    Task<List<BillingSubscription>> GetUserSubscriptionsAsync(string userId, CancellationToken cancellationToken = default);

    Task RecordUsageAsync(string userId, int subscriptionId, int componentId, decimal quantity, string? memo = null, CancellationToken cancellationToken = default);
    Task<decimal> GetUsageTotalAsync(string userId, int subscriptionId, int componentId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ChangeSubscriptionPlanAsync(string userId, int subscriptionId, int newProductId, CancellationToken cancellationToken = default);
    Task<decimal> GetProratedAmountAsync(string userId, int subscriptionId, int newProductId, CancellationToken cancellationToken = default);

    Task PauseSubscriptionAsync(string userId, int subscriptionId, string? reason = null, CancellationToken cancellationToken = default);
    Task ResumeSubscriptionAsync(string userId, int subscriptionId, CancellationToken cancellationToken = default);
    Task CancelSubscriptionAsync(string userId, int subscriptionId, bool cancelImmediately = false, string? reason = null, CancellationToken cancellationToken = default);
    Task ReactivateSubscriptionAsync(string userId, int subscriptionId, CancellationToken cancellationToken = default);
}
