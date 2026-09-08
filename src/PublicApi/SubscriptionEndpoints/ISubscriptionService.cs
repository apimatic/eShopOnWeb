using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Orchestrates the subscribe flow: ensures a Maxio customer exists for the
/// eShopOnWeb user (idempotently), enrolls them in a plan, and lists their
/// subscriptions.
/// </summary>
public interface ISubscriptionService
{
    Task<SubscriptionDto> SubscribeAsync(string userName, string productHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken);
}
