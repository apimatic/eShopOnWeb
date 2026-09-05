using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port to Maxio Advanced Billing, the system of record for eShopOnWeb's subscription billing.
/// </summary>
public interface IMaxioSubscriptionGateway
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="buyerId"/> and enrolls it in the given
    /// plan. Idempotent: calling this again for a buyer who already has a live subscription to the
    /// same plan (e.g. from a double-click) returns that existing subscription rather than creating
    /// a duplicate.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(string buyerId, string email, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string buyerId, CancellationToken cancellationToken = default);
}
