using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioSubscriptionService
{
    Task<MaxioSubscriptionPlan[]> GetSubscriptionPlansAsync(CancellationToken ct = default);
    Task<MaxioSubscription?> CreateSubscriptionAsync(string userReference, string productHandle, CancellationToken ct = default);
    Task<MaxioSubscription[]> GetUserSubscriptionsAsync(string userReference, CancellationToken ct = default);
}

public record MaxioSubscriptionPlan(
    int Id,
    string Handle,
    string Name,
    string? Description,
    decimal? Price);

public record MaxioSubscription(
    int Id,
    string State,
    DateTime? NextBillingAt,
    decimal? Balance,
    string? ProductHandle);
