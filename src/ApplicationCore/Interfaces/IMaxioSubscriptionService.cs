using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Use-case level orchestration for the eShopOnWeb subscribe flow: ensures a Maxio customer
/// exists for the buyer, enrolls them in a plan, and reports back on their subscriptions.
/// </summary>
public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<MaxioPlanDto>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotent: repeated calls for the same buyer and plan while a live subscription already
    /// exists return that subscription instead of creating a duplicate.
    /// </summary>
    Task<MaxioSubscriptionDto> SubscribeAsync(string buyerId, string email, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscriptionDto>> GetMySubscriptionsAsync(string buyerId, CancellationToken cancellationToken = default);
}
