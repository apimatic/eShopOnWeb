using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionService
{
    /// <summary>Lists the subscribable plans in the configured Maxio product family.</summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves a single plan by its Maxio product handle; null when unknown or archived.</summary>
    Task<SubscriptionPlanDto?> GetPlanAsync(string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently subscribes the identified user to a plan: ensures a Maxio customer exists
    /// (keyed by the user's reference), reuses an existing live subscription to the same plan
    /// when present, and otherwise creates the subscription.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(string username, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists all subscriptions the identified user holds in Maxio.</summary>
    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string username, CancellationToken cancellationToken = default);
}
