using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public record SubscribeOutcome(SubscriptionDto Subscription, bool AlreadyExisted);

public interface ISubscriptionService
{
    /// <summary>List the subscribable plans from the configured Maxio product family.</summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently subscribe a user to a plan: ensures a Maxio customer exists for the user,
    /// returns the existing live subscription when one is already present, otherwise creates it.
    /// Returns null when the plan handle is not part of the configured product family.
    /// </summary>
    Task<SubscribeOutcome?> SubscribeAsync(string username, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>List the user's subscriptions; empty when the user has no Maxio customer yet.</summary>
    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string username, CancellationToken cancellationToken = default);
}
