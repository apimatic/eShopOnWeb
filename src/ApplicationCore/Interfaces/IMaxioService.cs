using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioService
{
    Task<MaxioSubscriptionPlan[]> GetSubscriptionPlansAsync(CancellationToken cancellationToken = default);
    Task<MaxioSubscriptionResponse> CreateSubscriptionAsync(string userId, string email, int planId, CancellationToken cancellationToken = default);
    Task<MaxioSubscription[]> GetSubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
}

public record MaxioSubscriptionPlan(
    int Id,
    string Handle,
    string Name,
    string Description,
    decimal Price,
    string BillingCycle
);

public record MaxioSubscriptionResponse(
    int SubscriptionId,
    int CustomerId,
    string Status,
    DateTime? NextBillingDate
);

public record MaxioSubscription(
    int Id,
    int CustomerId,
    int ProductId,
    string Status,
    DateTime CreatedAt,
    DateTime? CanceledAt,
    DateTime? NextBillingDate,
    decimal CurrentPrice
);
