using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SubscribeResult(SubscriptionInfo Subscription, bool Created);

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken ct = default);

    /// <summary>Idempotent: subscribing again to a plan the user already holds returns the existing subscription.</summary>
    Task<SubscribeResult> SubscribeAsync(string userName, string? email, string planHandle, CancellationToken ct = default);

    Task<IReadOnlyList<SubscriptionInfo>> GetMySubscriptionsAsync(string userName, CancellationToken ct = default);
}
