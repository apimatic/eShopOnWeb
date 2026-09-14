using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionServices;

public class SubscriptionEnrollmentResult
{
    public SubscriptionDto Subscription { get; set; } = new();

    public bool Created { get; set; }
}

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);

    Task<SubscriptionEnrollmentResult> SubscribeAsync(string customerReference, string customerEmail, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string customerReference, CancellationToken cancellationToken);
}
