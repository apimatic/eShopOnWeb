using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// The integration layer over the Maxio Advanced Billing SDK. All Maxio interaction goes through
/// here; callers see only DTOs and <see cref="MaxioApiException"/>.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>Lists the subscribable plans (products in the configured product family).</summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct);

    /// <summary>
    /// Ensures a Maxio customer exists for the eShop user (idempotent by stable reference) and
    /// subscribes them to <paramref name="planHandle"/>. A double-click never creates a second
    /// customer or a second live subscription to the same plan.
    /// </summary>
    Task<SubscribeOutcome> SubscribeAsync(string userName, string? planHandle, CancellationToken ct);

    /// <summary>Lists the eShop user's subscriptions (empty when the customer does not yet exist).</summary>
    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userName, CancellationToken ct);
}
