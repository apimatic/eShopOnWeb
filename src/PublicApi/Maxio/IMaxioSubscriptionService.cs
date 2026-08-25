using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Idempotently subscribes a user to a plan: finds-or-creates the Maxio
    /// customer (reference = eShopOnWeb user id), then finds-or-creates the
    /// subscription (deterministic reference "{userId}:{planHandle}").
    /// </summary>
    Task<SubscriptionDto> SubscribeAsync(string userId, string email, string? planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string userId, CancellationToken cancellationToken);
}
