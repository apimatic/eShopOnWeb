using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscriptionEnrollment> SubscribeAsync(string subscriberReference, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string subscriberReference, CancellationToken cancellationToken = default);
}

public sealed class SubscriptionEnrollment
{
    public SubscriptionDto Subscription { get; set; } = new();

    public bool Created { get; set; }
}
