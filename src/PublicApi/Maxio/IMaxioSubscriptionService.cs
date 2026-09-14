using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Facade over Maxio Advanced Billing for the subscription capability. This is the only place
/// that talks to Maxio; SDK types never leak past it.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>Lists the saleable plans of the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Enrolls the shopper: ensures a Maxio customer exists for them (idempotent) and creates a
    /// subscription to the given plan handle without capturing a payment method. When the shopper
    /// already has a non-terminal subscription to the same plan, that subscription is returned
    /// instead of creating a duplicate.
    /// </summary>
    Task<SubscriptionEnrollment> SubscribeAsync(MaxioShopper shopper, string planHandle, CancellationToken cancellationToken);

    /// <summary>Lists the shopper's subscriptions (empty when they have never subscribed).</summary>
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(MaxioShopper shopper, CancellationToken cancellationToken);
}
