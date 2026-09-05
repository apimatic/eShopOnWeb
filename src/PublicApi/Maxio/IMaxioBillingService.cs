using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioBillingService
{
    /// <summary>Lists the subscribable plans for the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="userName"/> (idempotent - a double-click
    /// never creates two customers) and enrolls it on <paramref name="planHandle"/>, returning the
    /// existing subscription for that plan if one already exists instead of creating a duplicate.
    /// </summary>
    /// <param name="userName">The authenticated eShopOnWeb user name, which doubles as email in this app.</param>
    Task<SubscriptionDto> SubscribeAsync(string userName, string planHandle, CancellationToken ct);

    /// <summary>Lists the subscriptions already enrolled for <paramref name="userName"/>, if any.</summary>
    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userName, CancellationToken ct);
}
